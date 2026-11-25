using System.Text.Json;
using Intelibill.Application.Common.Licensing;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Licensing;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Intelibill.Integration.Tests;

public sealed partial class P0501LicensingTests
{
    private static readonly string[] RestrictedOwnerTables =
    [
        "shop_license_states", "license_grant_evidence", "entitlement_messages", "account_license_restrictions",
        "shop_closure_consents", "invoice_leases", "offline_license_grants", "offline_license_permits",
        "offline_license_verification_keys", "offline_submissions", "reconciliation_issues",
        "applied_shop_capacity", "capacity_reservation_receipts", "applied_shop_reductions", "coverage_slot_consent_events",
        "coverage_slot_activations", "coverage_replacement_consent_events", "coverage_replacement_activations",
        "reduction_reservation_receipts", "reduction_manifest_shops", "reduction_manifest_pages",
        "license_history_review_observations", "\"__EFMigrationsHistory\"",
    ];

    /// <summary>
    /// Under a real non-superuser NOBYPASSRLS schema owner (RLS hides every row): empty P05 migrations
    /// roll back cleanly over populated entitlement rows so P04 guards see their own contract, while
    /// retained P05 evidence (verification keys and historical submission references) blocks downgrade with 55000.
    /// </summary>
    [Fact]
    public async Task Downgrade_allows_empty_p05_with_retained_entitlements_and_refuses_p05_evidence_as_restricted_owner()
    {
        var dbName = $"p0501_rls_{Guid.NewGuid():N}";
        var role = $"{dbName}_owner";
        var adminConn = Migrator;
        var dbConn = new NpgsqlConnectionStringBuilder(adminConn) { Database = dbName }.ConnectionString;
        var restrictedConn = new NpgsqlConnectionStringBuilder(dbConn) { Username = role, Password = role, Pooling = false }.ConnectionString;
        await ExecuteSqlAsync(adminConn, $"CREATE ROLE {role} LOGIN NOSUPERUSER NOBYPASSRLS PASSWORD '{role}'");
        await ExecuteSqlAsync(adminConn, $"CREATE DATABASE \"{dbName}\"");
        try
        {
            await using var superDb = Open(dbConn);
            await superDb.Database.MigrateAsync(Ct);
            await RestrictedRuntimeRole.EnsureAsync(dbConn);
            await RestrictedRuntimeRole.GrantAsync(dbConn);

            var shopId = await SeedEntitledShopAsync(superDb, dbConn);
            await AssignOwnerAsync(dbConn, role);
            await AssertRestrictedRoleCannotBypassRlsAsync(restrictedConn);
            await AssertEmptyP05DowngradeRetainsEntitlementsAsync(superDb, restrictedConn, shopId);
            await AssertPopulatedP05EvidenceRefusesDowngradeAsync(superDb, dbConn, restrictedConn, role, shopId);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await ExecuteSqlAsync(adminConn,
                $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{dbName}' AND pid <> pg_backend_pid()");
            await ExecuteSqlAsync(adminConn, $"DROP DATABASE IF EXISTS \"{dbName}\"");
            await ExecuteSqlAsync(adminConn, $"DROP ROLE IF EXISTS {role}");
        }
    }

    private static async Task AssertEmptyP05DowngradeRetainsEntitlementsAsync(ApplicationDbContext superDb,
        string restrictedConn, Guid shopId)
    {
        await using (var restricted = Open(restrictedConn))
            await restricted.GetService<IMigrator>().MigrateAsync(Predecessor, Ct);
        Assert.Equal(0, await ScalarAsync<int>(superDb, $"""
            SELECT count(*)::int AS "Value" FROM pg_class WHERE relname = ANY({EvidenceTables}) AND relkind = 'r'
            """));
        Assert.Equal(1, await ScalarAsync<int>(superDb,
            $"SELECT count(*)::int AS \"Value\" FROM public.shop_license_states WHERE shop_id = {shopId}"));
        await AssertForcedRlsAsync(superDb, "shop_license_states", "license_grant_evidence",
            "entitlement_messages", "offline_submissions", "reconciliation_issues");
    }

    private async Task AssertPopulatedP05EvidenceRefusesDowngradeAsync(ApplicationDbContext superDb,
        string dbConn, string restrictedConn, string role, Guid shopId)
    {
        await superDb.Database.MigrateAsync(Ct);
        await AssignOwnerAsync(dbConn, role);
        await superDb.Database.ExecuteSqlAsync($"""
            INSERT INTO public.offline_license_verification_keys (id, shop_id, key_id, public_key, fingerprint, first_used_at)
            VALUES ({Guid.NewGuid()}, {shopId}, {_keyA.Id}, {_keyA.PublicKey}, {_keyA.Fingerprint}, {At("2026-11-20T13:00:00Z")})
            """, Ct);
        await AssertRestrictedRefusalAsync(restrictedConn, Predecessor, "Retained offline licence evidence prevents downgrade");
        await superDb.Database.MigrateAsync(Ct);
        await AssertEvidenceSchemaAsync(superDb);
        Assert.Equal(1, await ScalarAsync<int>(superDb,
            $"SELECT count(*)::int AS \"Value\" FROM public.offline_license_verification_keys WHERE shop_id = {shopId}"));

        await AssignOwnerAsync(dbConn, role);
        var env = OfflineSubmission.Record(shopId, "dev-rls", "sale-rls", "A/2627/000001", new string('a', 64),
            """{"licenceEvidence":{"version":1}}""", At("2026-11-20T13:00:00Z"), At("2026-11-20T13:01:00Z"),
            OfflineSubmissionSource.Sync, Guid.NewGuid(), null, null, null, null);
        superDb.OfflineSubmissions.Add(env);
        await superDb.SaveChangesAsync(Ct);
        await AssertRestrictedRefusalAsync(restrictedConn, EvidenceMigration,
            "Cannot remove historical licence references after receipt");
        await AssertForcedRlsAsync(superDb, "offline_submissions");
    }

    private async Task<Guid> SeedEntitledShopAsync(ApplicationDbContext superDb, string dbConn)
    {
        var shop = Shop.Create("P0501 RLS Shop", "1 Main", "Bengaluru", "Karnataka", "560001", null, null, null);
        var owner = User.CreateWithEmail($"p0501-rls-{Guid.NewGuid():N}@fixture.test", "fixture", "Fixture", "Owner");
        var membership = ShopMembership.Create(shop.Id, owner.Id, ShopRole.Owner, isDefault: true);
        superDb.AddRange(shop, owner, membership);
        await superDb.SaveChangesAsync(Ct);
        var now = At("2026-11-20T12:00:00Z");
        _clock.Now = now;
        var binding = new Binding(shop.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        foreach (var kind in new[] { "Granted", "Accepted" })
            await superDb.Database.ExecuteSqlAsync($"""
                INSERT INTO public.shop_affiliation_consent_events (id, shop_id, account_id, request_id,
                    actor_id, membership_id, authority_version, operation_key, kind, recorded_at, term_id,
                    shop_name, owner_name, request_expires_at)
                VALUES ({Guid.NewGuid()}, {shop.Id}, {binding.Account}, {binding.Request},
                    {owner.Id}, {membership.Id}, {membership.AuthorityVersion}, {Guid.NewGuid()}, {kind}, {now},
                    {binding.Term}, {"P0501 RLS Shop"}, {"Fixture Owner"}, {now.AddDays(15)})
                """, Ct);
        var snapshot = new EntitlementSnapshotV1(1, EntitlementSnapshotsV1.PolicyVersion, Guid.NewGuid(),
            binding.Account, binding.Shop, binding.Request,
            new(1, now, "Unrestricted", null),
            new(1, now, "Active", binding.Term, "Paid", now.AddDays(-1), now.AddDays(30), now.AddDays(30), 2, 3));
        var body = JsonSerializer.SerializeToUtf8Bytes(snapshot, EntitlementSnapshotsV1.Json);
        var proof = new VerifiedServiceAssertion(Guid.NewGuid(), "test-key", "test-public-key", now, now.AddMinutes(1),
            SignedServiceAssertion.Digest(body), "fixture-verified-assertion");
        await using var runtimeDb = Open(RestrictedRuntimeRole.ConnectionStringFor(dbConn));
        var fence = new ShopLicenceFence(runtimeDb, new LicenceActionPolicy(), _clock);
        var applied = await new EntitlementProjectionStore(runtimeDb, _clock, fence).ApplyAsync(snapshot, body, proof, Ct);
        Assert.False(applied.IsError, applied.IsError ? applied.FirstError.Code : null);
        var ack = JsonSerializer.Deserialize<EntitlementAcknowledgementV1>(applied.Value, EntitlementSnapshotsV1.Json);
        Assert.True(ack?.Outcome == "Applied", ack?.Disposition);
        return shop.Id;
    }

    private static Task AssignOwnerAsync(string dbConn, string role)
    {
        var alters = string.Concat(RestrictedOwnerTables.Select(t => $"ALTER TABLE public.{t} OWNER TO {role};"));
        return ExecuteSqlAsync(dbConn, $"""
            GRANT CREATE ON SCHEMA public TO {role};
            {alters}
            ALTER FUNCTION public.replacement_outgoing_released(uuid,timestamptz) OWNER TO {role};
            ALTER FUNCTION public.capacity_fence_shops(uuid,uuid) OWNER TO {role};
            ALTER FUNCTION public.reduction_fence_shops(uuid,uuid) OWNER TO {role};
            ALTER FUNCTION public.reduction_observe_shops(uuid,uuid[]) OWNER TO {role};
            """);
    }

    private static async Task AssertRestrictedRoleCannotBypassRlsAsync(string restrictedConn)
    {
        await using var db = Open(restrictedConn);
        Assert.Equal("off", await ScalarAsync<string>(db, $"SELECT current_setting('is_superuser') AS \"Value\""));
        Assert.False(await ScalarAsync<bool>(db, $"SELECT rolbypassrls AS \"Value\" FROM pg_roles WHERE rolname = current_user"));
        Assert.Equal(0, await ScalarAsync<int>(db, $"SELECT count(*)::int AS \"Value\" FROM public.shop_license_states"));
    }

    private static async Task AssertRestrictedRefusalAsync(string restrictedConn, string target, string expectedMessage)
    {
        await using var restricted = Open(restrictedConn);
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => restricted.GetService<IMigrator>().MigrateAsync(target, Ct));
        var pg = Assert.IsType<PostgresException>(failure.GetBaseException());
        Assert.Equal("55000", pg.SqlState);
        Assert.Equal(expectedMessage, pg.MessageText);
    }

    private static async Task AssertForcedRlsAsync(ApplicationDbContext db, params string[] tables)
    {
        Assert.Equal(tables.Length, await ScalarAsync<int>(db, $"""
            SELECT count(*)::int AS "Value" FROM pg_class
            WHERE relname = ANY({tables}) AND relnamespace = 'public'::regnamespace
                AND relrowsecurity AND relforcerowsecurity
            """));
    }

    private static async Task ExecuteSqlAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }
}
