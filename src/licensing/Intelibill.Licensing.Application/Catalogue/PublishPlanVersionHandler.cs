using System.Text.Json;
using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Licensing.Domain.Policies;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Catalogue;

public sealed class PublishPlanVersionHandler(ICatalogueDraftStore drafts, ICataloguePublicationStore store,
    ICataloguePublicationApproval approval, CataloguePublicationOperation operation, TimeProvider clock)
{
    public Task<ErrorOr<CataloguePublicationView>> PrepareAsync(AdminSessionCaller caller, Guid planId, Guid versionId,
        PrepareCataloguePublication? request, CancellationToken token) => operation.PrepareAsync(caller, planId,
        versionId, request, async (preparation, ct) =>
        {
            var trial = await store.LockPublishedTrialAsync(ct);
            var (plan, draft) = await drafts.LockPlanAsync(planId, ct);
            var result = Snapshot(preparation, plan, draft, trial);
            return result.IsError ? result.Errors : result.Value.Json;
        }, token);

    public Task<ErrorOr<CataloguePublicationView>> ConfirmAsync(AdminSessionCaller caller, Guid planId, Guid versionId,
        ConfirmCataloguePublication? request, CancellationToken token) => operation.ConfirmAsync(caller, planId,
        versionId, request, (prepared, ct) => PublishAsync(caller, prepared, ct), token);

    private async Task<ErrorOr<DateTimeOffset>> PublishAsync(AdminSessionCaller caller, CataloguePreparation prepared,
        CancellationToken token)
    {
        var trial = await store.LockPublishedTrialAsync(token);
        var planId = prepared.PlanId!.Value;
        var (plan, draft) = await drafts.LockPlanAsync(planId, token);
        var previous = await store.LockPublishedPlanAsync(planId, token);
        if (await operation.AuthorizeAsync(caller, token) is { } expired) return expired;
        var snapshot = Snapshot(prepared, plan, draft, trial);
        if (snapshot.IsError || snapshot.Value.Json != prepared.Snapshot) return CataloguePublicationErrors.Conflict;
        var now = clock.GetUtcNow();
        previous?.Retire(caller.AdminId, now);
        drafts.Add(new CataloguePublication(prepared.VersionId, caller.AdminId, prepared.OperationKey, now));
        await store.FlushAsync(token);
        draft!.Publish(caller.AdminId, now);
        if (plan!.Kind != PlanKind.Custom)
        {
            await store.FlushAsync(token);
            var terms = snapshot.Value.Terms;
            drafts.Add(new PublishedCatalogueOffer(terms.PlanId, terms.VersionId, terms.VersionNumber,
                terms.DevelopmentFixture, JsonSerializer.Serialize(terms, CataloguePublicationOperation.Json)));
        }
        return now;
    }

    private ErrorOr<(PublishedPlanTermsV1 Terms, string Json)> Snapshot(CataloguePreparation prepared, Plan? plan,
        PlanVersion? draft, TrialPolicyVersion? trial)
    {
        if (plan is null || draft is null || draft.Id != prepared.VersionId || draft.Revision != prepared.ExpectedVersion)
            return CataloguePublicationErrors.Conflict;
        if (!approval.Allows(draft.Source) || approval.AccountingTreatmentReference is not { } accounting)
            return CataloguePublicationErrors.Configuration;
        if (CatalogueAuthority.ToError(CatalogueDraftRules.Check(plan.Kind, draft.Facts)) is { } invalid) return invalid;
        if (CatalogueDraftRules.Missing(plan.Kind, draft.Facts).Length != 0) return CataloguePublicationErrors.Incomplete;
        if (trial is null || trial.Id != prepared.TrialPolicyVersionId || trial.Source != draft.Source)
            return CataloguePublicationErrors.TrialRequired;

        var terms = ToTerms(plan, draft, trial);
        var json = JsonSerializer.Serialize(new PlanPublicationSnapshot(terms, draft.NegotiationReason, accounting,
            draft.SourceVersionId, draft.SourceExpectedVersion),
            CataloguePublicationOperation.Json);
        if (!approval.Approves(CataloguePublicationOperation.Hash(json), draft.Id, terms.TrialPolicy.TrialTermsVersion,
                terms.CustomerTermsVersion, terms.RefundPolicyVersion))
            return CataloguePublicationErrors.Configuration;
        return (terms, json);
    }

    private static PublishedPlanTermsV1 ToTerms(Plan plan, PlanVersion draft, TrialPolicyVersion trial) =>
        new(plan.Id, draft.Id, draft.VersionNumber, CatalogueDraftViews.KindName(plan.Kind), draft.Name,
            draft.MonthlyPricePaise, draft.AnnualPricePaise, draft.IncludedShops!.Value, draft.MembersPerShop!.Value,
            draft.ExtraShopsOffered!.Value, draft.ExtraShopMonthlyPricePaise, draft.ExtraShopAnnualPricePaise,
            draft.CustomerTermsVersion!, draft.RefundPolicyVersion!, PublishTrialPolicyVersionHandler.ToSnapshot(trial),
            draft.Source == CatalogueSource.DevelopmentFixture);

    private sealed record PlanPublicationSnapshot(PublishedPlanTermsV1 Plan, string? NegotiationReason,
        string AccountingTreatmentReference, Guid? SourceVersionId, int? SourceExpectedVersion);
}
