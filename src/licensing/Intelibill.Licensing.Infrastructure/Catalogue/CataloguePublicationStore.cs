using Intelibill.Licensing.Application.Catalogue;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Catalogue;

internal sealed class CataloguePublicationStore(LicensingDbContext context) : ICataloguePublicationStore
{
    public Task LockOperationAsync(Guid adminId, Guid key, CancellationToken token) =>
        CatalogueDraftStore.LockFenceAsync(context, $"catalogue:operation:{adminId:D}:{key:D}", token);

    public Task<CataloguePreparation?> ReadPreparationAsync(Guid adminId, Guid key, CancellationToken token) =>
        context.Set<CataloguePreparation>().AsNoTracking().SingleOrDefaultAsync(
            item => item.AdminId == adminId && item.OperationKey == key, token);

    public Task<CataloguePublication?> ReadPublicationAsync(Guid versionId, CancellationToken token) =>
        context.Set<CataloguePublication>().AsNoTracking().SingleOrDefaultAsync(item => item.VersionId == versionId, token);

    public async Task<TrialPolicyVersion?> LockPublishedTrialAsync(CancellationToken token)
    {
        await CatalogueDraftStore.LockFenceAsync(context, "catalogue:trial", token);
        var versions = await context.Set<TrialPolicyVersion>().FromSqlRaw("""
            SELECT * FROM public.trial_policy_versions WHERE status = 2 FOR UPDATE
            """).ToListAsync(token);
        return versions.SingleOrDefault();
    }

    public async Task<PlanVersion?> LockPublishedPlanAsync(Guid planId, CancellationToken token)
    {
        var versions = await context.Set<PlanVersion>().FromSql($"""
            SELECT * FROM public.plan_versions WHERE plan_id = {planId} AND status = 2 FOR UPDATE
            """).ToListAsync(token);
        return versions.SingleOrDefault();
    }

    public Task FlushAsync(CancellationToken token) => context.SaveChangesAsync(token);
}
