using ErrorOr;
using LicenceErrors = Intelibill.Application.Common.Errors.Errors.Licence;

namespace Intelibill.Application.Common.Licensing;

public static class LicenceGate
{
    public static async Task<Error?> ReadTradingDenialAsync(this IShopLicenceFence fence, Guid shopId, CancellationToken ct)
    {
        var result = await fence.EnterAsync(shopId, LicenceAction.NewTrading, ct);
        if (result.IsError)
            return result.FirstError;

        return result.Value.Decision.EnforcedDenial();
    }

    public static Error? EnforcedDenial(this LicenceDecision decision) =>
        decision is { Enforced: true, Outcome: LicenceOutcome.Blocked }
            ? decision.Denial ?? LicenceErrors.VerificationUnavailable
            : null;
}
