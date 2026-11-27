using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0416Tests
{
    private static async Task<string> SeedObservedSubmissionsAsync(P0213Fixture fixture, Merchant owner, DateTimeOffset now)
    {
        await using var context = MerchantDb(fixture.Hosts.Database);
        context.OfflineSubmissions.AddRange(
            OfflineSubmission.Record(owner.ShopId, "direct-device", "fixture-sync", "A/2627/000001",
                new string('a', 64), """{"fixture":"direct"}""", now.AddMinutes(-5), now, OfflineSubmissionSource.Sync,
                owner.UserId, null, null, null, null),
            OfflineSubmission.Record(owner.ShopId, "disconnected-device", "fixture-recovery", "A/2627/000002",
                new string('b', 64), """{"fixture":"recovery"}""", now.AddMinutes(-10), now, OfflineSubmissionSource.Recovery,
                owner.UserId, null, null, null, "support-import"));
        await context.SaveChangesAsync();
        return await RetainedSubmissionsAsync(fixture, owner);
    }

    private static async Task<string> RetainedSubmissionsAsync(P0213Fixture fixture, Merchant owner) =>
        (string)(await fixture.MerchantScalarAsync($"""
            SELECT jsonb_agg(to_jsonb(s) ORDER BY s.id)::text FROM public.offline_submissions s WHERE shop_id = '{owner.ShopId:D}'
            """))!;
}
