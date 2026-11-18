using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Licensing.Application.Payments;
using Intelibill.Shared.Contracts.V1;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// A trial delivery and a paid fulfilment for the same shop meet at the billing-account lock. A table fence pauses
/// whichever side took the lock first, so the other side is provably waiting behind it with its first read done.
/// </summary>
public sealed partial class P0302Tests
{
    [Fact]
    public async Task TrialWaitingBehindFulfilmentLosesToThePaidTerm()
    {
        await using var fixture = await P0302Fixture.StartAsync(database, new P0201Clock { Now = P0302Fixture.WholeSecondStart });
        var (payer, account, _, quote) = await fixture.AcceptedQuoteAsync(1);
        var attempt = (await StartAsync(payer.Client, quote, HttpStatusCode.Created)).GetProperty("attemptId").GetGuid();
        await CaptureAsync(payer.Client, quote, attempt);

        await using var fence = await FenceAsync(fixture, "payment_fulfilments");
        var verify = payer.Client.PostAsJsonAsync(VerifyPath(quote, attempt), new { launcherOutcome = (string?)null });
        await WaitForBlockedChainAsync(fixture, fence.Pid, 1);
        using var licensing = fixture.Hosts.Licensing.CreateClient();
        using var delivery = fixture.Trial.Delivery(TrialConsent(fixture, payer));
        var trial = licensing.SendAsync(delivery);
        await WaitForBlockedChainAsync(fixture, fence.Pid, 2);
        await fence.ReleaseAsync();

        using var verified = await verify;
        AssertStatus(await ReadAsync(verified), "Verified", "Fulfilled", "Applying");
        var refused = await ReadTrialAsync(await trial);
        Assert.Equal(("NeedsReview", "CoverageExists"), (refused.Status, refused.Reason));
        await AssertSingleFulfilmentAsync(fixture, quote, 1);
        Assert.Equal(0L, await fixture.CountAsync("trial_history", $"shop_id = '{payer.ShopId:D}'"));
        var shop = await ReadAsync(await ShopStatusAsync(payer.Client, payer.ShopId));
        Assert.Equal("Active", shop.GetProperty("state").GetString());
        Assert.Equal("Paid", shop.GetProperty("term").GetProperty("basis").GetString());
        var snapshot = await fixture.Delivery.LatestSnapshotAsync(payer.ShopId);
        Assert.Equal(("Active", "Paid"), (snapshot.Shop!.State, snapshot.Shop.Basis));
        Assert.Equal(account, snapshot.AccountId);
    }

    [Fact]
    public async Task FulfilmentWaitingBehindTrialRetainsMoneyForReview()
    {
        await using var fixture = await P0302Fixture.StartAsync(database, new P0201Clock { Now = P0210Fixture.Start });
        var (payer, _, _, quote) = await fixture.AcceptedQuoteAsync(1);
        var attempt = (await StartAsync(payer.Client, quote, HttpStatusCode.Created)).GetProperty("attemptId").GetGuid();
        await CaptureAsync(payer.Client, quote, attempt);

        await using var fence = await FenceAsync(fixture, "trial_history");
        using var licensing = fixture.Hosts.Licensing.CreateClient();
        using var delivery = fixture.Trial.Delivery(TrialConsent(fixture, payer));
        var trial = licensing.SendAsync(delivery);
        await WaitForBlockedChainAsync(fixture, fence.Pid, 1);
        var verify = payer.Client.PostAsJsonAsync(VerifyPath(quote, attempt), new { launcherOutcome = (string?)null });
        await WaitForBlockedChainAsync(fixture, fence.Pid, 2);
        await fence.ReleaseAsync();

        Assert.Equal("Applied", (await ReadTrialAsync(await trial)).Status);
        using var verified = await verify;
        AssertStatus(await ReadAsync(verified), "Verified", "NeedsReview", "NotApplicable");
        var order = $"order_id = '{quote.OrderId:D}'";
        Assert.Equal(1L, await fixture.CountAsync("verified_payments", order));
        Assert.Equal(0L, await fixture.CountAsync("payment_fulfilments", order));
        Assert.Equal(0L, await fixture.CountAsync("payment_fulfilment_shops"));
        Assert.Equal(1L, await fixture.CountAsync("trial_history", $"shop_id = '{payer.ShopId:D}'"));
        Assert.Equal(1L, await fixture.CountAsync("payment_events", $"kind = '{PaymentEventKinds.PaidNeedsReview}'"));
    }

    [Fact]
    public async Task TrialHistoryCannotBeWrittenForAPaidShop()
    {
        await using var fixture = await P0302Fixture.StartAsync(database, new P0201Clock { Now = P0210Fixture.Start });
        var (payer, account, _, quote) = await fixture.AcceptedQuoteAsync(1);
        var attempt = (await StartAsync(payer.Client, quote, HttpStatusCode.Created)).GetProperty("attemptId").GetGuid();
        await CaptureAsync(payer.Client, quote, attempt);
        AssertStatus(await VerifyAsync(payer.Client, quote, attempt), "Verified", "Fulfilled", "Applying");

        // A stale writer that skipped the eligibility re-read: every trial fact is valid except the shop is paid.
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, DirectTrial(Guid.NewGuid(), account));
        var refused = await Assert.ThrowsAsync<PostgresException>(() =>
            LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, DirectTrial(payer.ShopId, account)));
        Assert.Equal("23514", refused.SqlState);
        Assert.Equal("Trial coverage conflicts with paid coverage", refused.MessageText);
        Assert.Equal(0L, await fixture.CountAsync("trial_history", $"shop_id = '{payer.ShopId:D}'"));
    }

    private static string DirectTrial(Guid shop, Guid account)
    {
        var (term, evt, replay, digest) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new string('a', 64));
        return $"""
            BEGIN;
            INSERT INTO public.subscription_coverage_terms (term_id, account_id, mode, capacity, starts_at, ends_at)
            VALUES ('{term:D}', '{account:D}', 'Independent', 1, '2027-02-10Z', '2027-02-24Z');
            INSERT INTO public.shop_trial_delivery_uses (replay_id, event_id, shop_id, digest, expires_at)
            VALUES ('{replay:D}', '{evt:D}', '{shop:D}', '{digest}', '2027-02-11Z');
            INSERT INTO public.shop_trial_inbox (event_id, shop_id, actor_id, operation_key, account_id, term_id,
                replay_id, digest, status)
            VALUES ('{evt:D}', '{shop:D}', gen_random_uuid(), gen_random_uuid(), '{account:D}', '{term:D}',
                '{replay:D}', '{digest}', 'Applied');
            INSERT INTO public.trial_history (shop_id, account_id, term_id, event_id, operation_key, actor_id,
                membership_id, authority_version, policy_id, policy_revision, terms_version, member_cap,
                account_trial_cap, active_members, starts_at, ends_at, applied_at, consent_digest,
                development_fixture, computation_version)
            SELECT '{shop:D}', '{account:D}', '{term:D}', '{evt:D}', gen_random_uuid(), gen_random_uuid(),
                gen_random_uuid(), 0, id, revision, trial_terms_version, active_member_cap, max_concurrent_trial_shops, 1,
                '2027-02-10Z', '2027-02-24Z', '2027-02-10Z', '{digest}', source = 1, 'shop-trial/1'
            FROM public.trial_policy_versions WHERE status = 2 ORDER BY revision DESC LIMIT 1;
            COMMIT;
            """;
    }

    private static ShopTrialConsentV1 TrialConsent(P0302Fixture fixture, Merchant owner) =>
        new(Guid.NewGuid(), owner.ShopId, owner.UserId, Guid.NewGuid(), 0, 1, fixture.Hosts.StatusClock.Now,
            fixture.Trial.Accept());

    private static async Task<ShopTrialDeliveryResultV1> ReadTrialAsync(HttpResponseMessage response)
    {
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.OK, body);
            return JsonSerializer.Deserialize<ShopTrialDeliveryResultV1>(body, JsonSerializerOptions.Web)!;
        }
    }

    internal static async Task<TableFence> FenceAsync(P0302Fixture fixture, string table)
    {
        var connection = new NpgsqlConnection(fixture.Hosts.LicensingOwner.ConnectionString);
        await connection.OpenAsync();
        var transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand($"LOCK TABLE public.{table} IN SHARE MODE", connection, transaction);
        await command.ExecuteNonQueryAsync();
        return new(connection, transaction);
    }

    /// <summary>Waits until <paramref name="expected"/> licensing sessions are blocked, directly or through each other, by the fence.</summary>
    internal static async Task<int[]> WaitForBlockedChainAsync(P0302Fixture fixture, int fence, int expected)
    {
        await using var observer = new NpgsqlConnection(fixture.Hosts.LicensingOwner.ConnectionString);
        await observer.OpenAsync();
        await using var count = new NpgsqlCommand("""
            WITH RECURSIVE blocked(pid) AS (
                SELECT pid FROM pg_stat_activity WHERE datname = current_database() AND $1 = ANY(pg_blocking_pids(pid))
                UNION
                SELECT a.pid FROM pg_stat_activity a JOIN blocked b ON b.pid = ANY(pg_blocking_pids(a.pid))
                WHERE a.datname = current_database())
            SELECT coalesce(array_agg(pid ORDER BY pid), ARRAY[]::int[]) FROM blocked
            """, observer);
        count.Parameters.AddWithValue(fence);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (true)
        {
            var pids = (int[])(await count.ExecuteScalarAsync(timeout.Token))!;
            if (pids.Length >= expected) return pids;
            await Task.Delay(15, timeout.Token);
        }
    }

    internal sealed class TableFence(NpgsqlConnection connection, NpgsqlTransaction transaction) : IAsyncDisposable
    {
        internal int Pid => connection.ProcessID;

        internal Task ReleaseAsync() => transaction.CommitAsync();

        public async ValueTask DisposeAsync()
        {
            await transaction.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
