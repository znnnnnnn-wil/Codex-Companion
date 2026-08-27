using CodexCompanion.Bridge.Configuration;

namespace CodexCompanion.Bridge.Tests;

public sealed class BridgeConfigurationTests
{
    [Theory]
    [InlineData("ws://127.0.0.1:8080/ws/bridge")]
    [InlineData("wss://companion.example.com/ws/bridge")]
    public void RelayUriAcceptsWebSocketSchemes(string value)
    {
        Assert.True(BridgeConfiguration.IsValidRelayUri(value, out var uri));
        Assert.Equal(value, uri!.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://companion.example.com/ws/bridge")]
    [InlineData("companion.example.com")]
    public void RelayUriRejectsInvalidValues(string value)
    {
        Assert.False(BridgeConfiguration.IsValidRelayUri(value, out var uri));
        Assert.Null(uri);
    }
}
