using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Options;
using Intelibill.Infrastructure.Services.Tills;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Intelibill.Api.Unit.Tests.Infrastructure.Services.Tills;

public sealed class TillSessionPolicyTests
{
    private readonly IShopRepository _shops = Substitute.For<IShopRepository>();
    private readonly Guid _shop = Guid.NewGuid();
    private readonly Guid _actor = Guid.NewGuid();

    [Fact]
    public void Gate_RequiresBothGlobalAndExactShopAllowlist()
    {
        Assert.False(Create(new()).IsEnabled(_shop));
        Assert.False(Create(new() { Enabled = true }).IsEnabled(_shop));
        Assert.False(Create(new() { AllowedShopIds = [_shop] }).IsEnabled(_shop));
        Assert.True(Create(new() { Enabled = true, AllowedShopIds = [_shop] }).IsEnabled(_shop));
        Assert.False(Create(new() { Enabled = true, AllowedShopIds = [Guid.NewGuid()] }).IsEnabled(_shop));
    }

    [Fact]
    public async Task Authorization_UsesLiveActiveRolesAndRequiresManagerOverrideReason()
    {
        var policy = Create(new());
        Assert.True((await policy.AuthorizeAsync(_shop, _actor, _actor, null)).IsError);
        _shops.GetActiveRoleAsync(_actor, _shop, Arg.Any<CancellationToken>()).Returns(ShopRole.Staff);
        Assert.False((await policy.AuthorizeAsync(_shop, _actor, _actor, null)).IsError);
        Assert.True((await policy.AuthorizeAsync(_shop, _actor, Guid.NewGuid(), "help")).IsError);
        _shops.GetActiveRoleAsync(_actor, _shop, Arg.Any<CancellationToken>()).Returns(ShopRole.Manager);
        var cashier = Guid.NewGuid();
        _shops.GetActiveRoleAsync(cashier, _shop, Arg.Any<CancellationToken>()).Returns(ShopRole.Staff);
        Assert.True((await policy.AuthorizeAsync(_shop, _actor, cashier, null)).IsError);
        Assert.False((await policy.AuthorizeAsync(_shop, _actor, cashier, "cover shift")).IsError);
    }

    [Fact]
    public void Trading_RefusesOverdueAndDifferentSourceDate()
    {
        var today = new DateOnly(2026, 11, 8);
        var session = TillSession.Open(_shop, Guid.NewGuid(), _actor, "desk", "cashier", _actor,
            DateTimeOffset.UtcNow, today, "Asia/Kolkata", 0m, null).Value;
        var policy = Create(new());
        Assert.False(policy.ValidateTradingDate(session, today, today).IsError);
        Assert.True(policy.ValidateTradingDate(session, today.AddDays(-1), today).IsError);
        Assert.True(policy.ValidateTradingDate(session, today, today.AddDays(1)).IsError);
    }

    [Fact]
    public void MixedTenderConfiguration_IsRejected()
    {
        Assert.Throws<OptionsValidationException>(() => Create(new() { MixedTenderEnabled = true }));
    }

    private TillSessionPolicy Create(TillSessionOptions options) => new(Microsoft.Extensions.Options.Options.Create(options), _shops);
}
