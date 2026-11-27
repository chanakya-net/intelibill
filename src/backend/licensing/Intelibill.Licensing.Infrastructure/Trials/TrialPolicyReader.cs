using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Intelibill.Licensing.Application.Catalogue;
using Intelibill.Licensing.Application.Trials;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Trials;

internal sealed class TrialPolicyReader(LicensingDbContext db, ICataloguePublicationApproval approval) : ITrialPolicyReader
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ShopTrialPolicyV1?> ReadAsync(Guid? previewReplayId, CancellationToken ct)
    {
        if (previewReplayId is { } replay)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                SELECT set_config('app.trial_preview_replay', {replay.ToString("D")}, true)
                """, ct);
        }
        else
        {
            // Publication/retirement take the exclusive version of the same transaction fence.
            await db.Database.ExecuteSqlRawAsync("""
                SELECT pg_advisory_xact_lock_shared(hashtextextended('catalogue:trial', 0))
                """, ct);
        }

        var row = await db.Database.SqlQuery<PolicyRow>($"""
            SELECT id, revision, version_number, duration_days, active_member_cap, max_concurrent_trial_shops,
                trial_terms_version, source FROM public.trial_policy_versions WHERE status = 2
            """).SingleOrDefaultAsync(ct);
        if (row is not { DurationDays: ShopTrialsV1.DurationDays, ActiveMemberCap: > 0,
            MaxConcurrentTrialShops: > 0, TrialTermsVersion.Length: > 0, Revision: > 0 } ||
            !approval.Allows((CatalogueSource)row.Source))
            return null;

        var snapshot = new PublishedTrialPolicyV1(row.Id, row.VersionNumber, row.DurationDays, row.ActiveMemberCap.Value,
            row.MaxConcurrentTrialShops.Value, row.TrialTermsVersion, row.Source == (int)CatalogueSource.DevelopmentFixture);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(snapshot, Json))));
        return approval.Approves(hash, row.Id, row.TrialTermsVersion)
            ? new ShopTrialPolicyV1(row.Id, row.Revision, row.TrialTermsVersion, snapshot.ActiveMemberCap,
                snapshot.MaxConcurrentTrialShops, snapshot.DevelopmentFixture)
            : null;
    }

    private sealed record PolicyRow(Guid Id, int Revision, int VersionNumber, int DurationDays, int? ActiveMemberCap,
        int? MaxConcurrentTrialShops, string? TrialTermsVersion, int Source);
}
