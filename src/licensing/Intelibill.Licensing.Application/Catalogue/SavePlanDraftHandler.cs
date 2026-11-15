using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Licensing.Domain.Policies;

namespace Intelibill.Licensing.Application.Catalogue;

/// <summary>Lists and saves plan drafts. Every save is a full replacement guarded by the expected revision.</summary>
public sealed class SavePlanDraftHandler(ICatalogueDraftStore store, TimeProvider clock, ICataloguePublicationApproval approval)
{
    private const int PageSize = 50;

    public Task<ErrorOr<PlanPage>> ListAsync(AdminSessionCaller caller, Guid? after, CancellationToken token) =>
        store.InTransactionAsync<PlanPage>(async ct =>
        {
            if (await CatalogueAuthority.EnterAsync(store, caller, clock, ct) is { } denied) return denied;
            var rows = await store.ListPlanDraftsAsync(after, PageSize + 1, ct);
            var items = rows.Take(PageSize).Select(row => CatalogueDraftViews.ToItem(row.Plan, row.Draft)).ToArray();
            return new PlanPage(items, rows.Count > PageSize ? items[^1].PlanId : null);
        }, token);

    public Task<ErrorOr<PlanDraftSaved>> SaveAsync(AdminSessionCaller caller, Guid planId, PlanDraftRequest? request,
        CancellationToken token) =>
        store.InTransactionAsync<PlanDraftSaved>(async ct =>
        {
            if (await CatalogueAuthority.EnterAsync(store, caller, clock, ct) is { } denied) return denied;
            var parsed = Parse(planId, request);
            if (parsed.IsError) return parsed.Errors;
            var (kind, facts, expected) = parsed.Value;
            var (plan, draft) = await store.LockPlanAsync(planId, ct);
            if (await CatalogueAuthority.EnterAsync(store, caller, clock, ct) is { } expired) return expired;
            var now = clock.GetUtcNow();
            if (expected == 0)
            {
                if (plan is null && request!.SourceVersionId is null)
                    return Create(planId, kind, facts, caller.AdminId, now);
                if (plan is null || draft is not null || request!.SourceVersionId is not { } sourceId)
                    return CatalogueErrors.VersionConflict;
                return await CreateSuccessorAsync(plan, kind, facts, sourceId, request.SourceExpectedVersion, caller, ct);
            }
            if (plan is null || draft is null) return CatalogueErrors.DraftNotFound;
            if (plan.Kind != kind) return CatalogueErrors.KindFixed;
            if (draft.Revision != expected) return CatalogueErrors.VersionConflict;
            draft.Revise(facts, caller.AdminId, now);
            store.Add(CatalogueDraftChange.For(draft));
            return new PlanDraftSaved(CatalogueDraftViews.ToItem(plan, draft), Created: false);
        }, token);

    private async Task<ErrorOr<PlanDraftSaved>> CreateSuccessorAsync(Plan plan, PlanKind kind, PlanDraftFacts facts,
        Guid sourceId, int? sourceExpectedVersion, AdminSessionCaller caller, CancellationToken token)
    {
        if (plan.Kind != kind) return CatalogueErrors.KindFixed;
        var source = await store.ReadPlanSourceAsync(plan.Id, sourceId, token);
        if (source is null || source.Revision != sourceExpectedVersion) return CatalogueErrors.VersionConflict;
        var number = await store.NextPlanVersionNumberAsync(plan.Id, token);
        var now = clock.GetUtcNow();
        var draft = PlanVersion.Draft(Guid.CreateVersion7(now), plan.Id, number, facts, caller.AdminId, now,
            source.Source, source.Id, source.Revision);
        store.Add(draft);
        store.Add(CatalogueDraftChange.For(draft));
        return new PlanDraftSaved(CatalogueDraftViews.ToItem(plan, draft), Created: true);
    }

    private PlanDraftSaved Create(Guid planId, PlanKind kind, PlanDraftFacts facts, Guid adminId, DateTimeOffset now)
    {
        var plan = Plan.Create(planId, kind, adminId, now);
        var draft = PlanVersion.Draft(Guid.CreateVersion7(now), planId, 1, facts, adminId, now, approval.DraftSource);
        store.Add(plan);
        store.Add(draft);
        store.Add(CatalogueDraftChange.For(draft));
        return new PlanDraftSaved(CatalogueDraftViews.ToItem(plan, draft), Created: true);
    }

    private static ErrorOr<(PlanKind Kind, PlanDraftFacts Facts, int Expected)> Parse(Guid planId,
        PlanDraftRequest? request)
    {
        if (planId == Guid.Empty || request is not { ExpectedVersion: >= 0 and var expected }
            || CatalogueDraftViews.ParseKind(request.Kind) is not { } kind
            || !CatalogueAuthority.ValidSource(expected, request.SourceVersionId, request.SourceExpectedVersion))
            return CatalogueErrors.DraftInvalid;
        var facts = new PlanDraftFacts(request.Name ?? string.Empty, request.MonthlyPricePaise,
            request.AnnualPricePaise, request.IncludedShops, request.MembersPerShop, request.ExtraShopsOffered,
            request.ExtraShopMonthlyPricePaise, request.ExtraShopAnnualPricePaise, request.CustomerTermsVersion,
            request.RefundPolicyVersion, request.Reason);
        if (CatalogueAuthority.ToError(CatalogueDraftRules.Check(kind, facts)) is { } invalid) return invalid;
        return (kind, CatalogueDraftRules.Normalize(kind, facts), expected);
    }
}
