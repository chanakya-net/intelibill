using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Licensing.Domain.Policies;

namespace Intelibill.Licensing.Application.Catalogue;

/// <summary>Reads and saves the single open trial policy draft (fixed 14 days, explicit caps).</summary>
public sealed class SaveTrialPolicyDraftHandler(ICatalogueDraftStore store, TimeProvider clock)
{
    public Task<ErrorOr<TrialDraftEnvelope>> GetAsync(AdminSessionCaller caller, CancellationToken token) =>
        store.InTransactionAsync<TrialDraftEnvelope>(async ct =>
        {
            if (await CatalogueAuthority.EnterAsync(store, caller, clock.GetUtcNow(), ct) is { } denied) return denied;
            var draft = await store.LockTrialDraftAsync(ct);
            return new TrialDraftEnvelope(draft is null ? null : CatalogueDraftViews.ToView(draft));
        }, token);

    public Task<ErrorOr<TrialDraftSaved>> SaveAsync(AdminSessionCaller caller, TrialPolicyDraftRequest? request,
        CancellationToken token) =>
        store.InTransactionAsync<TrialDraftSaved>(async ct =>
        {
            if (await CatalogueAuthority.EnterAsync(store, caller, clock.GetUtcNow(), ct) is { } denied) return denied;
            if (request is not { ExpectedVersion: >= 0 and var expected }) return CatalogueErrors.DraftInvalid;
            var facts = new TrialPolicyFacts(request.ActiveMemberCap, request.MaxConcurrentTrialShops,
                request.TrialTermsVersion);
            if (CatalogueAuthority.ToError(CatalogueDraftRules.Check(facts)) is { } invalid) return invalid;

            var draft = await store.LockTrialDraftAsync(ct);
            var now = clock.GetUtcNow();
            if (expected == 0)
            {
                if (draft is not null) return CatalogueErrors.VersionConflict;
                draft = TrialPolicyVersion.Draft(Guid.CreateVersion7(now), await store.NextTrialVersionNumberAsync(ct),
                    facts, caller.AdminId, now);
                store.Add(draft);
            }
            else
            {
                if (draft is null) return CatalogueErrors.DraftNotFound;
                if (draft.Revision != expected) return CatalogueErrors.VersionConflict;
                draft.Revise(facts, caller.AdminId, now);
            }
            store.Add(CatalogueDraftChange.For(draft));
            return new TrialDraftSaved(new TrialDraftEnvelope(CatalogueDraftViews.ToView(draft)), expected == 0);
        }, token);
}
