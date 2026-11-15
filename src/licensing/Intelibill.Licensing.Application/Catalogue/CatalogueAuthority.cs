using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Licensing.Domain.Policies;

namespace Intelibill.Licensing.Application.Catalogue;

/// <summary>
/// Live authority for catalogue drafts: an active, MFA-enrolled administrator holding the persisted Licensing
/// Manager grant, on a live session. Platform Owner alone does not qualify.
/// </summary>
internal static class CatalogueAuthority
{
    internal static async Task<Error?> EnterAsync(ICatalogueDraftStore store, AdminSessionCaller caller,
        DateTimeOffset now, CancellationToken token)
    {
        var (actor, session) = await store.LockAuthorityAsync(caller, token);
        if (session is null || now >= caller.AccessExpiresAt || !session.IsLiveAt(now)
            || actor is not { DeactivatedAt: null, MfaEnrolledAt: not null })
            return AdminRefreshErrors.SessionEnded;
        if (!actor.Capabilities.HasFlag(AdminCapability.LicensingManager)) return CatalogueErrors.AccessDenied;
        await store.EnterCatalogueAsync(actor.Id, token);
        return null;
    }

    internal static Error? ToError(CatalogueRuleViolation violation) => violation switch
    {
        CatalogueRuleViolation.None => null,
        CatalogueRuleViolation.NameInvalid => CatalogueErrors.NameInvalid,
        CatalogueRuleViolation.AmountInvalid => CatalogueErrors.AmountInvalid,
        CatalogueRuleViolation.CapacityInvalid => CatalogueErrors.CapacityInvalid,
        CatalogueRuleViolation.ExtraShopTermsInvalid => CatalogueErrors.ExtraShopTermsInvalid,
        CatalogueRuleViolation.PolicyVersionInvalid => CatalogueErrors.PolicyVersionInvalid,
        CatalogueRuleViolation.CustomReasonRequired => CatalogueErrors.CustomReasonRequired,
        _ => CatalogueErrors.DraftInvalid,
    };
}
