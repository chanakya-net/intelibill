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
        TimeProvider clock, CancellationToken token, bool recentMfa = false)
    {
        var (actor, session) = await store.LockAuthorityAsync(caller, token);
        var now = clock.GetUtcNow();
        if (session is null || now >= caller.AccessExpiresAt || !session.IsLiveAt(now)
            || actor is not { DeactivatedAt: null, MfaEnrolledAt: not null })
            return AdminRefreshErrors.SessionEnded;
        if (!actor.Capabilities.HasFlag(AdminCapability.LicensingManager)) return CatalogueErrors.AccessDenied;
        if (recentMfa && !new ProviderActor(caller.AdminId, session.MfaVerifiedAt).HasRecentMfa(now))
            return CataloguePublicationErrors.Mfa;
        await store.EnterCatalogueAsync(actor.Id, token);
        return null;
    }

    internal static bool ValidSource(int expected, Guid? source, int? sourceRevision) =>
        source is null && sourceRevision is null
        || expected == 0 && source is { } id && id != Guid.Empty && sourceRevision is > 0;

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
