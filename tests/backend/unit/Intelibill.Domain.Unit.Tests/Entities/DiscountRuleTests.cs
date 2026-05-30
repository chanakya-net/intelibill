using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Events;

namespace Intelibill.Domain.Unit.Tests.Entities;

public class DiscountRuleTests
{
    private static readonly Guid ShopId = Guid.NewGuid();
    private static readonly Guid CreatedBy = Guid.NewGuid();

    private static ErrorOr.ErrorOr<DiscountRule> MakeRule(
        DiscountRuleType ruleType = DiscountRuleType.SalePercentage,
        string name = "Summer Sale",
        decimal percentage = 10m,
        decimal? thresholdAmount = null,
        DateTimeOffset? startsAt = null,
        DateTimeOffset? endsAt = null,
        Guid? inventoryBatchId = null)
    {
        return DiscountRule.Create(
            ShopId,
            ruleType,
            name,
            description: null,
            inventoryBatchId: inventoryBatchId,
            percentage: percentage,
            thresholdAmount: thresholdAmount,
            startsAt: startsAt,
            endsAt: endsAt,
            belowCostConfirmed: false,
            belowCostConfirmationReason: null,
            createdBy: CreatedBy);
    }

    [Fact]
    public void Create_ValidInputs_CreatesActiveRule()
    {
        var result = MakeRule();

        Assert.False(result.IsError);
        Assert.Equal(ShopId, result.Value.ShopId);
        Assert.Equal("Summer Sale", result.Value.Name);
        Assert.Equal(10m, result.Value.Percentage);
        Assert.True(result.Value.IsActive);
        Assert.Null(result.Value.DisabledAt);
        Assert.Null(result.Value.DisabledReason);
        Assert.Equal(CreatedBy, result.Value.CreatedBy);
    }

    [Fact]
    public void Create_ValidInputs_AddsDiscountRuleChangedDomainEvent()
    {
        var result = MakeRule();

        Assert.False(result.IsError);
        var rule = result.Value;
        Assert.Single(rule.DomainEvents);

        var @event = Assert.IsType<DiscountRuleChangedDomainEvent>(rule.DomainEvents[0]);
        Assert.Equal(ShopId, @event.ShopId);
        Assert.Equal(rule.Id, Assert.Single(@event.DiscountRuleIds));
    }

    [Fact]
    public void Create_BlankName_ReturnsNameRequiredError()
    {
        var result = MakeRule(name: "   ");

        Assert.True(result.IsError);
        Assert.Equal("DiscountRule.NameRequired", result.FirstError.Code);
    }

    [Fact]
    public void Create_ZeroPercentage_ReturnsPercentageOutOfRangeError()
    {
        var result = MakeRule(percentage: 0m);

        Assert.True(result.IsError);
        Assert.Equal("DiscountRule.PercentageOutOfRange", result.FirstError.Code);
    }

    [Fact]
    public void Create_PercentageAbove100_ReturnsPercentageOutOfRangeError()
    {
        var result = MakeRule(percentage: 100.01m);

        Assert.True(result.IsError);
        Assert.Equal("DiscountRule.PercentageOutOfRange", result.FirstError.Code);
    }

    [Fact]
    public void Create_ExactlyHundredPercent_Succeeds()
    {
        var result = MakeRule(percentage: 100m);

        Assert.False(result.IsError);
        Assert.Equal(100m, result.Value.Percentage);
    }

    [Fact]
    public void Create_SaleThresholdPercentage_WithoutThreshold_ReturnsThresholdRequiredError()
    {
        var result = MakeRule(ruleType: DiscountRuleType.SaleThresholdPercentage, thresholdAmount: null);

        Assert.True(result.IsError);
        Assert.Equal("DiscountRule.ThresholdAmountRequired", result.FirstError.Code);
    }

    [Fact]
    public void Create_SaleThresholdPercentage_WithThreshold_Succeeds()
    {
        var result = MakeRule(ruleType: DiscountRuleType.SaleThresholdPercentage, thresholdAmount: 500m);

        Assert.False(result.IsError);
        Assert.Equal(500m, result.Value.ThresholdAmount);
    }

    [Fact]
    public void Create_EndsAtBeforeStartsAt_ReturnsEndsAtError()
    {
        var starts = DateTimeOffset.UtcNow;
        var ends = starts.AddDays(-1);

        var result = MakeRule(startsAt: starts, endsAt: ends);

        Assert.True(result.IsError);
        Assert.Equal("DiscountRule.EndsAtMustBeAfterStartsAt", result.FirstError.Code);
    }

    [Fact]
    public void Create_EndsAtEqualToStartsAt_ReturnsEndsAtError()
    {
        var point = DateTimeOffset.UtcNow;

        var result = MakeRule(startsAt: point, endsAt: point);

        Assert.True(result.IsError);
        Assert.Equal("DiscountRule.EndsAtMustBeAfterStartsAt", result.FirstError.Code);
    }

    [Fact]
    public void Create_OpenEndedWindow_Succeeds()
    {
        var result = MakeRule(startsAt: DateTimeOffset.UtcNow, endsAt: null);

        Assert.False(result.IsError);
        Assert.NotNull(result.Value.StartsAt);
        Assert.Null(result.Value.EndsAt);
    }

    [Fact]
    public void Create_NegativeThresholdAmount_ReturnsThresholdPositiveError()
    {
        var result = MakeRule(
            ruleType: DiscountRuleType.SaleThresholdPercentage,
            thresholdAmount: -100m);

        Assert.True(result.IsError);
        Assert.Equal("DiscountRule.ThresholdAmountMustBePositive", result.FirstError.Code);
    }

    [Fact]
    public void Disable_ActiveRule_DisablesWithAudit()
    {
        var rule = MakeRule().Value;
        var disabledAt = DateTimeOffset.UtcNow;
        var disabledBy = Guid.NewGuid();

        var result = rule.Disable("End of promo", disabledAt, disabledBy);

        Assert.False(result.IsError);
        Assert.False(rule.IsActive);
        Assert.Equal(disabledAt, rule.DisabledAt);
        Assert.Equal("End of promo", rule.DisabledReason);
        Assert.Equal(disabledBy, rule.UpdatedBy);
    }

    [Fact]
    public void Disable_ActiveRule_AddsDiscountRuleChangedDomainEvent()
    {
        var rule = MakeRule().Value;
        rule.ClearDomainEvents();

        var result = rule.Disable("End of promo", DateTimeOffset.UtcNow, Guid.NewGuid());

        Assert.False(result.IsError);
        Assert.Single(rule.DomainEvents);

        var @event = Assert.IsType<DiscountRuleChangedDomainEvent>(rule.DomainEvents[0]);
        Assert.Equal(ShopId, @event.ShopId);
        Assert.Equal(rule.Id, Assert.Single(@event.DiscountRuleIds));
    }

    [Fact]
    public void Disable_AlreadyDisabledRule_ReturnsAlreadyDisabledError()
    {
        var rule = MakeRule().Value;
        rule.Disable(null, DateTimeOffset.UtcNow, Guid.NewGuid());

        var result = rule.Disable(null, DateTimeOffset.UtcNow, Guid.NewGuid());

        Assert.True(result.IsError);
        Assert.Equal("DiscountRule.AlreadyDisabled", result.FirstError.Code);
    }

    [Fact]
    public void ReplaceWith_SetsVersionLinkAndDisables()
    {
        var rule = MakeRule().Value;
        var newRuleId = Guid.NewGuid();
        var disabledAt = DateTimeOffset.UtcNow;
        var updatedBy = Guid.NewGuid();

        rule.ReplaceWith(newRuleId, disabledAt, updatedBy);

        Assert.Equal(newRuleId, rule.ReplacedByRuleId);
        Assert.False(rule.IsActive);
        Assert.Equal(disabledAt, rule.DisabledAt);
        Assert.NotNull(rule.DisabledReason);
        Assert.Equal(updatedBy, rule.UpdatedBy);
    }

    [Fact]
    public void ReplaceWith_AddsDiscountRuleChangedDomainEvent()
    {
        var rule = MakeRule().Value;
        rule.ClearDomainEvents();
        var newRuleId = Guid.NewGuid();

        rule.ReplaceWith(newRuleId, DateTimeOffset.UtcNow, Guid.NewGuid());

        Assert.Single(rule.DomainEvents);
        var @event = Assert.IsType<DiscountRuleChangedDomainEvent>(rule.DomainEvents[0]);
        Assert.Equal(ShopId, @event.ShopId);
        Assert.Equal(new[] { rule.Id, newRuleId }, @event.DiscountRuleIds);
    }

    [Fact]
    public void MarkAsReplacement_SetsReplacesRuleId()
    {
        var rule = MakeRule().Value;
        var oldRuleId = Guid.NewGuid();

        rule.MarkAsReplacement(oldRuleId);

        Assert.Equal(oldRuleId, rule.ReplacesRuleId);
    }

    [Fact]
    public void Create_NameTrimmed_StoresWithoutWhitespace()
    {
        var result = MakeRule(name: "  Flash Sale  ");

        Assert.False(result.IsError);
        Assert.Equal("Flash Sale", result.Value.Name);
    }
}
