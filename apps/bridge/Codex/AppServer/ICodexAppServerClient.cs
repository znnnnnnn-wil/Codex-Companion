using System.Text.Json;

namespace CodexCompanion.Bridge.Codex.AppServer;

public interface ICodexAppServerClient
{
    Task<JsonElement> InvokeAsync(
        string method,
        object parameters,
        CancellationToken cancellationToken = default);
}
