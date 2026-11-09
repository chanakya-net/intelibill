using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Expiry;

public sealed record ExpiryPolicySettings(ExpiryMode Mode, bool ExpiryRequired, bool SellThroughExpiryDate, int WarningDays, bool OverrideAllowed)
{
    public ExpiryPolicy ToPolicy(long version) => new(version, Mode, ExpiryRequired, SellThroughExpiryDate, WarningDays, OverrideAllowed);
}
public sealed record ExpiryPolicyUpdateRequest(long ExpectedCatalogVersion, ExpiryPolicySettings? Policy, string Reason);
public sealed record ExpiryPolicyStateDto(ExpiryPolicy DefaultPolicy, ExpiryPolicy? OverridePolicy, ExpiryPolicy EffectivePolicy, long CatalogVersion);
public sealed record ExpiryReadinessDto(DateOnly BusinessDate, long CatalogVersion, int Page, int PageSize, ExpiryReadinessPage Results);
