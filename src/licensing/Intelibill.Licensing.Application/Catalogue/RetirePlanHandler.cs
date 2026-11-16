using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Domain.Entities;

namespace Intelibill.Licensing.Application.Catalogue;

public sealed class RetirePlanHandler(ICatalogueDraftStore drafts, ICataloguePublicationStore store, TimeProvider clock)
{
    public Task<ErrorOr<PlanRetirementView>> HandleAsync(AdminSessionCaller caller, Guid planId, Guid versionId,
        RetirePlanVersion? request, CancellationToken token) => drafts.InTransactionAsync<PlanRetirementView>(async ct =>
        {
            if (await AuthorizeAsync(caller, ct) is { } denied) return denied;
            var reason = request?.Reason?.Trim();
            if (request is not { Confirmed: true, ExpectedVersion: > 0 } || request.OperationKey == Guid.Empty
                || planId == Guid.Empty || versionId == Guid.Empty || reason is not { Length: > 0 and <= 500 }
                || reason.Any(char.IsControl))
                return CatalogueRetirementErrors.Invalid;

            await store.LockOperationAsync(caller.AdminId, request.OperationKey, ct);
            if (await AuthorizeAsync(caller, ct) is { } expired) return expired;
            var previous = await store.ReadRetirementAsync(caller.AdminId, request.OperationKey, ct);
            if (previous is not null)
                return previous.PlanId == planId && previous.VersionId == versionId
                    && previous.ExpectedVersion == request.ExpectedVersion && previous.Reason == reason
                    ? View(previous) : CatalogueRetirementErrors.Conflict;

            var (plan, _) = await drafts.LockPlanAsync(planId, ct);
            var published = await store.LockPublishedPlanAsync(planId, ct);
            if (await AuthorizeAsync(caller, ct) is { } stale) return stale;
            if (plan is null || published?.Id != versionId || published.Revision != request.ExpectedVersion)
                return CatalogueRetirementErrors.Conflict;

            var now = clock.GetUtcNow();
            published.Retire(caller.AdminId, now);
            await store.FlushAsync(ct);
            var evidence = new CatalogueRetirement(versionId, planId, caller.AdminId, request.OperationKey,
                request.ExpectedVersion, reason, now);
            drafts.Add(evidence);
            if (plan.Kind != PlanKind.Custom)
            {
                await store.FlushAsync(ct);
                drafts.Add(new WithdrawnCatalogueOffer(versionId));
            }
            return View(evidence);
        }, token);

    public Task<ErrorOr<PlanRetirementView>> RecoverAsync(AdminSessionCaller caller, Guid key, CancellationToken token) =>
        drafts.InTransactionAsync<PlanRetirementView>(async ct =>
        {
            if (await AuthorizeAsync(caller, ct) is { } denied) return denied;
            var previous = await store.ReadRetirementAsync(caller.AdminId, key, ct);
            return previous is null ? CatalogueRetirementErrors.Missing : View(previous);
        }, token);

    private Task<Error?> AuthorizeAsync(AdminSessionCaller caller, CancellationToken token) =>
        CatalogueAuthority.EnterAsync(drafts, caller, clock, token, recentMfa: true);

    private static PlanRetirementView View(CatalogueRetirement row) =>
        new(row.OperationKey, row.PlanId, row.VersionId, row.ExpectedVersion, row.Reason, row.RetiredAt);
}
