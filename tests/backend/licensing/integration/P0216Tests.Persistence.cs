using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;
using static Intelibill.Licensing.Integration.Tests.P0215Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0216Tests
{
    [Fact]
    public async Task CutoffIsEvaluatedInsideTheAccountFence()
    {
        await using var fixture = await P0213Fixture.StartAsync(database);
        var payer = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var account = await CreateAccountAsync(payer.Client);
        var term = await SeedPaidAsync(fixture.Hosts, payer, account, payer);
        fixture.Hosts.StatusClock.Now = EndsAt.AddTicks(-10);
        var cancellation = await ScheduleAsync(payer, account, term);

        HttpResponseMessage response;
        await using (var connection = new NpgsqlConnection(fixture.Hosts.LicensingOwner.ConnectionString))
        {
            await connection.OpenAsync();
            await using var fence = await connection.BeginTransactionAsync();
            await using (var take = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended($1, 0))", connection, fence))
            {
                take.Parameters.AddWithValue("b" + account.ToString("D"));
                await take.ExecuteNonQueryAsync();
            }

            var pending = payer.Client.PostAsJsonAsync(WithdrawalPath(account, term), Withdraw(cancellation, Guid.NewGuid()));
            await WaitForAdvisoryWaiterAsync(fixture, connection.ProcessID);
            fixture.Hosts.StatusClock.Now = EndsAt;
            await fence.CommitAsync();
            response = await pending;
        }

        using (response)
            await AssertProblemAsync(response, HttpStatusCode.Conflict, "Billing.CancellationEffective");
        Assert.Equal(1L, await fixture.Trial.CountAsync("scheduled_subscription_changes"));
        Assert.Equal(1L, await fixture.Trial.CountAsync("subscription_cancellation_receipts"));
    }

    [Fact]
    public async Task ConcurrentWithdrawalsHaveOneEffect()
    {
        await using var fixture = await P0213Fixture.StartAsync(database);
        var payer = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var account = await CreateAccountAsync(payer.Client);
        var term = await SeedPaidAsync(fixture.Hosts, payer, account, payer);
        fixture.Hosts.StatusClock.Now = EndsAt.AddDays(-3);
        var cancellation = await ScheduleAsync(payer, account, term);
        var same = Withdraw(cancellation, Guid.NewGuid());
        var path = WithdrawalPath(account, term);
        var responses = await Task.WhenAll(payer.Client.PostAsJsonAsync(path, same), payer.Client.PostAsJsonAsync(path, same),
            payer.Client.PostAsJsonAsync(path, Withdraw(cancellation, Guid.NewGuid())));
        try
        {
            Assert.Contains(responses[2].StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
            var sameWon = responses[2].StatusCode == HttpStatusCode.Conflict;
            Assert.Equal(sameWon ? HttpStatusCode.OK : HttpStatusCode.Conflict, responses[0].StatusCode);
            Assert.Equal(responses[0].StatusCode, responses[1].StatusCode);
            if (sameWon) Assert.True(JsonElement.DeepEquals(await ReadAsync(responses[0]), await ReadAsync(responses[1])));
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }

        Assert.Equal(2L, await fixture.Trial.CountAsync("scheduled_subscription_changes"));
        Assert.Equal(2L, await fixture.Trial.CountAsync("subscription_cancellation_receipts"));
    }

    [Fact]
    public async Task WithdrawalEvidenceIsIsolatedAndForgeriesAreRejected()
    {
        await using var fixture = await P0213Fixture.StartAsync(database);
        var payer = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var other = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var account = await CreateAccountAsync(payer.Client);
        var term = await SeedPaidAsync(fixture.Hosts, payer, account, payer);
        var otherTerm = await SeedPaidAsync(fixture.Hosts, payer, account, other);
        fixture.Hosts.StatusClock.Now = EndsAt.AddDays(-3);
        var cancellation = await ScheduleAsync(payer, account, term);
        await ScheduleAsync(payer, account, otherTerm);
        Assert.Equal(cancellation.GetProperty("changeId").GetGuid(),
            (await StatusAsync(payer)).GetProperty("term").GetProperty("cancellation").GetProperty("changeId").GetGuid());

        using (var withdrawn = await payer.Client.PostAsJsonAsync(WithdrawalPath(account, term), Withdraw(cancellation, Guid.NewGuid())))
            await ReadAsync(withdrawn);
        await CheckWithdrawalIsolationAsync(fixture, account, payer);
        await AssertForgedWithdrawalsRejectedAsync(fixture, term, otherTerm);
    }

    private static async Task CheckWithdrawalIsolationAsync(P0213Fixture fixture, Guid account, Merchant owner)
    {
        var settings = fixture.Hosts.Fixture.Runtime;
        Assert.Equal("0,0,0", await ScopedAsync(settings, null, null, null));
        Assert.Equal("0,0,0", await ScopedAsync(settings, Guid.NewGuid(), null, owner.UserId));
        Assert.Equal("2,1,0", await ScopedAsync(settings, null, owner.ShopId, null));
        Assert.Equal("3,2,0", await ScopedAsync(settings, account, null, null));
        Assert.Equal("3,2,3", await ScopedAsync(settings, account, null, owner.UserId));
        foreach (var command in new[] { "UPDATE scheduled_subscription_changes SET withdraws_change_id = withdraws_change_id",
                     "DELETE FROM scheduled_subscription_changes WHERE kind = 'WithdrawCancellation'",
                     "TRUNCATE scheduled_subscription_changes CASCADE" })
        {
            var exception = await Assert.ThrowsAsync<PostgresException>(() =>
                LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, command));
            Assert.Equal("55000", exception.SqlState);
        }
    }

    private static async Task AssertForgedWithdrawalsRejectedAsync(P0213Fixture fixture, Guid withdrawnTerm, Guid scheduledTerm)
    {
        var forgeries = new (string Sql, string State)[]
        {
            (Forge(scheduledTerm, "2", "s.recorded_at + interval '1 second'"), "23514"),
            (Forge(scheduledTerm, "4", "s.recorded_at + interval '1 second'"), "23514"),
            (Forge(scheduledTerm, "2", "s.effective_at"), "23514"),
            (Forge(withdrawnTerm, "4", "s.recorded_at + interval '1 second'"), "23505"),
            ($"""
                INSERT INTO scheduled_subscription_changes
                    (change_id, account_id, term_id, revision, kind, effective_at, recorded_at, policy_version, shop_count, shop_hash)
                SELECT gen_random_uuid(), account_id, term_id, 2, 'ScheduleCancellation', effective_at, recorded_at,
                    policy_version, shop_count, shop_hash
                FROM scheduled_subscription_changes WHERE term_id = '{scheduledTerm:D}'
                """, "23514"),
            ($"""
                INSERT INTO scheduled_subscription_change_shops (account_id, change_id, shop_id, request_id, shop_name)
                SELECT w.account_id, w.change_id, s.shop_id, s.request_id, s.shop_name
                FROM scheduled_subscription_changes w JOIN scheduled_subscription_change_shops s ON s.change_id = w.withdraws_change_id
                WHERE w.term_id = '{withdrawnTerm:D}'
                """, "23514"),
        };
        foreach (var (sql, state) in forgeries)
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() =>
                LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, sql));
            Assert.Equal(state, error.SqlState);
        }
    }

    // A withdrawal row copied from the term's first scheduled change, with no acceptance receipt.
    private static string Forge(Guid term, string revision, string recordedAt) => $"""
        INSERT INTO scheduled_subscription_changes (change_id, account_id, term_id, revision, kind, effective_at,
            recorded_at, policy_version, shop_count, shop_hash, withdraws_change_id)
        SELECT gen_random_uuid(), s.account_id, s.term_id, {revision}, 'WithdrawCancellation', s.effective_at, {recordedAt},
            s.policy_version, s.shop_count, s.shop_hash, s.change_id
        FROM scheduled_subscription_changes s WHERE s.term_id = '{term:D}' AND s.revision = 1
        """;

    private static async Task<string?> ScopedAsync(NpgsqlConnectionStringBuilder settings, Guid? account, Guid? shop, Guid? actor)
    {
        await using var connection = new NpgsqlConnection(settings.ConnectionString);
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        await using var scope = new NpgsqlCommand("""
            SELECT set_config('app.billing_account_id', $1, true), set_config('app.status_shop_id', $2, true),
                set_config('app.billing_actor_id', $3, true)
            """, connection, tx);
        foreach (var id in new[] { account, shop, actor }) scope.Parameters.AddWithValue(id?.ToString("D") ?? "");
        await scope.ExecuteNonQueryAsync();
        await using var counts = new NpgsqlCommand("""
            SELECT concat_ws(',', (SELECT count(*) FROM scheduled_subscription_changes),
                (SELECT count(*) FROM scheduled_subscription_change_shops), (SELECT count(*) FROM subscription_cancellation_receipts))
            """, connection, tx);
        return (string?)await counts.ExecuteScalarAsync();
    }

    private static async Task WaitForAdvisoryWaiterAsync(P0213Fixture fixture, int blocker)
    {
        await using var observer = new NpgsqlConnection(fixture.Hosts.LicensingOwner.ConnectionString);
        await observer.OpenAsync();
        await using var count = new NpgsqlCommand("""
            SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event = 'advisory'
                AND $1 = ANY(pg_blocking_pids(pid))
            """, observer);
        count.Parameters.AddWithValue(blocker);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while ((long)(await count.ExecuteScalarAsync(timeout.Token))! != 1)
            await Task.Delay(15, timeout.Token);
    }

    [Fact]
    public async Task WithdrawnTermAcceptsCoverageAndCanBeScheduledAgain()
    {
        await using var fixture = await P0213Fixture.StartAsync(database);
        var payer = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var second = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var account = await CreateAccountAsync(payer.Client);
        var (term, end) = await SeedCurrentTermAsync(fixture, account, capacity: 2);
        await P0213Fixture.LinkAsync(account, payer, payer, term);
        var cancellation = await ScheduleAsync(payer, account, term);
        using (var withdrawn = await payer.Client.PostAsJsonAsync(WithdrawalPath(account, term), Withdraw(cancellation, Guid.NewGuid())))
            await ReadAsync(withdrawn);

        using (var listed = await payer.Client.GetAsync(new Uri($"/api/billing/accounts/{account:D}/affiliations", UriKind.Relative)))
            Assert.Contains((await ReadAsync(listed)).GetProperty("terms").EnumerateArray(), row => row.GetProperty("termId").GetGuid() == term);
        await ApplyRequestAsync(second, await RequestAsync(payer, second, account, term));
        var path = P0215Tests.CancellationPath(account, term);
        var preview = await PreviewAsync(payer, path);
        Assert.Equal(2L, preview.GetProperty("version").GetInt64());
        Assert.Equal(JsonValueKind.Null, preview.GetProperty("changeId").ValueKind);
        Assert.Equal(2, preview.GetProperty("affectedShopCount").GetInt32());

        var stale = JsonSerializer.SerializeToNode(P0215Tests.Accept(preview, Guid.NewGuid()))!.AsObject();
        stale["expectedVersion"] = 0;
        using (var refused = await payer.Client.PostAsJsonAsync(path, stale))
            await AssertProblemAsync(refused, HttpStatusCode.Conflict, "Billing.CancellationConflict");
        using var again = await payer.Client.PostAsJsonAsync(path, P0215Tests.Accept(preview, Guid.NewGuid()));
        var rescheduled = (await ReadAsync(again)).GetProperty("cancellation");
        Assert.Equal(3L, rescheduled.GetProperty("version").GetInt64());
        var status = (await StatusAsync(second)).GetProperty("term");
        Assert.Equal(3L, status.GetProperty("cancellation").GetProperty("version").GetInt64());
        Assert.Equal(JsonValueKind.Null, status.GetProperty("graceEndsAt").ValueKind);
        Assert.Equal(end, status.GetProperty("paidThrough").GetDateTimeOffset());

        using (var old = await payer.Client.PostAsJsonAsync(WithdrawalPath(account, term), Withdraw(cancellation, Guid.NewGuid())))
            await AssertProblemAsync(old, HttpStatusCode.Conflict, "Billing.CancellationConflict");
        using var latest = await payer.Client.PostAsJsonAsync(WithdrawalPath(account, term), Withdraw(rescheduled, Guid.NewGuid()));
        Assert.Equal(4L, (await ReadAsync(latest)).GetProperty("version").GetInt64());
        Assert.Equal(end.AddDays(7), (await StatusAsync(second)).GetProperty("term").GetProperty("graceEndsAt").GetDateTimeOffset());
    }
}
