using System.Text.Json;
using CodexCompanion.Bridge.Codex.AppServer;
using CodexCompanion.Bridge.Codex.History;

namespace CodexCompanion.Bridge.Tests;

public sealed class CodexHistoryAdapterTests
{
    [Fact]
    public async Task CreateThread_UsesRealThreadStartResponse()
    {
        var client = new FakeAppServerClient();
        var adapter = new CodexHistoryAdapter(client);

        var created = await adapter.CreateThreadAsync("C:\\repo");

        Assert.Equal("created-thread", created.ThreadId);
        Assert.Equal("C:\\repo", created.Cwd);
        Assert.Equal(["thread/start"], client.Methods);
        Assert.Equal("C:\\repo", client.Parameters[0].GetProperty("cwd").GetString());
        Assert.StartsWith("新会话 ", created.Title);
    }

    private sealed class FakeAppServerClient : ICodexAppServerClient
    {
        public List<string> Methods { get; } = [];
        public List<JsonElement> Parameters { get; } = [];

        public Task<JsonElement> InvokeAsync(
            string method,
            object parameters,
            CancellationToken cancellationToken = default)
        {
            Methods.Add(method);
            Parameters.Add(JsonSerializer.SerializeToElement(parameters));
            object result = method switch
            {
                "thread/start" => new
                {
                    thread = new { id = "created-thread" }
                },
                "thread/list" => new
                {
                    data = new[]
                    {
                        new
                        {
                            id = "created-thread",
                            name = "新会话 10:49:00",
                            cwd = "C:\\repo",
                            updatedAt = 1,
                            status = new { type = "notLoaded" },
                            source = "appServer"
                        }
                    }
                },
                _ => new { }
            };
            return Task.FromResult(JsonSerializer.SerializeToElement(result));
        }
    }
}
