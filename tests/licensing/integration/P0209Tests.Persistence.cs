using System.Text.Json;
using Intelibill.Domain.Enums;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0209Tests
{
    [Fact]
    public async Task PersistedStatusAdvancesAtExactBoundariesWithoutAScheduler()
    {
        await using var host = await StartAsync(database, Utc("2027-02-28T04:29:59.999999Z"));
        var payer = await SignInAsync(host);
        var account = await CreateAccountAsync(payer.Client);
        var paid = await SignInAsync(host, ShopRole.Owner);
        var trial = await SignInAsync(host, ShopRole.Owner);
        var paidTerm = await SeedTermAsync(host, account, new TermSeed(Jan31, Feb28, AnchorLocal: "2027-01-31 10:00:00"));
        var trialTerm = await SeedTermAsync(host, account, new TermSeed(Jan31, Feb28, Basis: "Trial",
            AnchorLocal: "2027-01-31 10:00:00", PricePaise: null));
        await AffiliateAsync(host, account, payer.UserId, paid.ShopId, paid.UserId, paidTerm, Jan31, Feb28);
        await AffiliateAsync(host, account, payer.UserId, trial.ShopId, trial.UserId, trialTerm, Jan31, Feb28);
        var before = await TermEvidenceAsync(host);

        (string Paid, string Trial)[] expected = [("Active", "Active"), ("Grace", "Expired"), ("Grace", "Expired"), ("Expired", "Expired")];
        string[] instants = ["2027-02-28T04:29:59.999999Z", "2027-02-28T04:30:00Z", "2027-03-07T04:29:59.999999Z", "2027-03-07T04:30:00Z"];
        foreach (var (instant, states) in instants.Zip(expected))
        {
            host.StatusClock.Now = Utc(instant);
            var paidStatus = await ReadAsync(await ShopStatusAsync(paid.Client, paid.ShopId));
            var trialStatus = await ReadAsync(await ShopStatusAsync(trial.Client, trial.ShopId));
            Assert.True(states.Paid == paidStatus.GetProperty("state").GetString(), $"{instant} paid {paidStatus}");
            Assert.True(states.Trial == trialStatus.GetProperty("state").GetString(), $"{instant} trial {trialStatus}");
            Assert.Equal(JsonValueKind.Null, trialStatus.GetProperty("term").GetProperty("graceEndsAt").ValueKind);
            Assert.Equal(Utc(instant), paidStatus.GetProperty("evaluatedAt").GetDateTimeOffset());
        }

        // Evaluation never writes: the persisted evidence is byte-for-byte unchanged.
        Assert.Equal(before, await TermEvidenceAsync(host));
    }

    [Fact]
    public async Task ContradictoryFactsNeedReviewAndSupplementsAreImmutableUnderRuntimeCredentials()
    {
        await using var host = await StartAsync(database, Utc("2027-02-10T00:00:00Z"));
        var payer = await SignInAsync(host);
        var account = await CreateAccountAsync(payer.Client);
        var owner = await SignInAsync(host, ShopRole.Owner);
        // Facts claim the 31 Jan anchor but the coverage runs to 1 Mar: a chained or invented date is never trusted.
        var term = await SeedTermAsync(host, account, new TermSeed(Jan31, Utc("2027-03-01T04:30:00Z"),
            AnchorLocal: "2027-01-31 10:00:00"));
        await AffiliateAsync(host, account, payer.UserId, owner.ShopId, owner.UserId, term, Jan31, Utc("2027-03-01T04:30:00Z"));

        var status = await ReadAsync(await ShopStatusAsync(owner.Client, owner.ShopId));
        Assert.Equal("NeedsReview", status.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, status.GetProperty("term").GetProperty("graceEndsAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, status.GetProperty("term").GetProperty("basis").ValueKind);

        await using var runtime = new NpgsqlConnection(host.Fixture.Runtime.ConnectionString);
        await runtime.OpenAsync();
        // No runtime UPDATE/DELETE policy exists: the rows are invisible to those statements; the owner's trigger refuses too.
        Assert.Equal(0, await RuntimeChangeAsync(runtime, "UPDATE public.subscription_term_facts SET member_allowance = 99", account));
        Assert.Equal(0, await RuntimeChangeAsync(runtime, "DELETE FROM public.subscription_term_facts", account));
        await using (var migrator = new NpgsqlConnection(host.LicensingOwner.ConnectionString))
        {
            await migrator.OpenAsync();
            await using var update = new NpgsqlCommand("UPDATE public.subscription_term_facts SET member_allowance = 99", migrator);
            Assert.Equal("55000", (await Assert.ThrowsAsync<PostgresException>(() => update.ExecuteNonQueryAsync())).SqlState);
        }
        await AssertRefusedAsync(runtime, "TRUNCATE public.subscription_term_facts", "55000", account);
        await AssertRefusedAsync(runtime, $"""
            INSERT INTO public.subscription_term_facts (term_id, account_id, basis, billing_period, anchor_local, time_zone,
                period_ordinal, calendar_version, access_policy_version, member_allowance, price_paise, currency,
                source_reference, development_fixture, recorded_at)
            SELECT gen_random_uuid(), '{account:D}', 'Paid', 'Month', '2027-01-31 10:00', 'Asia/Kolkata', 0, 'c', 'a', 1,
                0, 'INR', 'runtime', false, now()
            """, "42501", account);

        // Without a scope the runtime role sees nothing; another account's scope sees nothing either.
        Assert.Equal(0L, await P0202Fixture.RuntimeScalarAsync(host.Fixture, null, null, "SELECT count(*) FROM public.subscription_term_facts"));
        Assert.Equal(0L, await P0202Fixture.RuntimeScalarAsync(host.Fixture, Guid.NewGuid(), null, "SELECT count(*) FROM public.subscription_term_facts"));
        Assert.Equal(1L, await P0202Fixture.RuntimeScalarAsync(host.Fixture, account, null, "SELECT count(*) FROM public.subscription_term_facts"));
    }

    [Fact]
    public async Task FactConstraintsRefuseInventedOrUnlimitedValues()
    {
        await using var host = await StartAsync(database, Utc("2027-02-10T00:00:00Z"));
        var payer = await SignInAsync(host);
        var account = await CreateAccountAsync(payer.Client);
        await using var connection = new NpgsqlConnection(host.LicensingOwner.ConnectionString);
        await connection.OpenAsync();
        TermSeed[] invalid =
        [
            new(Jan31, Feb28, AnchorLocal: "2027-01-31 10:00:00", PricePaise: null),
            new(Jan31, Feb28, AnchorLocal: "2027-01-31 10:00:00", MemberAllowance: 0),
            new(Jan31, Feb28, AnchorLocal: "2027-01-31 10:00:00", PricePaise: -1),
            new(Jan31, Feb28, AnchorLocal: "2027-01-31 10:00:00.5"),
            new(Jan31, Feb28, AnchorLocal: "2027-01-31 10:00:00", Zone: "UTC"),
            new(Jan31, Feb28, Basis: "Gift", AnchorLocal: "2027-01-31 10:00:00"),
        ];
        foreach (var seed in invalid)
        {
            var term = await SeedTermAsync(host, account, seed with { Basis = null });
            var error = await Assert.ThrowsAsync<PostgresException>(() => InsertFactsAsync(connection, account, term, seed));
            Assert.Equal("23514", error.SqlState);
        }

        // A zero price is an explicit price, never unlimited or missing.
        var free = await SeedTermAsync(host, account, new TermSeed(Jan31, Feb28, AnchorLocal: "2027-01-31 10:00:00", PricePaise: 0));
        Assert.Equal(0L, await LicensingPostgreSqlFixture.ReadScalarAsync(host.LicensingOwner,
            $"SELECT price_paise FROM public.subscription_term_facts WHERE term_id = '{free:D}'"));
        var orphan = await SeedTermAsync(host, account, new TermSeed(Jan31, Feb28, Basis: null));
        var mismatched = await Assert.ThrowsAsync<PostgresException>(() => InsertFactsAsync(connection, Guid.NewGuid(), orphan,
            new TermSeed(Jan31, Feb28, AnchorLocal: "2027-01-31 10:00:00")));
        Assert.Equal("23503", mismatched.SqlState);
    }

    private static async Task AssertRefusedAsync(NpgsqlConnection runtime, string sql, string state, Guid account)
    {
        await using var transaction = await runtime.BeginTransactionAsync();
        await using (var scope = new NpgsqlCommand("SELECT set_config('app.billing_account_id', $1, true)", runtime, transaction))
        {
            scope.Parameters.AddWithValue(account.ToString("D"));
            await scope.ExecuteNonQueryAsync();
        }
        await using var command = new NpgsqlCommand(sql, runtime, transaction);
        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.True(state == error.SqlState, $"{sql}: {error.SqlState}");
        await transaction.RollbackAsync();
    }

    private static async Task<int> RuntimeChangeAsync(NpgsqlConnection runtime, string sql, Guid account)
    {
        await using var transaction = await runtime.BeginTransactionAsync();
        await using (var scope = new NpgsqlCommand("SELECT set_config('app.billing_account_id', $1, true)", runtime, transaction))
        {
            scope.Parameters.AddWithValue(account.ToString("D"));
            await scope.ExecuteNonQueryAsync();
        }
        await using var command = new NpgsqlCommand(sql, runtime, transaction);
        var changed = await command.ExecuteNonQueryAsync();
        await transaction.RollbackAsync();
        return changed;
    }

    private static async Task<string> TermEvidenceAsync(Hosts host) => (string)(await LicensingPostgreSqlFixture.ReadScalarAsync(
        host.LicensingOwner, """
        SELECT json_build_array((SELECT json_agg(t ORDER BY term_id) FROM public.subscription_coverage_terms t),
            (SELECT json_agg(f ORDER BY term_id) FROM public.subscription_term_facts f),
            (SELECT json_agg(a ORDER BY request_id) FROM public.shop_affiliations a))::text
        """))!;
}
