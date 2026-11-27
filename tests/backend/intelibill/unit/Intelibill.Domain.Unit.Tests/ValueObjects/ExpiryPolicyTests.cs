using Intelibill.Domain.ValueObjects;

namespace Intelibill.Domain.Unit.Tests.ValueObjects;

public class ExpiryPolicyTests
{
    private static readonly DateOnly Today = new(2026, 10, 21);
    private static ExpiryBatchFacts Facts(DateOnly? expiry = null) => new(Guid.NewGuid(), Guid.NewGuid(), expiry, null, 10m, 3m, false, true, 1);
    private static ExpiryPolicy Policy(ExpiryMode mode = ExpiryMode.Block, bool required = false, bool inclusive = true, int days = 7, bool approval = false) => new(2, mode, required, inclusive, days, approval);

    [Theory]
    [InlineData(ExpiryMode.Ignore, false, false, true, null)]
    [InlineData(ExpiryMode.Block, false, false, false, "Expiry.Expired")]
    [InlineData(ExpiryMode.Warn, false, false, false, "Expiry.OverrideDisabled")]
    [InlineData(ExpiryMode.Warn, false, true, true, "Expiry.Expired")]
    public void ExpiredLot_UsesModeAndApproval(ExpiryMode mode, bool required, bool approval, bool allowed, string? code)
    {
        var decision = ExpiryPolicyEvaluator.Evaluate(Facts(Today.AddDays(-1)), Policy(mode, required, approval: approval), 4, Today);
        Assert.Equal(allowed, decision.Allowed);
        Assert.Equal(code, decision.ReasonCode);
        Assert.Equal(allowed && mode == ExpiryMode.Warn, decision.RequiresApproval);
        Assert.Equal(allowed ? 7m : 0m, decision.AvailableQuantity);
        Assert.Equal(2, decision.PolicyVersion);
        Assert.Equal(4, decision.CatalogVersion);
    }

    [Theory]
    [InlineData(ExpiryMode.Ignore)]
    [InlineData(ExpiryMode.Warn)]
    [InlineData(ExpiryMode.Block)]
    public void RequiredMissingDate_HardBlocksEveryMode(ExpiryMode mode)
    {
        var decision = ExpiryPolicyEvaluator.Evaluate(Facts(), Policy(mode, required: true, approval: true), 2, Today);
        Assert.False(decision.Allowed);
        Assert.False(decision.RequiresApproval);
        Assert.Equal("Expiry.RequiredDateMissing", decision.ReasonCode);
        Assert.Equal(0m, decision.AvailableQuantity);
    }

    [Theory]
    [InlineData(true, 0, true)]
    [InlineData(true, 1, false)]
    [InlineData(false, 0, false)]
    public void ExpiryBoundary_UsesShopBusinessDate(bool inclusive, int elapsed, bool allowed)
    {
        var decision = ExpiryPolicyEvaluator.Evaluate(Facts(Today), Policy(inclusive: inclusive), 2, Today.AddDays(elapsed));
        Assert.Equal(allowed, decision.Allowed);
        if (!allowed) Assert.Equal("Expiry.Expired", decision.ReasonCode);
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(0, 1, false)]
    [InlineData(7, 7, true)]
    [InlineData(7, 8, false)]
    [InlineData(365, 365, true)]
    [InlineData(365, 366, false)]
    public void WarningWindow_IsInclusiveAndInformational(int days, int until, bool warning)
    {
        var decision = ExpiryPolicyEvaluator.Evaluate(Facts(Today.AddDays(until)), Policy(days: days), 2, Today);
        Assert.True(decision.Allowed);
        Assert.Equal(warning, decision.Warning);
        Assert.False(decision.RequiresApproval);
        Assert.Equal(warning ? "Expiry.ExpiringSoon" : null, decision.ReasonCode);
        Assert.Equal(7m, decision.AvailableQuantity);
    }

    [Fact]
    public void OptionalMissingAndIgnoreDates_PreserveCompatibility()
    {
        Assert.True(ExpiryPolicyEvaluator.Evaluate(Facts(), Policy(), 2, Today).Allowed);
        var ignored = ExpiryPolicyEvaluator.Evaluate(Facts(Today), Policy(ExpiryMode.Ignore), 2, Today);
        Assert.True(ignored.Allowed);
        Assert.False(ignored.Warning);
    }

    [Theory]
    [InlineData(true, true, 10, 3)]
    [InlineData(false, false, 10, 3)]
    [InlineData(false, true, 3, 3)]
    [InlineData(false, true, 0, 0)]
    public void StockAndItemRefusals_WinOverWarningApproval(bool voided, bool active, int physical, int held)
    {
        var batch = Facts(Today.AddDays(-1)) with { IsVoided = voided, ItemActive = active, PhysicalQuantity = physical, QuarantinedQuantity = held };
        var result = ExpiryPolicyEvaluator.Evaluate(batch, Policy(ExpiryMode.Warn, approval: true), 2, Today);
        Assert.False(result.Allowed);
        Assert.False(result.RequiresApproval);
        Assert.Equal(0m, result.AvailableQuantity);
    }

    [Theory]
    [InlineData(-1, 7)]
    [InlineData(3, 7)]
    [InlineData(0, -1)]
    [InlineData(1, 366)]
    public void InvalidPolicy_IsRefused(int mode, int days) =>
        Assert.False((Policy((ExpiryMode)mode, days: days)).IsValid());
}
