using System.Data;
using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.Catalogue;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Licensing.Infrastructure.AdminIdentities;
using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Intelibill.Licensing.Infrastructure.Catalogue;

/// <summary>
/// Catalogue tables are forced-RLS: rows are visible and writable only inside a transaction whose
/// transaction-local <c>app.catalogue_admin_id</c> names a live Licensing Manager.
/// </summary>
internal sealed class CatalogueDraftStore(LicensingDbContext context) : ICatalogueDraftStore
{
    public async Task<ErrorOr<T>> InTransactionAsync<T>(Func<CancellationToken, Task<ErrorOr<T>>> operation,
        CancellationToken token)
    {
        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
            await context.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '3s'", token);
            var result = await operation(token);
            if (result.IsError) return result;
            await context.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            return result;
        }
        catch (Exception exception) when (SqlState(exception) == PostgresErrorCodes.LockNotAvailable)
        {
            return CatalogueErrors.Busy;
        }
        catch (Exception exception) when (SqlState(exception) == PostgresErrorCodes.UniqueViolation)
        {
            // A concurrent save created the same plan or the open trial draft first.
            return CatalogueErrors.VersionConflict;
        }
    }

    public async Task<(AdminIdentity? Actor, AdminSession? Session)> LockAuthorityAsync(AdminSessionCaller caller,
        CancellationToken token)
    {
        var actors = await context.Set<AdminIdentity>().FromSql($"""
            SELECT *, xmin FROM public.admin_identities WHERE id = {caller.AdminId} FOR SHARE
            """).AsNoTracking().ToListAsync(token);
        var session = await AdminAuthorityLocks.LockSessionAsync(context, caller, token);
        return (actors.SingleOrDefault(), session);
    }

    public Task EnterCatalogueAsync(Guid adminId, CancellationToken token) => context.Database.ExecuteSqlAsync(
        $"SELECT set_config('app.catalogue_admin_id', {adminId.ToString("D")}, true)", token);

    public async Task<IReadOnlyList<(Plan Plan, PlanVersion Draft)>> ListPlanDraftsAsync(Guid? after, int take,
        CancellationToken token)
    {
        var plans = context.Set<Plan>().AsNoTracking();
        if (after is { } id) plans = plans.Where(plan => plan.Id.CompareTo(id) > 0);
        var rows = await plans
            .Join(context.Set<PlanVersion>().AsNoTracking().Where(version => version.Status == CatalogueVersionStatus.Draft),
                plan => plan.Id, version => version.PlanId, (plan, version) => new { plan, version })
            .OrderBy(row => row.plan.Id).Take(take).ToListAsync(token);
        return rows.Select(row => (row.plan, row.version)).ToArray();
    }

    public async Task<(Plan? Plan, PlanVersion? Draft)> LockPlanAsync(Guid planId, CancellationToken token)
    {
        await LockFenceAsync(context, $"catalogue:plan:{planId:D}", token);
        var plan = await context.Set<Plan>().AsNoTracking().SingleOrDefaultAsync(row => row.Id == planId, token);
        if (plan is null) return (null, null);
        var drafts = await context.Set<PlanVersion>().FromSql($"""
            SELECT * FROM public.plan_versions WHERE plan_id = {planId} AND status = 1 FOR UPDATE
            """).ToListAsync(token);
        return (plan, drafts.SingleOrDefault());
    }

    public Task<TrialPolicyVersion?> ReadTrialDraftAsync(CancellationToken token) =>
        context.Set<TrialPolicyVersion>().AsNoTracking()
            .SingleOrDefaultAsync(version => version.Status == CatalogueVersionStatus.Draft, token);

    public async Task<TrialPolicyVersion?> LockTrialDraftAsync(CancellationToken token)
    {
        await LockFenceAsync(context, "catalogue:trial", token);
        var drafts = await context.Set<TrialPolicyVersion>().FromSql($"""
            SELECT * FROM public.trial_policy_versions WHERE status = 1 FOR UPDATE
            """).ToListAsync(token);
        return drafts.SingleOrDefault();
    }

    public Task<PlanVersion?> ReadPlanSourceAsync(Guid planId, Guid sourceId, CancellationToken token) =>
        context.Set<PlanVersion>().AsNoTracking().SingleOrDefaultAsync(
            version => version.PlanId == planId && version.Id == sourceId && version.Status != CatalogueVersionStatus.Draft, token);

    public Task<TrialPolicyVersion?> ReadTrialSourceAsync(Guid sourceId, CancellationToken token) =>
        context.Set<TrialPolicyVersion>().AsNoTracking().SingleOrDefaultAsync(
            version => version.Id == sourceId && version.Status != CatalogueVersionStatus.Draft, token);

    public async Task<int> NextPlanVersionNumberAsync(Guid planId, CancellationToken token) =>
        (await context.Set<PlanVersion>().Where(version => version.PlanId == planId)
            .MaxAsync(version => (int?)version.VersionNumber, token) ?? 0) + 1;

    public async Task<int> NextTrialVersionNumberAsync(CancellationToken token) =>
        (await context.Set<TrialPolicyVersion>().MaxAsync(version => (int?)version.VersionNumber, token) ?? 0) + 1;

    internal static Task LockFenceAsync(LicensingDbContext db, string resource, CancellationToken token) =>
        db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({resource}, 0))", token);

    public void Add(object entity) => context.Add(entity);

    private static string? SqlState(Exception exception) =>
        (exception as PostgresException ?? exception.InnerException as PostgresException)?.SqlState;
}
