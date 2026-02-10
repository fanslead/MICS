using Mics.LoadTester;

namespace Mics.Tests;

public sealed class LoadTesterOptionsTests
{
    [Fact]
    public void WsUriBuilder_Build_AddsExpectedQueryString()
    {
        var baseUrl = new Uri("ws://localhost:8080/ws");
        var uri = WsUriBuilder.Build(baseUrl, tenantId: "t1", token: "valid:u1", deviceId: "dev1");

        Assert.Equal("ws://localhost:8080/ws?tenantId=t1&token=valid%3Au1&deviceId=dev1", uri.ToString());
    }

    [Fact]
    public void LoadTestOptions_Parse_ParsesMinimalArgsAndDefaults()
    {
        var options = LoadTestOptions.Parse(
            [
                "--url", "ws://localhost:8080/ws",
                "--tenantId", "t1",
                "--connections", "10",
                "--durationSeconds", "5",
            ]);

        Assert.Equal(new Uri("ws://localhost:8080/ws"), options.BaseUrl);
        Assert.Equal("t1", options.TenantId);
        Assert.Equal(10, options.Connections);
        Assert.Equal(0, options.RampSeconds);
        Assert.Equal(5, options.DurationSeconds);
        Assert.Equal(LoadTestMode.ConnectOnly, options.Mode);
        Assert.Equal(0d, options.SendQpsPerConnection);
        Assert.Equal(32, options.PayloadBytes);
        Assert.Equal("valid:", options.TokenPrefix);
        Assert.Equal("dev-", options.DevicePrefix);
        Assert.Null(options.GroupId);
    }
}

