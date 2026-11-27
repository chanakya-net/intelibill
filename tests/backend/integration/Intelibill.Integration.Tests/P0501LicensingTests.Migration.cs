using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Intelibill.Integration.Tests;

public sealed partial class P0501LicensingTests
{
    private static readonly string[] EvidenceTables =
        ["offline_license_grants", "offline_license_permits", "offline_license_verification_keys"];

    private const string PermitColumns = "id, shop_id, grant_id, lease_id, snapshot_id, user_id, session_id, device_id, " +
        "till_session_id, batch_count, catalog_version, current_account_revision, current_shop_revision, completed_at, " +
        "not_before, valid_until, limiting_reason, key_id, signed_permit";

    /// <summary>
    /// The offline licence evidence tables isolate shops for the restricted runtime role, refuse every
    /// mutation and hold their constraints, and a stored grant keeps the fixed bounds it was verified with.
    /// </summary>
    [Theory]
    [InlineData("2026-11-20T12:00:00Z")]
    [InlineData("2024-11-20T12:00:00Z")]
    public async Task Evidence_tables_isolate_shops_and_retain_immutable_evidence(string scenarioVerifiedAt)
    {
        var verified = At(scenarioVerifiedAt);
        var setupStartedAt = TimeProvider.System.GetUtcNow();
        if (scenarioVerifiedAt == "2024-11-20T12:00:00Z")
            Assert.True(setupStartedAt > verified.AddDays(400));
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        using var otherClient = CreateClient(host);
        var a = await OpenShopAsync(client, verified, verified.AddDays(30));
        var b = await OpenShopAsync(otherClient, verified, verified.AddDays(30));

        await using var owner = Open(Migrator);
        await AssertEvidenceSchemaAsync(owner);

        _clock.Now = verified.AddHours(1);
        foreach (var actor in new[] { a, b })
        {
            var (lease, captured) = await ReserveAsync(host, actor);
            var grant = Issued(captured).GrantId;
            var permit = Issued(await IssueAsync(host, actor, grant, await SnapshotAsync(host, actor, grant, lease)));
            Assert.Equal((_clock.Now, verified.AddHours(48)), (permit.NotBefore, permit.ValidUntil));
        }
        var storedGrant = await owner.Set<OfflineLicenseGrant>().AsNoTracking().SingleAsync(x => x.ShopId == a.Shop, Ct);
        Assert.Equal((verified, verified.AddDays(400)), (storedGrant.AuthorityVerifiedAt, storedGrant.AuthDeadline));
        Assert.Equal(new Evidence(1, 1, 1), await EvidenceAsync(a.Shop));

        await AssertIsolationAsync(a, b);
        await AssertAppendOnlyAsync(owner);
        await AssertConstraintsAsync(owner, a, b);
        _output.WriteLine("Evidence scenario {0}: HTTP setup {1:O} vs cutoff {2:O}; RLS and fixed bounds preserved",
            scenarioVerifiedAt, setupStartedAt, verified.AddDays(400));
    }

    private static async Task AssertEvidenceSchemaAsync(DbContext owner)
    {
        Assert.Equal(3, await ScalarAsync<int>(owner, $"""
            SELECT count(*)::int AS "Value" FROM pg_class c JOIN pg_policy p ON p.polrelid = c.oid
            WHERE c.relname = ANY({EvidenceTables}) AND c.relrowsecurity AND c.relforcerowsecurity
                AND p.polname = c.relname || '_shop_isolation' AND p.polqual IS NOT NULL AND p.polwithcheck IS NOT NULL
            """));
        Assert.Equal(6, await ScalarAsync<int>(owner, $"""
            SELECT count(*)::int AS "Value" FROM pg_trigger t JOIN pg_class c ON c.oid = t.tgrelid
            WHERE c.relname = ANY({EvidenceTables}) AND t.tgname IN ('retain_rows', 'retain_table') AND t.tgenabled = 'O'
            """));
        Assert.Equal(4, await ScalarAsync<int>(owner, $"""
            SELECT count(*)::int AS "Value" FROM pg_constraint WHERE conname IN ('valid_offline_license_grant',
                'valid_offline_license_permit', 'valid_offline_license_verification_key', 'ak_invoice_leases_id_shop_id')
            """));
    }

    /// <summary>Separate restricted-role connections: each shop sees and writes only its own evidence.</summary>
    private async Task AssertIsolationAsync(Actor a, Actor b)
    {
        foreach (var table in EvidenceTables)
        {
            Assert.Equal((1, 1), await AsShopAsync(a.Shop, $"SELECT count(*)::int, count(*) FILTER (WHERE shop_id = '{a.Shop:D}')::int FROM {table}"));
            Assert.Equal((1, 0), await AsShopAsync(b.Shop, $"SELECT count(*)::int, count(*) FILTER (WHERE shop_id = '{a.Shop:D}')::int FROM {table}"));
            Assert.Equal((0, 0), await AsShopAsync(null, $"SELECT count(*)::int, 0 FROM {table}"));
        }

        // Shop A cannot plant a key for shop B, nor rewrite or remove anything, its own included.
        var foreign = await Assert.ThrowsAsync<PostgresException>(() => AsShopAsync(a.Shop, $"""
            INSERT INTO offline_license_verification_keys (id, shop_id, key_id, public_key, fingerprint, first_used_at)
            SELECT gen_random_uuid(), '{b.Shop:D}', 'planted', public_key, fingerprint, first_used_at
            FROM offline_license_verification_keys RETURNING 1, 1
            """));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, foreign.SqlState);
        foreach (var statement in new[] { "UPDATE offline_license_permits SET valid_until = valid_until + interval '1 day' RETURNING 1, 1",
            "DELETE FROM offline_license_grants RETURNING 1, 1" })
            Assert.Contains((await Assert.ThrowsAsync<PostgresException>(() => AsShopAsync(a.Shop, statement))).SqlState,
                new[] { "55000", PostgresErrorCodes.InsufficientPrivilege });
        Assert.Equal((0, 0), await AsShopAsync(a.Shop,
            $"UPDATE offline_license_permits SET valid_until = valid_until WHERE shop_id = '{b.Shop:D}' RETURNING 1, 1"));
    }

    private static async Task AssertAppendOnlyAsync(DbContext owner)
    {
        foreach (var table in EvidenceTables)
            foreach (var statement in new[] { $"UPDATE {table} SET key_id = key_id", $"DELETE FROM {table}", $"TRUNCATE {table} CASCADE" })
                Assert.Equal("55000", (await Assert.ThrowsAsync<PostgresException>(() => owner.Database.ExecuteSqlRawAsync(statement, Ct))).SqlState);
    }

    private static async Task AssertConstraintsAsync(DbContext owner, Actor a, Actor b)
    {
        var copy = $"INSERT INTO offline_license_permits ({PermitColumns}) SELECT gen_random_uuid(), ";
        var rest = "user_id, session_id, device_id, till_session_id, batch_count, catalog_version, current_account_revision, " +
            "current_shop_revision, completed_at, ";
        var tail = $"limiting_reason, key_id, signed_permit FROM offline_license_permits WHERE shop_id = '{a.Shop:D}'";
        var cases = new (string Sql, string State)[]
        {
            // An empty window, a second completion of one snapshot, and a permit pointing at another shop's grant.
            ($"{copy}shop_id, grant_id, lease_id, gen_random_uuid(), {rest}valid_until, valid_until, {tail}", PostgresErrorCodes.CheckViolation),
            ($"{copy}shop_id, grant_id, lease_id, snapshot_id, {rest}not_before, valid_until, {tail}", PostgresErrorCodes.UniqueViolation),
            ($"{copy}'{b.Shop:D}', grant_id, lease_id, gen_random_uuid(), {rest}not_before, valid_until, {tail}", PostgresErrorCodes.ForeignKeyViolation),
            // A second original grant for one lease.
            ($"""
                INSERT INTO offline_license_grants SELECT (json_populate_record(g, json_build_object('id', gen_random_uuid()))).*
                FROM offline_license_grants g WHERE shop_id = '{a.Shop:D}'
                """, PostgresErrorCodes.UniqueViolation),
            // A key identifier rebound to other material, and a fingerprint that is not the key's.
            ($"""
                INSERT INTO offline_license_verification_keys (id, shop_id, key_id, public_key, fingerprint, first_used_at)
                SELECT gen_random_uuid(), k.shop_id, k.key_id, o.public_key, o.fingerprint, k.first_used_at
                FROM offline_license_verification_keys k, offline_license_verification_keys o
                WHERE k.shop_id = '{a.Shop:D}' AND o.shop_id = '{b.Shop:D}'
                """, PostgresErrorCodes.UniqueViolation),
            ($"""
                INSERT INTO offline_license_verification_keys (id, shop_id, key_id, public_key, fingerprint, first_used_at)
                SELECT gen_random_uuid(), shop_id, 'other', public_key, repeat('0', 64), first_used_at
                FROM offline_license_verification_keys WHERE shop_id = '{a.Shop:D}'
                """, PostgresErrorCodes.CheckViolation),
        };
        foreach (var (sql, state) in cases)
            Assert.True(state == (await Assert.ThrowsAsync<PostgresException>(() => owner.Database.ExecuteSqlRawAsync(sql, Ct))).SqlState, sql);
    }

    private async Task<(int, int)> AsShopAsync(Guid? shop, string sql)
    {
        await using var connection = new NpgsqlConnection(RestrictedRuntimeRole.ConnectionStringFor(Migrator));
        await connection.OpenAsync(Ct);
        await using (var scope = new NpgsqlCommand("SELECT set_config('app.active_shop_id', @shop, false)", connection))
        {
            scope.Parameters.AddWithValue("shop", shop?.ToString("D") ?? string.Empty);
            await scope.ExecuteNonQueryAsync(Ct);
        }
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        return await reader.ReadAsync(Ct) ? (reader.GetInt32(0), reader.GetInt32(1)) : (0, 0);
    }

    private static Task<T> ScalarAsync<T>(DbContext owner, FormattableString sql) => owner.Database.SqlQuery<T>(sql).SingleAsync(Ct);
}
