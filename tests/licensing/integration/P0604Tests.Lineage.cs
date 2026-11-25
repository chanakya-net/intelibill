using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0604Tests
{
    // One more contiguous monthly successor than the earlier 120-edge walk could follow.
    private const int Successors = 121;

    [Fact]
    public async Task AssignmentFollowsItsRenewalLineagePast120SuccessorsAndStopsAtARestoration()
    {
        await using var fixture = await StartAsync(database, January31);
        var (p, term) = await PrepareAsync(fixture);
        var shop = p.Shops[2];
        var delivery = fixture.Checkout.Delivery;
        var assignment = await AcceptSlotAsync(p, term, shop);
        await delivery.DrainAsync(false);
        await delivery.DrainAsync(true);
        await delivery.DrainAsync(false);

        var (tail, end) = (term, February29);
        for (var successor = 1; successor <= Successors; successor++)
        {
            SyncAt(fixture, end.AddDays(-1));
            // A decade of renewals arrives within seconds of the merchant's request clock.
            await LicensingPostgreSqlFixture.ExecuteAsync(delivery.MerchantDb, "TRUNCATE public.rate_limit_buckets");
            var (_, paid) = await RenewAsync(p, tail, "Month");
            (tail, end) = (paid.GetProperty("termId").GetGuid(), paid.GetProperty("termEndsAt").GetDateTimeOffset());
        }
        Assert.Equal(Successors + 1L, await fixture.CountAsync("payment_fulfilment_shops", $"slot_assignment_id='{assignment}'"));

        // Inside the 121st successor the merchant confirms the tail and the assignment reads it, with its dates.
        var inTail = end.AddDays(-2);
        SyncAt(fixture, inTail);
        await DeliverSingleSnapshotAsync(fixture, await LatestSnapshotAsync(fixture, shop.ShopId), expectOutcome: "Applied");
        await delivery.DrainAsync(false);
        Assert.Equal(tail, (await ShopTermAsync(p, shop)).GetProperty("termId").GetGuid());
        Assert.Equal("Acknowledged", (await ReadAssignmentAsync(p.Payer.Client, Slots(p, term), assignment)).GetProperty("activation").GetString());
        Assert.Equal((end, end.AddDays(7)), await StatusDatesAsync(fixture, p.Account, assignment, inTail));

        // A restoration after the lapse starts fresh coverage; the assignment's lineage still stops at the old tail.
        var restoredAt = end.AddDays(10);
        SyncAt(fixture, restoredAt);
        var quote = await AcceptAsync(p, await RenewalQuoteAsync(p, tail, "Month"));
        Assert.Equal("RestoredAccess", quote.StartRule);
        var restored = (await PayAsync(p, quote)).GetProperty("termId").GetGuid();
        Assert.Equal(restored, (await ShopTermAsync(p, shop)).GetProperty("termId").GetGuid());
        Assert.Equal("TermEnded", (await ReadAssignmentAsync(p.Payer.Client, Slots(p, term), assignment)).GetProperty("activation").GetString());
        Assert.Equal((end, end.AddDays(7)), await StatusDatesAsync(fixture, p.Account, assignment, restoredAt));
        Assert.Equal(Successors + 1L, await fixture.CountAsync("payment_fulfilment_shops", $"slot_assignment_id='{assignment}'"));
    }

    private static async Task<(DateTimeOffset PaidThrough, DateTimeOffset AccessEndsAt)> StatusDatesAsync(
        P0308Fixture fixture, Guid account, Guid assignment, DateTimeOffset at)
    {
        async Task<DateTimeOffset> ReadAsync(string column) => new((DateTime)(await fixture.ScalarAsync($"""
            SELECT st.{column} AT TIME ZONE 'UTC'
            FROM (SELECT set_config('app.billing_account_id', '{account:D}', true) AS scope) s
            CROSS JOIN LATERAL public.slot_assignment_status(s.scope::uuid, '{assignment:D}', '{at.UtcDateTime:O}') st
            """))!, TimeSpan.Zero);
        return (await ReadAsync("effective_paid_through"), await ReadAsync("effective_access_ends_at"));
    }
}
