using System.Text.Json;
using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Licensing.Domain.Policies;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Catalogue;

public sealed class PublishTrialPolicyVersionHandler(ICatalogueDraftStore drafts, ICataloguePublicationStore store,
    ICataloguePublicationApproval approval, CataloguePublicationOperation operation, TimeProvider clock)
{
    public Task<ErrorOr<CataloguePublicationView>> PrepareAsync(AdminSessionCaller caller, Guid id,
        PrepareCataloguePublication? request, CancellationToken token) => operation.PrepareAsync(caller, null, id, request,
        async (preparation, ct) =>
        {
            var draft = await drafts.LockTrialDraftAsync(ct);
            return Snapshot(draft, preparation);
        }, token);

    public Task<ErrorOr<CataloguePublicationView>> ConfirmAsync(AdminSessionCaller caller, Guid id,
        ConfirmCataloguePublication? request, CancellationToken token) => operation.ConfirmAsync(caller, null, id, request,
        (prepared, ct) => PublishAsync(caller, prepared, ct), token);

    private async Task<ErrorOr<DateTimeOffset>> PublishAsync(AdminSessionCaller caller, CataloguePreparation prepared,
        CancellationToken token)
    {
        var draft = await drafts.LockTrialDraftAsync(token);
        var previous = await store.LockPublishedTrialAsync(token);
        if (await operation.AuthorizeAsync(caller, token) is { } expired) return expired;
        var snapshot = Snapshot(draft, prepared);
        if (snapshot.IsError || snapshot.Value != prepared.Snapshot) return CataloguePublicationErrors.Conflict;
        var now = clock.GetUtcNow();
        previous?.Retire(caller.AdminId, now);
        drafts.Add(new CataloguePublication(prepared.VersionId, caller.AdminId, prepared.OperationKey, now));
        await store.FlushAsync(token);
        draft!.Publish(caller.AdminId, now);
        return now;
    }

    private ErrorOr<string> Snapshot(TrialPolicyVersion? draft, CataloguePreparation prepared)
    {
        if (draft is null || draft.Id != prepared.VersionId || draft.Revision != prepared.ExpectedVersion)
            return CataloguePublicationErrors.Conflict;
        if (!approval.Allows(draft.Source)) return CataloguePublicationErrors.Configuration;
        if (CatalogueAuthority.ToError(CatalogueDraftRules.Check(draft.Facts)) is { } invalid) return invalid;
        if (CatalogueDraftRules.Missing(draft.Facts).Length != 0) return CataloguePublicationErrors.Incomplete;
        var facts = ToSnapshot(draft);
        var json = JsonSerializer.Serialize(facts, CataloguePublicationOperation.Json);
        return approval.Approves(CataloguePublicationOperation.Hash(json), draft.Id, facts.TrialTermsVersion)
            ? json : CataloguePublicationErrors.Configuration;
    }

    internal static PublishedTrialPolicyV1 ToSnapshot(TrialPolicyVersion draft) =>
        new(draft.Id, draft.VersionNumber, draft.DurationDays, draft.ActiveMemberCap!.Value,
            draft.MaxConcurrentTrialShops!.Value, draft.TrialTermsVersion!, draft.Source == CatalogueSource.DevelopmentFixture);
}
