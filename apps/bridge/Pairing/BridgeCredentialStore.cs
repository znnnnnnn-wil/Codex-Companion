using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodexCompanion.Bridge.Pairing;

public sealed record BridgeCredential(string DeviceId, string Credential);

public sealed class CredentialException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class BridgeCredentialStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CodexCompanion.Bridge.v1");
    private readonly string _path;

    public BridgeCredentialStore(string? path = null)
    {
        _path = Path.GetFullPath(string.IsNullOrWhiteSpace(path) ? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexCompanion",
            "bridge-credential.json") : path);
    }

    public string FilePath => _path;

    public BridgeCredential? Load()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var stored = JsonSerializer.Deserialize<StoredCredential>(stream);
            if (stored is null || string.IsNullOrWhiteSpace(stored.DeviceId) || string.IsNullOrWhiteSpace(stored.ProtectedCredential))
            {
                throw new CredentialException("CREDENTIAL_INVALID", "凭据格式损坏：缺少 DeviceId 或 ProtectedCredential。请运行 pair。");
            }

            var encrypted = Convert.FromBase64String(stored.ProtectedCredential);
            var plain = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            try
            {
                var value = Encoding.UTF8.GetString(plain);
                if (string.IsNullOrWhiteSpace(value))
                    throw new CredentialException("CREDENTIAL_INVALID", "凭据内容为空。请运行 pair。");
                return new BridgeCredential(stored.DeviceId, value);
            }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
        catch (Exception exception) when (exception is JsonException or FormatException)
        {
            throw new CredentialException("CREDENTIAL_INVALID", "凭据 JSON 或 Base64 格式损坏。请运行 pair。");
        }
        catch (CryptographicException)
        {
            throw new CredentialException("CREDENTIAL_DECRYPT_FAILED", "凭据无法用当前 Windows 用户的 DPAPI 解密。请运行 pair。");
        }
    }

    // Caller must hold the runtime lease before resetting or writing credentials.
    public string? BackupAndReset()
    {
        if (!File.Exists(_path)) return null;
        var backup = _path + ".backup-" + Guid.NewGuid().ToString("N");
        File.Move(_path, backup);
        return backup;
    }

    public void Save(BridgeCredential credential)
    {
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var plain = Encoding.UTF8.GetBytes(credential.Credential);
        byte[] encrypted;
        try { encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(plain); }
        var stored = new StoredCredential(credential.DeviceId, Convert.ToBase64String(encrypted));
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(stored));
            File.Move(temporary, _path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private sealed record StoredCredential(string DeviceId, string ProtectedCredential);
}
