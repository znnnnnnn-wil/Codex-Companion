using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using CodexCompanion.Bridge.Codex.AppServer;
using CodexCompanion.Bridge.Codex.Desktop;
using CodexCompanion.Bridge.Codex.History;
using CodexCompanion.Bridge.Codex.Models;
using CodexCompanion.Bridge.Configuration;
using CodexCompanion.Bridge.Pairing;
using CodexCompanion.Bridge.Relay;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodexCompanion.Bridge;

public static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var configuration = BridgeConfiguration.Load();
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(ParseLogLevel(configuration.LogLevel));
            builder.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss ";
            });
        });
        var logger = loggerFactory.CreateLogger("Bridge");

        try
        {
            if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
            {
                PrintHelp();
                return 0;
            }

            return args[0] switch
            {
                "threads" => await RunWithHistoryAsync(async adapter =>
                {
                    Console.WriteLine(JsonSerializer.Serialize(await adapter.ListThreadsAsync(), JsonOptions));
                }, configuration, loggerFactory),
                "thread" => await RunThreadAsync(args, configuration, loggerFactory),
                "inspect-ui" => await RunInspectorAsync(args),
                "status" => RunStatus(loggerFactory),
                "setup" => await RunSetupAsync(configuration, loggerFactory),
                "doctor" => await RunDoctorAsync(args, configuration, loggerFactory),
                "send" => await RunSendAsync(args, configuration, loggerFactory),
                "stop" => await RunStopAsync(args, configuration, loggerFactory),
                "run" => await RunBridgeAsync(configuration, loggerFactory),
                _ => throw new ArgumentException($"未知命令：{args[0]}")
            };
        }
        catch (BridgeException exception)
        {
            logger.LogError(exception, "Bridge command failed with {Code}", exception.ProtocolCode);
            Console.Error.WriteLine(JsonSerializer.Serialize(new
            {
                type = "error",
                code = exception.ProtocolCode,
                message = exception.Message
            }, JsonOptions));
            return 2;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Bridge command failed");
            Console.Error.WriteLine(JsonSerializer.Serialize(new
            {
                type = "error",
                code = "INTERNAL_ERROR",
                message = "Bridge 执行失败，请查看本机日志。"
            }, JsonOptions));
            return 1;
        }
    }

    private static async Task<int> RunThreadAsync(string[] args, BridgeConfiguration configuration, ILoggerFactory loggerFactory)
    {
        if (args.Length != 2)
        {
            throw new ArgumentException("用法：thread <thread-id>");
        }
        return await RunWithHistoryAsync(async adapter =>
        {
            Console.WriteLine(JsonSerializer.Serialize(await adapter.ReadThreadAsync(args[1]), JsonOptions));
        }, configuration, loggerFactory);
    }

    private static async Task<int> RunInspectorAsync(string[] args)
    {
        string? outputPath = null;
        for (var index = 1; index < args.Length; index++)
        {
            if (args[index] == "--output" && index + 1 < args.Length)
            {
                outputPath = Path.GetFullPath(args[++index]);
            }
        }

        var tree = new CodexUiInspector().Inspect();
        if (outputPath is null)
        {
            Console.Write(tree);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await File.WriteAllTextAsync(outputPath, tree, new UTF8Encoding(false));
            Console.WriteLine(outputPath);
        }
        return 0;
    }

    private static int RunStatus(ILoggerFactory loggerFactory)
    {
        var driver = new SystemWindowsCodexUiDriver(loggerFactory.CreateLogger<SystemWindowsCodexUiDriver>());
        var adapter = new CodexDesktopAdapter(driver);
        Console.WriteLine(JsonSerializer.Serialize(adapter.GetDesktopStatus(), JsonOptions));
        return 0;
    }

    private static async Task<int> RunSendAsync(string[] args, BridgeConfiguration configuration, ILoggerFactory loggerFactory)
    {
        if (args.Length < 3)
        {
            throw new ArgumentException("用法：send <thread-id> <message>");
        }

        var threadId = args[1];
        var messageParts = new List<string>();
        var attachmentPaths = new List<string>();
        for (var index = 2; index < args.Length; index++)
        {
            if (args[index] == "--attach" && index + 1 < args.Length)
            {
                attachmentPaths.Add(Path.GetFullPath(args[++index]));
            }
            else
            {
                messageParts.Add(args[index]);
            }
        }
        var text = string.Join(' ', messageParts);
        if (string.IsNullOrWhiteSpace(text) && attachmentPaths.Count == 0)
        {
            throw new ArgumentException("消息和附件不能同时为空。");
        }
        return await RunWithHistoryAsync(async history =>
        {
            var driver = new SystemWindowsCodexUiDriver(loggerFactory.CreateLogger<SystemWindowsCodexUiDriver>());
            var desktop = new CodexDesktopAdapter(driver);
            var coordinator = new MessageSendCoordinator(history, desktop);
            var result = await coordinator.SendAndConfirmAsync(threadId, text, attachmentPaths);
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        }, configuration, loggerFactory);
    }

    private static async Task<int> RunStopAsync(string[] args, BridgeConfiguration configuration, ILoggerFactory loggerFactory)
    {
        if (args.Length != 2)
        {
            throw new ArgumentException("用法：stop <thread-id>");
        }
        return await RunWithHistoryAsync(async history =>
        {
            var thread = (await history.ListThreadsAsync())
                .FirstOrDefault(candidate => candidate.ThreadId == args[1])
                ?? throw new BridgeException(BridgeErrorCode.ThreadNotFound, "找不到指定的 Codex thread。");
            var driver = new SystemWindowsCodexUiDriver(loggerFactory.CreateLogger<SystemWindowsCodexUiDriver>());
            var desktop = new CodexDesktopAdapter(driver);
            await desktop.StopAsync(thread);
            Console.WriteLine(JsonSerializer.Serialize(new { threadId = thread.ThreadId, stopped = true }, JsonOptions));
        }, configuration, loggerFactory);
    }

    private static async Task<int> RunBridgeAsync(BridgeConfiguration configuration, ILoggerFactory loggerFactory)
    {
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += handler;
        try
        {
            var executable = new CodexExecutableResolver().Resolve(configuration.CodexExecutable);
            await using var appServer = await CodexAppServerClient.StartAsync(
                executable,
                loggerFactory.CreateLogger<CodexAppServerClient>(),
                cancellation.Token);
            var history = new CodexHistoryAdapter(appServer);
            var uiDriver = new SystemWindowsCodexUiDriver(loggerFactory.CreateLogger<SystemWindowsCodexUiDriver>());
            var desktop = new CodexDesktopAdapter(uiDriver);
            var relayUrl = configuration.RelayUrl!;
            var relay = new BridgeRelayClient(
                new Uri(relayUrl),
                new BridgeCredentialStore(configuration.CredentialPath),
                history,
                desktop,
                loggerFactory.CreateLogger<BridgeRelayClient>());
            await relay.RunAsync(cancellation.Token);
            return 0;
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }
    }

    private static async Task<int> RunWithHistoryAsync(
        Func<ICodexHistoryAdapter, Task> action,
        BridgeConfiguration configuration,
        ILoggerFactory loggerFactory)
    {
        var executable = new CodexExecutableResolver().Resolve(configuration.CodexExecutable);
        await using var client = await CodexAppServerClient.StartAsync(
            executable,
            loggerFactory.CreateLogger<CodexAppServerClient>());
        var adapter = new CodexHistoryAdapter(client);
        await action(adapter);
        return 0;
    }

    private static LogLevel ParseLogLevel(string? value)
        => Enum.TryParse<LogLevel>(value, true, out var level) ? level : LogLevel.Warning;

    private static async Task<int> RunSetupAsync(BridgeConfiguration configuration, ILoggerFactory loggerFactory)
    {
        Console.WriteLine($"配置文件：{configuration.FilePath}");
        var relay = Prompt("Relay 地址", configuration.RelayUrl);
        if (!BridgeConfiguration.IsValidRelayUri(relay, out _))
        {
            throw new ArgumentException("Relay 地址必须是 ws:// 或 wss:// 开头的完整 URL。");
        }

        var executable = Prompt("Codex CLI 路径（可留空自动探测）", configuration.CodexExecutable);
        if (!string.IsNullOrWhiteSpace(executable) && !File.Exists(executable))
        {
            throw new FileNotFoundException("指定的 Codex CLI 文件不存在。", executable);
        }

        var credential = Prompt("凭据文件路径（可留空使用默认路径）", configuration.CredentialPath);
        configuration.RelayUrl = relay.Trim();
        configuration.CodexExecutable = string.IsNullOrWhiteSpace(executable) ? null : Path.GetFullPath(executable.Trim());
        configuration.CredentialPath = string.IsNullOrWhiteSpace(credential) ? null : Path.GetFullPath(credential.Trim());
        configuration.Save();
        Console.WriteLine("配置已保存。首次运行 Bridge 后，终端会显示配对码。");

        Console.WriteLine("正在检查本机环境（检查失败不会阻止配置保存）...");
        string? resolvedExecutable = null;
        try
        {
            resolvedExecutable = new CodexExecutableResolver().Resolve(configuration.CodexExecutable);
            Console.WriteLine($"[PASS] Codex CLI：{resolvedExecutable}");
            var version = await ReadProcessOutputAsync(resolvedExecutable, "--version");
            Console.WriteLine($"[PASS] Codex 版本：{version.Trim()}");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"[WARN] Codex CLI：{exception.Message}");
        }

        if (resolvedExecutable is not null)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await using var appServer = await CodexAppServerClient.StartAsync(
                    resolvedExecutable,
                    loggerFactory.CreateLogger<CodexAppServerClient>(),
                    timeout.Token);
                Console.WriteLine("[PASS] Codex app-server：初始化成功");
            }
            catch (Exception exception)
            {
                Console.WriteLine($"[WARN] Codex app-server：{exception.Message}");
            }
        }

        if (BridgeConfiguration.IsValidRelayUri(configuration.RelayUrl ?? string.Empty, out var relayUri)
            && relayUri is not null)
        {
            try
            {
                using var socket = new ClientWebSocket();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                await socket.ConnectAsync(relayUri, timeout.Token);
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "setup", CancellationToken.None);
                Console.WriteLine("[PASS] Relay 网络/WSS：WebSocket 可达");
            }
            catch (Exception exception)
            {
                Console.WriteLine($"[WARN] Relay 网络/WSS：{exception.Message}");
            }
        }
        return 0;
    }

    private static string Prompt(string label, string? current)
    {
        var suffix = string.IsNullOrWhiteSpace(current) ? string.Empty : $" [{current}]";
        Console.Write($"{label}{suffix}: ");
        var value = Console.ReadLine();
        return string.IsNullOrWhiteSpace(value) ? current ?? string.Empty : value.Trim();
    }

    private sealed record DoctorCheck(string Name, bool Ok, bool Warning, string Message);

    private static async Task<int> RunDoctorAsync(
        string[] args,
        BridgeConfiguration configuration,
        ILoggerFactory loggerFactory)
    {
        var failed = false;
        var json = args.Skip(1).Any(value => value.Equals("--json", StringComparison.OrdinalIgnoreCase));
        var checks = new List<DoctorCheck>();
        void Report(string name, bool ok, string message, bool warning = false)
        {
            checks.Add(new DoctorCheck(name, ok, warning, message));
            failed |= !ok && !warning;
        }

        var configurationExists = File.Exists(configuration.FilePath);
        Report("配置文件", configurationExists,
            configurationExists ? configuration.FilePath : "未找到，将使用环境变量或默认值。",
            warning: !configurationExists);
        Report("Relay 地址", BridgeConfiguration.IsValidRelayUri(configuration.RelayUrl ?? string.Empty, out var relayUri), configuration.RelayUrl ?? "未配置");
        if (relayUri is not null && relayUri.Scheme.Equals("ws", StringComparison.OrdinalIgnoreCase))
        {
            Report("传输安全", false, "当前使用明文 ws://，个人测试可用，长期运行建议改为 wss://。", warning: true);
        }

        string? executable = null;
        try
        {
            executable = new CodexExecutableResolver().Resolve(configuration.CodexExecutable);
            Report("Codex CLI", true, executable);
        }
        catch (Exception exception)
        {
            Report("Codex CLI", false, exception.Message);
        }

        if (executable is not null)
        {
            try
            {
                var version = await ReadProcessOutputAsync(executable, "--version");
                Report("Codex 版本", true, version.Trim());
            }
            catch (Exception exception)
            {
                Report("Codex 版本", false, exception.Message);
            }

            try
            {
                var diagnosticLogger = json
                    ? NullLoggerFactory.Instance
                    : loggerFactory;
                await using var appServer = await CodexAppServerClient.StartAsync(
                    executable, diagnosticLogger.CreateLogger<CodexAppServerClient>(),
                    new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token);
                Report("Codex app-server", true, "初始化成功");
            }
            catch (Exception exception)
            {
                Report("Codex app-server", false, exception.Message);
            }
        }

        var credentialPath = configuration.CredentialPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexCompanion", "bridge-credential.json");
        Report("Bridge 凭据", File.Exists(credentialPath), credentialPath, warning: true);
        var desktopRunning = Process.GetProcessesByName("Codex").Length > 0;
        Report("Codex Desktop", desktopRunning,
            desktopRunning ? "已发现 Codex 进程。" : "未发现 Codex 进程，请确认 Desktop 已打开并登录。",
            warning: true);

        if (relayUri is not null)
        {
            try
            {
                using var socket = new ClientWebSocket();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                await socket.ConnectAsync(relayUri, timeout.Token);
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "doctor", CancellationToken.None);
                Report("Relay 网络/WSS", true, "WebSocket 握手成功");
            }
            catch (Exception exception)
            {
                Report("Relay 网络/WSS", false, exception.Message);
            }
        }

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                ok = !failed,
                checks
            }, JsonOptions));
        }
        else
        {
            foreach (var check in checks)
            {
                var status = check.Ok ? "PASS" : check.Warning ? "WARN" : "FAIL";
                Console.WriteLine($"[{status}] {check.Name}: {check.Message}");
            }
        }

        return failed ? 1 : 0;
    }

    private static async Task<string> ReadProcessOutputAsync(string executable, string argument)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            Arguments = argument,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        }) ?? throw new InvalidOperationException("无法启动 Codex CLI。");
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException((await process.StandardError.ReadToEndAsync()).Trim());
        }
        return output;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            Codex Companion Bridge

              threads
              thread <thread-id>
              inspect-ui [--output <path>]
              status
              setup
              doctor [--json]
              send <thread-id> <message> [--attach <path>]...
              stop <thread-id>
              run

            环境变量：
              CODEX_EXECUTABLE                独立 codex.exe 路径
              CODEX_COMPANION_LOG_LEVEL       Debug / Information / Warning / Error
              CODEX_COMPANION_RELAY_URL       Relay Bridge WebSocket URL
              CODEX_COMPANION_CREDENTIAL_PATH Bridge 凭据文件路径（可选）
              CODEX_COMPANION_CONFIG_PATH     Bridge 配置文件路径（可选）
            """);
    }
}
