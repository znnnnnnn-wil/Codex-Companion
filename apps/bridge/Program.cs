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
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(ParseLogLevel(Environment.GetEnvironmentVariable("CODEX_COMPANION_LOG_LEVEL")));
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
                }, loggerFactory),
                "thread" => await RunThreadAsync(args, loggerFactory),
                "inspect-ui" => await RunInspectorAsync(args),
                "status" => RunStatus(loggerFactory),
                "send" => await RunSendAsync(args, loggerFactory),
                "stop" => await RunStopAsync(args, loggerFactory),
                "run" => await RunBridgeAsync(loggerFactory),
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

    private static async Task<int> RunThreadAsync(string[] args, ILoggerFactory loggerFactory)
    {
        if (args.Length != 2)
        {
            throw new ArgumentException("用法：thread <thread-id>");
        }
        return await RunWithHistoryAsync(async adapter =>
        {
            Console.WriteLine(JsonSerializer.Serialize(await adapter.ReadThreadAsync(args[1]), JsonOptions));
        }, loggerFactory);
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

    private static async Task<int> RunSendAsync(string[] args, ILoggerFactory loggerFactory)
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
        }, loggerFactory);
    }

    private static async Task<int> RunStopAsync(string[] args, ILoggerFactory loggerFactory)
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
        }, loggerFactory);
    }

    private static async Task<int> RunBridgeAsync(ILoggerFactory loggerFactory)
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
            var executable = new CodexExecutableResolver().Resolve();
            await using var appServer = await CodexAppServerClient.StartAsync(
                executable,
                loggerFactory.CreateLogger<CodexAppServerClient>(),
                cancellation.Token);
            var history = new CodexHistoryAdapter(appServer);
            var uiDriver = new SystemWindowsCodexUiDriver(loggerFactory.CreateLogger<SystemWindowsCodexUiDriver>());
            var desktop = new CodexDesktopAdapter(uiDriver);
            var relayUrl = Environment.GetEnvironmentVariable("CODEX_COMPANION_RELAY_URL")
                ?? "ws://127.0.0.1:8080/ws/bridge";
            var relay = new BridgeRelayClient(
                new Uri(relayUrl),
                new BridgeCredentialStore(Environment.GetEnvironmentVariable("CODEX_COMPANION_CREDENTIAL_PATH")),
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
        ILoggerFactory loggerFactory)
    {
        var executable = new CodexExecutableResolver().Resolve();
        await using var client = await CodexAppServerClient.StartAsync(
            executable,
            loggerFactory.CreateLogger<CodexAppServerClient>());
        var adapter = new CodexHistoryAdapter(client);
        await action(adapter);
        return 0;
    }

    private static LogLevel ParseLogLevel(string? value)
        => Enum.TryParse<LogLevel>(value, true, out var level) ? level : LogLevel.Warning;

    private static void PrintHelp()
    {
        Console.WriteLine("""
            Codex Companion Bridge

              threads
              thread <thread-id>
              inspect-ui [--output <path>]
              status
              send <thread-id> <message> [--attach <path>]...
              stop <thread-id>
              run

            环境变量：
              CODEX_EXECUTABLE                独立 codex.exe 路径
              CODEX_COMPANION_LOG_LEVEL       Debug / Information / Warning / Error
              CODEX_COMPANION_RELAY_URL       Relay Bridge WebSocket URL
              CODEX_COMPANION_CREDENTIAL_PATH Bridge 凭据文件路径（可选）
            """);
    }
}
