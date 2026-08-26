using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using CodexCompanion.Bridge.Codex.Models;
using Microsoft.Extensions.Logging;

namespace CodexCompanion.Bridge.Codex.AppServer;

public sealed class CodexAppServerClient : ICodexAppServerClient, IAsyncDisposable
{
    private readonly Process _process;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _stdoutTask;
    private readonly Task _stderrTask;
    private long _nextRequestId;

    private CodexAppServerClient(Process process, ILogger logger)
    {
        _process = process;
        _logger = logger;
        _stdoutTask = ReadStdoutAsync(_shutdown.Token);
        _stderrTask = ReadStderrAsync(_shutdown.Token);
    }

    public event Action<string, JsonElement>? Notification;

    public static async Task<CodexAppServerClient> StartAsync(
        string executable,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("app-server");
        startInfo.ArgumentList.Add("--stdio");

        try
        {
            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            if (!process.Start())
            {
                throw new InvalidOperationException("Codex app-server 未启动。");
            }

            var client = new CodexAppServerClient(process, logger);
            var initialize = await client.InvokeAsync("initialize", new
            {
                clientInfo = new
                {
                    name = "codex_companion_bridge",
                    title = "Codex Companion Bridge",
                    version = "0.1.0"
                }
            }, cancellationToken);
            await client.SendNotificationAsync("initialized", new { }, cancellationToken);
            logger.LogInformation("Codex app-server initialized: {UserAgent}",
                initialize.TryGetProperty("userAgent", out var userAgent) ? userAgent.GetString() : "unknown");
            return client;
        }
        catch (Exception exception) when (exception is not BridgeException)
        {
            throw new BridgeException(
                BridgeErrorCode.CodexAppServerUnavailable,
                "无法启动或初始化 Codex app-server。",
                exception);
        }
    }

    public async Task<JsonElement> InvokeAsync(
        string method,
        object parameters,
        CancellationToken cancellationToken = default)
    {
        var requestId = Interlocked.Increment(ref _nextRequestId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(requestId, completion))
        {
            throw new InvalidOperationException("重复的 app-server request id。");
        }

        try
        {
            await WriteMessageAsync(new { method, id = requestId, @params = parameters }, cancellationToken);
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        }
        catch (TimeoutException exception)
        {
            throw new BridgeException(
                BridgeErrorCode.CodexAppServerUnavailable,
                $"Codex app-server 请求 {method} 超时。",
                exception);
        }
        finally
        {
            _pending.TryRemove(requestId, out _);
        }
    }

    private Task SendNotificationAsync(string method, object parameters, CancellationToken cancellationToken)
        => WriteMessageAsync(new { method, @params = parameters }, cancellationToken);

    private async Task WriteMessageAsync(object message, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(message);
        await _process.StandardInput.WriteLineAsync(json.AsMemory(), cancellationToken);
        await _process.StandardInput.FlushAsync(cancellationToken);
    }

    private async Task ReadStdoutAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await _process.StandardOutput.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.TryGetProperty("id", out var idElement) && idElement.TryGetInt64(out var id))
                {
                    if (_pending.TryGetValue(id, out var completion))
                    {
                        if (root.TryGetProperty("error", out var error))
                        {
                            completion.TrySetException(new BridgeException(
                                BridgeErrorCode.CodexAppServerUnavailable,
                                $"Codex app-server 返回错误：{SanitizeError(error)}"));
                        }
                        else if (root.TryGetProperty("result", out var result))
                        {
                            completion.TrySetResult(result.Clone());
                        }
                        else
                        {
                            completion.TrySetException(new InvalidDataException("app-server 响应缺少 result。"));
                        }
                    }
                    continue;
                }

                if (root.TryGetProperty("method", out var methodElement))
                {
                    var method = methodElement.GetString() ?? "unknown";
                    var parameters = root.TryGetProperty("params", out var paramsElement)
                        ? paramsElement.Clone()
                        : default;
                    Notification?.Invoke(method, parameters);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            foreach (var completion in _pending.Values)
            {
                completion.TrySetException(exception);
            }
        }
    }

    private async Task ReadStderrAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await _process.StandardError.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                _logger.LogDebug("Codex app-server diagnostic received ({Length} chars)", line.Length);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static string SanitizeError(JsonElement error)
    {
        var code = error.TryGetProperty("code", out var codeElement) ? codeElement.ToString() : "unknown";
        var message = error.TryGetProperty("message", out var messageElement)
            ? messageElement.GetString()
            : "unknown";
        return $"code={code}, message={message}";
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        foreach (var completion in _pending.Values)
        {
            completion.TrySetCanceled();
        }

        try
        {
            _process.StandardInput.Close();
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
        }
        catch (InvalidOperationException)
        {
        }

        try
        {
            await Task.WhenAll(_stdoutTask, _stderrTask).WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }

        _process.Dispose();
        _shutdown.Dispose();
    }
}
