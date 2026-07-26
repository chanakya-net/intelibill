using System.Security.Claims;
using Intelibill.Api.Hubs;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;

namespace Intelibill.Api.Unit.Tests.Hubs;

public class ShopUpdatesHubTests
{
    [Fact]
    public async Task OnConnectedAsync_WithActiveShopClaim_AddsConnectionToGroup()
    {
        var shopId = Guid.NewGuid();
        var (hub, groupManager, callerContext) = CreateHub("connection-1", shopId.ToString());

        await hub.OnConnectedAsync();

        await groupManager.Received(1).AddToGroupAsync(
            "connection-1",
            ShopUpdatesHub.ActiveShopGroupName(shopId),
            Arg.Any<CancellationToken>());
        callerContext.DidNotReceive().Abort();
    }

    [Fact]
    public async Task OnConnectedAsync_WithoutActiveShopClaim_AbortsConnection()
    {
        var (hub, groupManager, callerContext) = CreateHub("connection-2", activeShopClaim: null);

        await hub.OnConnectedAsync();

        await groupManager.DidNotReceiveWithAnyArgs().AddToGroupAsync(default!, default!, default);
        callerContext.Received(1).Abort();
    }

    [Fact]
    public async Task OnConnectedAsync_WithUnparseableActiveShopClaim_AbortsConnection()
    {
        var (hub, groupManager, callerContext) = CreateHub("connection-3", "not-a-guid");

        await hub.OnConnectedAsync();

        await groupManager.DidNotReceiveWithAnyArgs().AddToGroupAsync(default!, default!, default);
        callerContext.Received(1).Abort();
    }

    private static (ShopUpdatesHub Hub, IGroupManager Groups, HubCallerContext Context) CreateHub(
        string connectionId,
        string? activeShopClaim)
    {
        var claims = activeShopClaim is null
            ? []
            : new[] { new Claim("active_shop_id", activeShopClaim) };

        var groupManager = Substitute.For<IGroupManager>();
        var callerContext = Substitute.For<HubCallerContext>();
        callerContext.ConnectionId.Returns(connectionId);
        callerContext.User.Returns(new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")));

        var hub = new ShopUpdatesHub
        {
            Groups = groupManager,
            Context = callerContext,
        };

        return (hub, groupManager, callerContext);
    }
}
