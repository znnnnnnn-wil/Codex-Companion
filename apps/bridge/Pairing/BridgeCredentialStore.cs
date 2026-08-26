using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodexCompanion.Bridge.Pairing;

public sealed record BridgeCredential(string DeviceId, string Credential);

public sealed class BridgeCredentialStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CodexCompanion.Bridge.v1");
    private readonly string _path;

    public BridgeCredentialStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexCompanion",
            "bridge-credential.json");
    }

    public BridgeCredential? Load()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        var stored = JsonSerializer.Deserialize<StoredCredential>(File.ReadAllText(_path));
        if (stored is null || string.IsNullOrWhiteSpace(stored.DeviceId) || string.IsNullOrWhiteSpace(stored.ProtectedCredential))
        {
            return null;
        }

        var encrypted = Convert.FromBase64String(stored.ProtectedCredential);
        var plain = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
        return new BridgeCredential(stored.DeviceId, Encoding.UTF8.GetString(plain));
    }

    public void Save(BridgeCredential credential)
    {
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var plain = Encoding.UTF8.GetBytes(credential.Credential);
        var encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
        CryptographicOperations.ZeroMemory(plain);
        var stored = new StoredCredential(credential.DeviceId, Convert.ToBase64String(encrypted));
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(stored));
        File.Move(temporary, _path, overwrite: true);
    }

    private sealed record StoredCredential(string DeviceId, string ProtectedCredential);
}
