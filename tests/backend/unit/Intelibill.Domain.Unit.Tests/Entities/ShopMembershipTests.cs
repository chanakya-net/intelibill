using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Unit.Tests.Entities;

public class ShopMembershipTests
{
    [Fact]
    public void Create_SetsInitialProperties()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();

        var membership = ShopMembership.Create(shopId, userId, ShopRole.Staff, true);

        Assert.Equal(shopId, membership.ShopId);
        Assert.Equal(userId, membership.UserId);
        Assert.Equal(ShopRole.Staff, membership.Role);
        Assert.True(membership.IsDefault);
        Assert.Null(membership.LastUsedAt);
    }

    [Fact]
    public void SetDefault_UpdatesFlag()
    {
        var membership = ShopMembership.Create(Guid.NewGuid(), Guid.NewGuid(), ShopRole.Staff, false);

        membership.SetDefault(true);

        Assert.True(membership.IsDefault);
    }

    [Fact]
    public void SetRole_UpdatesRole()
    {
        var membership = ShopMembership.Create(Guid.NewGuid(), Guid.NewGuid(), ShopRole.Staff, false);

        membership.SetRole(ShopRole.Owner);

        Assert.Equal(ShopRole.Owner, membership.Role);
    }

    [Fact]
    public void MarkUsed_SetsTimestamp()
    {
        var membership = ShopMembership.Create(Guid.NewGuid(), Guid.NewGuid(), ShopRole.Manager, false);
        var before = DateTimeOffset.UtcNow;

        membership.MarkUsed();

        Assert.NotNull(membership.LastUsedAt);
        Assert.True(membership.LastUsedAt >= before);
    }
}