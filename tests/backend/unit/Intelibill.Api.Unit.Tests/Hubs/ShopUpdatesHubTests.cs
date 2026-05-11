using Intelibill.Api.Hubs;
using Intelibill.Application.Common.Interfaces;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;

namespace Intelibill.Api.Unit.Tests.Hubs;

public class ShopUpdatesHubTests
{
    [Fact]
    public async Task OnConnectedAsync_WithActiveShop_AddsConnectionToGroup()
    {
        var shopId = Guid.NewGuid();
        var sessionContext = Substitute.For<ICurrentSessionContext>();
        sessionContext.ActiveShopId.Returns(shopId);

        var hub = new ShopUpdatesHub(sessionContext);
        var groupManager = Substitute.For<IGroupManager>();
        var callerContext = Substitute.For<HubCallerContext>();
        callerContext.ConnectionId.Returns("connection-1");

        hub.Groups = groupManager;
        hub.Context = callerContext;

        await hub.OnConnectedAsync();

        await groupManager.Received(1).AddToGroupAsync(
            "connection-1",
            ShopUpdatesHub.ActiveShopGroupName(shopId),
            Arg.Any<CancellationToken>());
        callerContext.DidNotReceive().Abort();
    }

    [Fact]
    public async Task OnConnectedAsync_WithoutActiveShop_AbortsConnection()
    {
        var sessionContext = Substitute.For<ICurrentSessionContext>();
        sessionContext.ActiveShopId.Returns((Guid?)null);

        var hub = new ShopUpdatesHub(sessionContext);
        var groupManager = Substitute.For<IGroupManager>();
        var callerContext = Substitute.For<HubCallerContext>();
        callerContext.ConnectionId.Returns("connection-2");

        hub.Groups = groupManager;
        hub.Context = callerContext;

        await hub.OnConnectedAsync();

        await groupManager.DidNotReceiveWithAnyArgs().AddToGroupAsync(default!, default!, default);
        callerContext.Received(1).Abort();
    }
}
