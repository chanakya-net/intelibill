using System.Net.Http.Json;
using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Intelibill.Integration.Tests;

public sealed partial class P0501LicensingTests
{
    private const string Predecessor = "20261121120000_ProjectClosureReopenings";
    private const string EvidenceMigration = "20261122184354_AddOfflineLicenseEvidence";
    private static readonly string[] EvidenceTables =
        ["offline_license_grants", "offline_license_permits", "offline_license_verification_keys"];

    private const string PermitColumns = "id, shop_id, grant_id, lease_id, snapshot_id, user_id, session_id, device_id, " +
        "till_session_id, batch_count, catalog_version, current_account_revision, current_shop_revision, completed_at, " +
        "not_before, valid_until, limiting_reason, key_id, signed_permit";

    /// <summary>
    /// A populated predecessor database upgraded in place: legacy leases keep every value, the new
    /// tables isolate shops for the restricted runtime role, refuse every mutation, hold their
    /// constraints and refuse to be dropped once they retain evidence.
    /// </summary>
    [Fact]
    public async Task Upgrade_preserves_legacy_leases_and_isolates_immutable_evidence()
    {
        var verified = At("2026-11-20T12:00:00Z");
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        using var otherClient = CreateClient(host);
        // No entitlement yet: retained entitlement evidence would, rightly, refuse the downgrade below.
        var a = await OpenShopAsync(client, verified, verified.AddDays(30), licensed: false);
        var b = await OpenShopAsync(otherClient, verified, verified.AddDays(30), licensed: false);
        foreach (var device in new[] { "legacy-recorded", "legacy-unrecorded" })
            await ReadSuccessAsync(await client.PostAsJsonAsync("/api/sales/invoice-leases/reserve", new { deviceId = device, blockSize = 25 }, Ct));

        await using var owner = Open(Migrator);
        var migrator = owner.GetService<IMigrator>();
        await migrator.MigrateAsync(Predecessor, Ct);
        Assert.Equal(0, await ScalarAsync<int>(owner, $"""
            SELECT count(*)::int AS "Value" FROM pg_class WHERE relname = ANY({EvidenceTables}) AND relkind = 'r'
            """));
        // A lease from before reservers were recorded.
        await owner.Database.ExecuteSqlAsync($"""
            UPDATE invoice_leases SET reserved_by_user_id = NULL, reserved_by_session_id = NULL WHERE device_id = 'legacy-unrecorded'
            """, Ct);
        var legacy = await LeaseDigestAsync(owner);

        await migrator.MigrateAsync(EvidenceMigration, Ct);
        await RestrictedRuntimeRole.GrantAsync(Migrator);
        NpgsqlConnection.ClearAllPools();
        Assert.Equal(legacy, await LeaseDigestAsync(owner));
        Assert.Equal(1, await ScalarAsync<int>(owner, $"""
            SELECT count(*)::int AS "Value" FROM invoice_leases WHERE reserved_by_user_id IS NULL AND reserved_by_session_id IS NULL
            """));
        Assert.Equal(1, await ScalarAsync<int>(owner, $"""
            SELECT count(*)::int AS "Value" FROM "__EFMigrationsHistory" WHERE migration_id = {EvidenceMigration}
            """));
        await AssertEvidenceSchemaAsync(owner);

        a = await LicenseAsync(a, verified, verified.AddDays(30));
        b = await LicenseAsync(b, verified, verified.AddDays(30));
        _clock.Now = verified.AddHours(1);
        foreach (var actor in new[] { a, b })
        {
            var (lease, captured) = await ReserveAsync(host, actor);
            Issued(await IssueAsync(host, actor, Issued(captured).GrantId, await SnapshotAsync(host, actor, lease)));
        }
        Assert.Equal(legacy, await LeaseDigestAsync(owner));
        Assert.Equal(new Evidence(1, 1, 1), await EvidenceAsync(a.Shop));

        await AssertIsolationAsync(a, b);
        await AssertAppendOnlyAsync(owner);
        await AssertConstraintsAsync(owner, a, b);

        // Evidence in place: the downgrade refuses, and everything it touched is as before.
        var refusal = await Assert.ThrowsAnyAsync<Exception>(() => migrator.MigrateAsync(Predecessor, Ct));
        Assert.Equal("55000", Assert.IsType<PostgresException>(refusal.GetBaseException()).SqlState);
        await AssertEvidenceSchemaAsync(owner);
        Assert.Equal(new Evidence(1, 1, 1), await EvidenceAsync(b.Shop));
        Assert.Equal(legacy, await LeaseDigestAsync(owner));
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

    /// <summary>Every column of every lease that existed before the upgrade.</summary>
    private static Task<string> LeaseDigestAsync(DbContext owner) => ScalarAsync<string>(owner, $"""
        SELECT md5(string_agg(l::text, '|' ORDER BY l.id)) AS "Value" FROM invoice_leases l WHERE device_id LIKE 'legacy-%'
        """);

    private static Task<T> ScalarAsync<T>(DbContext owner, FormattableString sql) => owner.Database.SqlQuery<T>(sql).SingleAsync(Ct);
}
