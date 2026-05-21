using Intelibill.Api.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Intelibill.Api.Unit.Tests.Controllers;

public class ConnectivityControllerTests
{
    private readonly ConnectivityController _controller = new();

    [Fact]
    public void Ping_ReturnsOk()
    {
        var result = _controller.Ping();

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public void Ping_ReturnsServerTime()
    {
        var before = DateTimeOffset.UtcNow;

        var result = _controller.Ping();

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<PingResponse>(ok.Value);
        Assert.True(payload.ServerTime >= before);
        Assert.True(payload.ServerTime <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Ping_PayloadHasNoSensitiveData()
    {
        var result = _controller.Ping();

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<PingResponse>(ok.Value);
        // Only ServerTime — no shop, user, or token fields
        var props = typeof(PingResponse).GetProperties();
        Assert.Single(props);
        Assert.Equal(nameof(PingResponse.ServerTime), props[0].Name);
    }
}
