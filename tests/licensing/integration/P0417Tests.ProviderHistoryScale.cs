using System.Data.Common;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0417Tests
{
    [Fact]
    public async Task ProviderHistoryUsesTheSameCommandsForOneTargetAndOneHundredWithRetainedHistory()
    {
        var commands = new HistoryCommands();
        var clock = new P0201Clock { Now = P0202Fixture.WholeSecondNow() };
        await using var fixture = await P0213Fixture.StartAsync(database, businessClock: clock,
            licensingServices: services => services.AddDbContext<LicensingDbContext>((_, options) => options.AddInterceptors(commands)));
        var (owner, account, route) = await CloseAsync(fixture, clock.Now);
        using var manager = await SignInAsync(fixture);
        await DrainBothAsync(fixture);
        using var reopened = await P0212Fixture.PostAsync(manager, route, Command(await PreviewAsync(manager, route), owner));
        Assert.True(reopened.IsSuccessStatusCode, await reopened.Content.ReadAsStringAsync());
        await DrainBothAsync(fixture);

        var one = Assert.Single((await ReadHistoryAsync(commands, manager, account)).EnumerateArray());
        Assert.Single(one.GetProperty("shops").EnumerateArray());
        var narrowCount = commands.Count;
        await SeedHistoryAsync(fixture, account, owner.ShopId, owner.UserId);
        var many = await ReadHistoryAsync(commands, manager, account);
        Assert.Equal(14, many.GetArrayLength());
        var wide = Assert.Single(many.EnumerateArray(), row => row.GetProperty("scope").GetString() == "Account");
        Assert.Equal(100, wide.GetProperty("shops").GetArrayLength());
        Assert.Equal(narrowCount, commands.Count);
        var query = Assert.IsType<string>(commands.Query);
        var plan = await ExplainHistoryAsync(fixture, account, query);
        Assert.Contains("entitlement_snapshots_closure_cycle", plan, StringComparison.Ordinal);
        Assert.Contains("Actual Rows", plan, StringComparison.Ordinal);
        if (Environment.GetEnvironmentVariable("P0417_HISTORY_PLAN_PATH") is { Length: > 0 } path)
        {
            await File.WriteAllTextAsync(path, $"Database commands: one target = {narrowCount}, 113 targets = {commands.Count}\n{plan}\n");
        }
    }

    private static async Task<JsonElement> ReadHistoryAsync(HistoryCommands commands, P0110CookieBrowser manager, Guid account)
    {
        commands.Reset();
        using var response = await manager.SendAsync(HttpMethod.Get, $"/api/admin/accounts/{account:D}/closures");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<string> ExplainHistoryAsync(P0213Fixture fixture, Guid account, string query)
    {
        await using var db = new NpgsqlConnection(fixture.Hosts.Fixture.Runtime.ConnectionString);
        await db.OpenAsync();
        await using var transaction = await db.BeginTransactionAsync();
        await using var setup = new NpgsqlCommand("SELECT set_config('app.billing_account_id', @account, true)", db, transaction);
        setup.Parameters.AddWithValue("account", account.ToString("D"));
        await setup.ExecuteNonQueryAsync();
        await using var explain = new NpgsqlCommand("EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) " + query, db, transaction);
        explain.Parameters.AddWithValue("p0", account);
        return Assert.IsType<string>(await explain.ExecuteScalarAsync());
    }

    private static Task SeedHistoryAsync(P0213Fixture fixture, Guid account, Guid shop, Guid actor) =>
        LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, $"""
            BEGIN;
            SELECT set_config('app.billing_account_id', '{account:D}', true);
            CREATE TEMP TABLE fixture_targets AS
                SELECT gen_random_uuid() AS shop_id, gen_random_uuid() AS request_id, gen_random_uuid() AS event_id
                FROM generate_series(1, 99);
            INSERT INTO public.shop_affiliation_requests
                (request_id, account_id, account_name, shop_id, requester_id, requester_revision,
                 operation_key, created_at, expires_at)
            SELECT request_id, '{account:D}', 'Scale fixture', shop_id, '{actor:D}', 1,
                gen_random_uuid(), now() - interval '1 day', now() + interval '1 day' FROM fixture_targets;
            INSERT INTO public.shop_affiliations (request_id, account_id, shop_id, event_id, actor_id,
                membership_id, authority_version, shop_name, owner_name, accepted_at, applied_at)
            SELECT request_id, '{account:D}', shop_id, gen_random_uuid(), '{actor:D}',
                gen_random_uuid(), 1, 'Scale shop', 'Fixture owner', now(), now() FROM fixture_targets;
            INSERT INTO fixture_targets SELECT shop_id, request_id, gen_random_uuid()
                FROM public.shop_affiliations WHERE shop_id = '{shop:D}';
            CREATE TEMP TABLE fixture_history AS
                SELECT gen_random_uuid() AS event_id, n AS revision, a.shop_id, a.request_id,
                    (SELECT max(shop_revision) FROM public.entitlement_snapshots WHERE shop_id = a.shop_id) AS cycle
                FROM public.shop_affiliations a CROSS JOIN generate_series(100, 4195) n
                WHERE a.shop_id = '{shop:D}';

            INSERT INTO public.entitlement_messages (event_id, account_id, shop_id, kind, payload, digest)
                SELECT event_id, '{account:D}'::uuid, shop_id, 'Snapshot', '\x7b7d'::bytea, repeat('a', 64) FROM fixture_targets
                UNION ALL SELECT event_id, '{account:D}'::uuid, shop_id, 'Snapshot', '\x7b7d'::bytea, repeat('a', 64) FROM fixture_history;
            INSERT INTO public.entitlement_outbox (event_id, account_id, shop_id, next_attempt_at)
                SELECT event_id, '{account:D}'::uuid, shop_id, now() FROM fixture_targets
                UNION ALL SELECT event_id, '{account:D}'::uuid, shop_id, now() FROM fixture_history WHERE revision >= 4184;
            INSERT INTO public.entitlement_snapshots
                (event_id, account_id, shop_id, affiliation_request_id, account_revision, shop_revision, closure_revision, publisher_protocol)
                SELECT event_id, '{account:D}'::uuid, shop_id, request_id, 1, 4196, 4196, 2 FROM fixture_targets
                UNION ALL
                SELECT event_id, '{account:D}'::uuid, shop_id, request_id, 1, revision,
                    CASE WHEN revision < 4184 THEN cycle ELSE revision END, 2 FROM fixture_history;

            CREATE TEMP TABLE fixture_closures AS
                SELECT gen_random_uuid() AS id, event_id, shop_id, request_id FROM fixture_history WHERE revision >= 4184;
            WITH account_close AS (
                INSERT INTO public.operational_closures (id, account_id, actor_id, operation_key, request_digest, financial_treatment, recorded_at)
                VALUES (gen_random_uuid(), '{account:D}', '{actor:D}', gen_random_uuid(), repeat('a', 64), 'NoFinancialChange', now())
                RETURNING id
            )
            INSERT INTO fixture_closures SELECT id, event_id, shop_id, request_id FROM account_close CROSS JOIN fixture_targets;
            INSERT INTO public.operational_closures (id, account_id, actor_id, shop_id, operation_key, request_digest, financial_treatment, recorded_at)
                SELECT id, '{account:D}', shop_id, shop_id, gen_random_uuid(), repeat('a', 64), 'NoFinancialChange', now()
                FROM fixture_closures GROUP BY id, shop_id HAVING id IN (SELECT id FROM fixture_closures GROUP BY id HAVING count(*) = 1);
            INSERT INTO public.operational_closure_targets
                (closure_id, account_id, shop_id, affiliation_request_id, shop_name, consent_id, work_digest, event_id)
                SELECT id, '{account:D}', shop_id, request_id, 'Scale shop', gen_random_uuid(), repeat('a', 64), event_id FROM fixture_closures;
            ANALYZE public.entitlement_snapshots;
            COMMIT;
            """);

    private sealed class HistoryCommands : DbCommandInterceptor
    {
        internal int Count { get; private set; }
        internal string? Query { get; private set; }

        internal void Reset()
        {
            Count = 0;
            Query = null;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Count++;
            if (command.CommandText.Contains("AS reopening_propagation", StringComparison.Ordinal))
                Query = command.CommandText;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Count++;
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
