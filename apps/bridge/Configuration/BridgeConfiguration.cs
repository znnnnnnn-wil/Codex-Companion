using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexCompanion.Bridge.Configuration;

public sealed class BridgeConfiguration
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public string? RelayUrl { get; set; }
    public string? CodexExecutable { get; set; }
    public string? CredentialPath { get; set; }
    public string? LogLevel { get; set; }

    [JsonIgnore]
    public string FilePath { get; private set; } = DefaultPath();

    public static BridgeConfiguration Load(string[]? args = null)
    {
        string? Option(string name)
        {
            var index = Array.IndexOf(args ?? [], name);
            if (index < 0) return null;
            if (index + 1 >= args!.Length) throw new ArgumentException($"{name} requires a value.");
            return args[index + 1];
        }
        var path = Option("--config") ?? Environment.GetEnvironmentVariable("CODEX_COMPANION_CONFIG_PATH");
        var configuration = new BridgeConfiguration
        {
            FilePath = string.IsNullOrWhiteSpace(path) ? DefaultPath() : Path.GetFullPath(path.Trim())
        };

        if (File.Exists(configuration.FilePath))
        {
            var loaded = JsonSerializer.Deserialize<BridgeConfiguration>(
                File.ReadAllText(configuration.FilePath), JsonOptions);
            if (loaded is not null)
            {
                configuration.RelayUrl = loaded.RelayUrl;
                configuration.CodexExecutable = loaded.CodexExecutable;
                configuration.CredentialPath = loaded.CredentialPath;
                configuration.LogLevel = loaded.LogLevel;
            }
        }

        configuration.RelayUrl = Option("--relay-url") ?? Environment.GetEnvironmentVariable("CODEX_COMPANION_RELAY_URL")
                                 ?? configuration.RelayUrl
                                 ?? "ws://127.0.0.1:8080/ws/bridge";
        configuration.CodexExecutable = Option("--codex-executable") ?? Environment.GetEnvironmentVariable("CODEX_EXECUTABLE")
                                        ?? configuration.CodexExecutable;
        configuration.CredentialPath = Option("--credential-path") ?? Environment.GetEnvironmentVariable("CODEX_COMPANION_CREDENTIAL_PATH")
                                       ?? configuration.CredentialPath;
        configuration.LogLevel = Option("--log-level") ?? Environment.GetEnvironmentVariable("CODEX_COMPANION_LOG_LEVEL")
                                 ?? configuration.LogLevel
                                 ?? "Warning";
        if (!string.IsNullOrWhiteSpace(configuration.CredentialPath))
            configuration.CredentialPath = Path.GetFullPath(configuration.CredentialPath, Path.GetDirectoryName(configuration.FilePath)!);
        return configuration;
    }

    public void Save()
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(temporary, FilePath, overwrite: true);
    }

    public static string DefaultPath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexCompanion",
            "config.json");

    public static bool IsValidRelayUri(string value, out Uri? uri)
    {
        if (Uri.TryCreate(value.Trim(), UriKind.Absolute, out var parsed)
            && (parsed.Scheme.Equals("ws", StringComparison.OrdinalIgnoreCase)
                || parsed.Scheme.Equals("wss", StringComparison.OrdinalIgnoreCase))
            && !string.IsNullOrWhiteSpace(parsed.Host))
        {
            uri = parsed;
            return true;
        }

        uri = null;
        return false;
    }
}
