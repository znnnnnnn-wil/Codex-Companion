using System.Text.Json;
using CodexCompanion.Bridge.Codex.AppServer;

namespace CodexCompanion.Bridge.Tests;

public sealed class AccountRateLimitsReaderTests
{
    [Fact]
    public async Task UsesReadOnlyMethodAndProjectsOnlyQuotaFields()
    {
        var client = new FakeClient();
        var result = await new AccountRateLimitsReader(client).ReadAsync();
        Assert.Equal("account/rateLimits/read", client.Method);
        Assert.Single(result.Buckets);
        Assert.Equal(4, result.Buckets[0].Primary!.UsedPercent);
        Assert.Equal(300, result.Buckets[0].Primary!.WindowDurationMins);
        Assert.Equal(1800000000, result.Buckets[0].Primary!.ResetsAt);
        Assert.Null(result.Buckets[0].Secondary);
        Assert.DoesNotContain("secret", JsonSerializer.Serialize(result));
    }

    [Fact]
    public void PrefersMultipleBucketsAndPreservesUnknownValues()
    {
        var result = AccountRateLimitsReader.Parse(JsonDocument.Parse("""
            {"rateLimits":{"primary":{"usedPercent":99}},"rateLimitsByLimitId":{
              "codex":{"primary":{"usedPercent":null,"windowDurationMins":-1,"resetsAt":null}},
              "other":{"secondary":{"usedPercent":100,"windowDurationMins":10080,"resetsAt":1800000000}}}}
            """).RootElement);
        Assert.Equal(2, result.Buckets.Count);
        Assert.Null(result.Buckets[0].Primary!.UsedPercent);
        Assert.Null(result.Buckets[0].Primary!.WindowDurationMins);
        Assert.Equal(100, result.Buckets[1].Secondary!.UsedPercent);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"rateLimits\":null,\"rateLimitsByLimitId\":null}")]
    public void MissingDataIsNotZeroUsage(string json) =>
        Assert.Empty(AccountRateLimitsReader.Parse(JsonDocument.Parse(json).RootElement).Buckets);

    private sealed class FakeClient : ICodexAppServerClient
    {
        public string? Method { get; private set; }
        public Task<JsonElement> InvokeAsync(string method, object parameters, CancellationToken cancellationToken = default)
        {
            Method = method;
            return Task.FromResult(JsonDocument.Parse("""
                {"rateLimits":{"limitId":"codex","primary":{"usedPercent":4,"windowDurationMins":300,"resetsAt":1800000000},"secondary":null,"accessToken":"secret"}}
                """).RootElement.Clone());
        }
    }
}
