using System.Text.Json;
using Intelibill.Application.Common.Licensing;
using Intelibill.Infrastructure.Licensing;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using static Intelibill.Application.Common.Licensing.LicenceOutcome;

namespace Intelibill.Integration.Tests;

public sealed partial class P0401LicensingTests
{
    public sealed partial class PostgreSql
    {
        private const string TrialPredecessor = "20261116160000_AddTrialActivationConsents";
        private const string DeliveryPredecessor = "20261116200000_AddShopLicensingState";
        private const string FenceMigration = "20261118080000_AddLicenceEnforcementFences";
        private static readonly string[] RetainedTables =
            ["license_grant_evidence", "shop_license_states", "entitlement_messages"];

        [Fact]
        public async Task Downgrade_with_an_applied_projection_refuses_and_keeps_the_fence_working()
        {
            await using var database = await IsolateDatabaseAsync();
            var binding = await SeedAsync();
            await using (var delivery = CreateDb())
                await ApplyAsync(delivery, binding, 1, "Unrestricted");
            var before = await RetainedDigestAsync();

            await AssertDowngradeRefusedAsync(database.OwnerConnection);

            await AssertFenceMigrationKeptAsync(before);
            var lease = await EnterIsolatedAsync(binding.ShopId);
            Assert.Equal(Allowed, lease.Decision.Outcome);
            Assert.Equal(1L, lease.ShopRevision);
        }

        [Fact]
        public async Task Downgrade_refuses_a_retained_merchant_capacity_receipt_as_restricted_owner()
        {
            await using var database = await IsolateDatabaseAsync(SeedCapacityReceiptAsync);
            await using var restricted = Open(database.OwnerConnection);
            Assert.Equal(0, await restricted.Database.SqlQuery<int>(
                $"SELECT count(*)::int AS \"Value\" FROM public.capacity_reservation_receipts").SingleAsync(Ct));
            var error = await Record.ExceptionAsync(() => restricted.GetService<IMigrator>().MigrateAsync(TrialPredecessor, Ct));
            var refused = Assert.IsType<PostgresException>(error?.GetBaseException());
            Assert.Equal("55000", refused.SqlState);
            Assert.Equal("Retained merchant capacity decisions prevent downgrade", refused.MessageText);
        }

        private async Task SeedCapacityReceiptAsync()
        {
            var binding = await SeedAsync();
            await using var db = CreateDb();
            var now = _clock.GetUtcNow();
            var digest = new string('a', 64);
            var command = new CapacityReservationV1(1, Guid.NewGuid(), CapacityReservationV1.Reserve, Guid.NewGuid(),
                binding.AccountId, binding.TermId, binding.ShopId, digest, 1, digest, 2, 3, 3, 4,
                now.AddDays(-1), now.AddDays(1), now.AddMinutes(5), 0, 1, null);
            var body = JsonSerializer.SerializeToUtf8Bytes(command, EntitlementSnapshotsV1.Json);
            var proof = new VerifiedServiceAssertion(Guid.NewGuid(), "test-key", "test-public-key", now, now.AddMinutes(1),
                SignedServiceAssertion.Digest(body), "fixture-verified-assertion");
            var fence = new ShopLicenceFence(db, new LicenceActionPolicy(), _clock);
            var store = new MerchantCapacityReservationStore(db, new EntitlementProjectionStore(db, _clock, fence), _clock, _clock);
            var received = await store.ReceiveAsync(command, body, proof, Ct);
            Assert.False(received.IsError, received.IsError ? received.FirstError.Code : null);
        }

        [Theory]
        [InlineData("license_grant_evidence")]
        [InlineData("shop_license_states")]
        [InlineData("entitlement_messages")]
        public async Task Downgrade_refuses_when_one_table_retains_rows_row_security_hides(string retained)
        {
            await using var database = await IsolateDatabaseAsync();
            var binding = await SeedAsync();
            await using (var delivery = CreateDb())
                await ApplyAsync(delivery, binding, 1, "Unrestricted");
            await KeepOnlyAsync(retained);
            var before = await RetainedDigestAsync();

            await AssertDowngradeRefusedAsync(database.OwnerConnection);

            await AssertFenceMigrationKeptAsync(before);
            await EnterIsolatedAsync(binding.ShopId);
        }

        [Fact]
        public async Task Downgrade_waits_for_an_inflight_projection_then_refuses_without_deadlock()
        {
            await using var database = await IsolateDatabaseAsync();
            var binding = await SeedAsync();
            await using var delivery = CreateDb();
            await ApplyAsync(delivery, binding, 1, "Unrestricted");
            await delivery.Database.OpenConnectionAsync(Ct);
            var deliveryPid = await PidAsync(delivery);

            // SHARE admits the projection's reads and stops it at its first write, after it has read the shop row.
            await using var holder = CreateDb(owner: true);
            await using var paused = await holder.Database.BeginTransactionAsync(Ct);
            await holder.Database.ExecuteSqlRawAsync("LOCK TABLE public.entitlement_snapshot_inbox IN SHARE MODE", Ct);
            var applying = ApplyAsync(delivery, binding, 2, "Unrestricted");
            await WaitForBlockAsync(applying, deliveryPid, await PidAsync(holder), "Projection must reach its first write.");

            await using var migration = CreateDb(owner: true);
            await migration.Database.OpenConnectionAsync(Ct);
            var migrationPid = await PidAsync(migration);
            var downgrade = migration.GetService<IMigrator>().MigrateAsync(TrialPredecessor, Ct);
            await WaitForBlockAsync(downgrade, migrationPid, deliveryPid, "Downgrade must wait for the projection.", seconds: 15);
            await paused.RollbackAsync(Ct);

            await applying.WaitAsync(TimeSpan.FromSeconds(10));
            var refused = await Assert.ThrowsAnyAsync<Exception>(() => downgrade.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal("55000", Assert.IsType<PostgresException>(refused.GetBaseException()).SqlState);
            await AssertFenceMigrationKeptAsync(await RetainedDigestAsync());
            Assert.Equal(2L, (await EnterIsolatedAsync(binding.ShopId)).ShopRevision);
        }

        [Fact]
        public async Task Empty_downgrade_restores_row_security_and_reupgrades_to_a_working_fence()
        {
            await using var database = await IsolateDatabaseAsync();
            await using var owner = CreateDb(owner: true);
            var migrator = owner.GetService<IMigrator>();

            await migrator.MigrateAsync(DeliveryPredecessor, Ct);

            Assert.DoesNotContain(FenceMigration, await owner.Database.GetAppliedMigrationsAsync(Ct));
            Assert.False(await FenceExistsAsync(owner));
            await AssertRetainedTablesForceRowSecurityAsync(owner);

            await migrator.MigrateAsync(TrialPredecessor, Ct);
            await owner.Database.MigrateAsync(Ct);
            await RestrictedRuntimeRole.GrantAsync(MigratorConnection);

            Assert.Empty(await owner.Database.GetPendingMigrationsAsync(Ct));
            var binding = await SeedAsync();
            await using (var delivery = CreateDb())
                await ApplyAsync(delivery, binding, 1, "Unrestricted");
            Assert.Equal(Allowed, (await EnterIsolatedAsync(binding.ShopId)).Decision.Outcome);
        }

        private static async Task AssertDowngradeRefusedAsync(string connection)
        {
            await using var owner = Open(connection);
            var refused = await Assert.ThrowsAnyAsync<Exception>(() => owner.GetService<IMigrator>().MigrateAsync(TrialPredecessor, Ct));
            Assert.Equal("55000", Assert.IsType<PostgresException>(refused.GetBaseException()).SqlState);
        }

        private async Task AssertFenceMigrationKeptAsync(string retainedBefore)
        {
            await using var owner = CreateDb(owner: true);
            Assert.Contains(FenceMigration, await owner.Database.GetAppliedMigrationsAsync(Ct));
            // The empty closure/reopening migrations can roll back before the retained fence refuses downgrade.
            // Restore the latest schema before the current projection model reads the retained data.
            await owner.Database.MigrateAsync(Ct);
            await RestrictedRuntimeRole.GrantAsync(MigratorConnection);
            Assert.Empty(await owner.Database.GetPendingMigrationsAsync(Ct));
            Assert.True(await FenceExistsAsync(owner), "A refused downgrade must leave the fence function in place.");
            await AssertRetainedTablesForceRowSecurityAsync(owner);
            Assert.Equal(retainedBefore, await RetainedDigestAsync());
        }

        private static async Task AssertRetainedTablesForceRowSecurityAsync(ApplicationDbContext owner)
        {
            Assert.Equal(RetainedTables.Length, await owner.Database.SqlQuery<int>($"""
                SELECT count(*)::int AS "Value" FROM pg_class
                WHERE oid = ANY (ARRAY['public.license_grant_evidence', 'public.shop_license_states',
                    'public.entitlement_messages']::regclass[]) AND relrowsecurity AND relforcerowsecurity
                """).SingleAsync(Ct));
        }

        private async Task<ShopLicenceLease> EnterIsolatedAsync(Guid shop)
        {
            await using var db = CreateDb();
            await using var tx = await BeginScopedAsync(db, shop);
            var entered = await EnterAsync(db, shop);
            Assert.False(entered.IsError, entered.IsError ? entered.FirstError.Code : null);
            return entered.Value;
        }

        private static Task<bool> FenceExistsAsync(ApplicationDbContext db) => db.Database.SqlQuery<bool>($"""
            SELECT to_regprocedure('public.take_merchant_licence_fence(uuid, uuid, boolean)') IS NOT NULL AS "Value"
            """).SingleAsync(Ct);

        private async Task<string> RetainedDigestAsync()
        {
            await using var owner = CreateDb(owner: true);
            return await owner.Database.SqlQuery<string>($"""
                SELECT md5(concat_ws('|',
                    (SELECT string_agg(t::text, ',' ORDER BY t::text) FROM public.license_grant_evidence t),
                    (SELECT string_agg(legacy.head::text, ',' ORDER BY legacy.head::text)
                        FROM public.shop_license_states t CROSS JOIN LATERAL
                            (SELECT to_jsonb(t) - 'reopened_revision' - 'closure_revision' AS head) legacy),
                    (SELECT string_agg(t::text, ',' ORDER BY t::text) FROM public.entitlement_messages t))) AS "Value"
                """).SingleAsync(Ct);
        }

        private async Task KeepOnlyAsync(string retained)
        {
            await using var connection = new NpgsqlConnection(MigratorConnection);
            await connection.OpenAsync(Ct);
            await using var transaction = await connection.BeginTransactionAsync(Ct);
            // Replica mode skips the retention triggers, which is the only way to reach this state in a test.
            var deletes = string.Concat(RetainedTables.Where(table => table != retained)
                .Select(table => $"DELETE FROM public.{table};"));
            await using var command = new NpgsqlCommand(
                $"SET LOCAL session_replication_role = replica;{deletes}", connection, transaction);
            await command.ExecuteNonQueryAsync(Ct);
            await transaction.CommitAsync(Ct);
        }

        private async Task<IsolatedDatabase> IsolateDatabaseAsync(Func<Task>? seed = null)
        {
            var name = "p0401_" + Guid.NewGuid().ToString("N");
            await SharedPostgres.StartAsync();
            await SharedPostgres.CreateFromTemplateAsync(name);
            _isolatedDatabase = name;
            var isolated = new IsolatedDatabase(name);
            if (seed is not null) await seed();
            await isolated.CreateRestrictedOwnerAsync();
            return isolated;
        }

        /// <summary>
        /// A database of its own, because downgrading changes the schema, and a schema owner
        /// without superuser rights, because a superuser never sees row security at all.
        /// </summary>
        private sealed class IsolatedDatabase(string name) : IAsyncDisposable
        {
            private readonly string _role = name + "_owner";

            public string OwnerConnection => new NpgsqlConnectionStringBuilder(SharedPostgres.ConnectionStringFor(name))
            {
                Username = _role,
                Password = _role,
                Pooling = false,
            }.ConnectionString;

            public async Task CreateRestrictedOwnerAsync()
            {
                await ExecuteAsync(SharedPostgres.Container.GetConnectionString(),
                    $"CREATE ROLE {_role} LOGIN NOSUPERUSER NOBYPASSRLS PASSWORD '{_role}'");
                var tables = string.Concat(RetainedTables.Select(table => $"ALTER TABLE public.{table} OWNER TO {_role};"));
                await ExecuteAsync(SharedPostgres.ConnectionStringFor(name), $"""
                    GRANT CREATE ON SCHEMA public TO {_role};
                    {tables}
                    ALTER TABLE public.applied_shop_capacity OWNER TO {_role};
                    ALTER TABLE public.capacity_reservation_receipts OWNER TO {_role};
                    {TransferMigrationOwnership.Sql(_role)}
                    ALTER TABLE public.coverage_slot_consent_events OWNER TO {_role};
                    ALTER TABLE public.coverage_replacement_consent_events OWNER TO {_role};
                    ALTER TABLE public.coverage_replacement_activations OWNER TO {_role};
                    ALTER TABLE public.coverage_exception_consent_events OWNER TO {_role};
                    ALTER TABLE public.merchant_coverage_exception_decisions OWNER TO {_role};
                    ALTER TABLE public.merchant_coverage_exception_cutoffs OWNER TO {_role};
                    ALTER TABLE public.merchant_coverage_exception_activations OWNER TO {_role};
                    ALTER TABLE public.coverage_slot_activations OWNER TO {_role};
                    ALTER TABLE public.applied_shop_reductions OWNER TO {_role};
                    ALTER TABLE public.reduction_reservation_receipts OWNER TO {_role};
                    ALTER TABLE public.reduction_manifest_shops OWNER TO {_role};
                    ALTER TABLE public.reduction_manifest_pages OWNER TO {_role};
                    ALTER TABLE public.account_license_restrictions OWNER TO {_role};
                    ALTER TABLE public.shop_closure_consents OWNER TO {_role};
                    ALTER TABLE public.invoice_leases OWNER TO {_role};
                    ALTER TABLE public.offline_license_grants OWNER TO {_role};
                    ALTER TABLE public.offline_license_permits OWNER TO {_role};
                    ALTER TABLE public.offline_license_verification_keys OWNER TO {_role};
                    ALTER TABLE public.offline_submissions OWNER TO {_role};
                    ALTER TABLE public.reconciliation_issues OWNER TO {_role};
                    ALTER TABLE public.license_history_review_observations OWNER TO {_role};
                    ALTER TABLE public."__EFMigrationsHistory" OWNER TO {_role};
                    ALTER FUNCTION public.replacement_outgoing_released(uuid,timestamptz) OWNER TO {_role};
                    ALTER FUNCTION public.exception_outgoing_released(uuid,bigint,timestamptz) OWNER TO {_role};
                    ALTER FUNCTION public.exception_fence_inspect(uuid,uuid,uuid,bigint,text,uuid,uuid,uuid,uuid,uuid,uuid,int,timestamptz) OWNER TO {_role};
                    ALTER FUNCTION public.guard_offline_permit_exception_cutoff() OWNER TO {_role};
                    ALTER FUNCTION public.capacity_fence_shops(uuid, uuid) OWNER TO {_role};
                    ALTER FUNCTION public.reduction_fence_shops(uuid, uuid) OWNER TO {_role};
                    ALTER FUNCTION public.reduction_observe_shops(uuid, uuid[]) OWNER TO {_role};
                    ALTER FUNCTION public.take_merchant_licence_fence(uuid, uuid, boolean) OWNER TO {_role};
                    """);
            }

            public async ValueTask DisposeAsync()
            {
                await SharedPostgres.DropAsync(name);
                await ExecuteAsync(SharedPostgres.Container.GetConnectionString(), $"DROP ROLE IF EXISTS {_role}");
            }

            private static async Task ExecuteAsync(string connectionString, string sql)
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync(Ct);
                await using var command = new NpgsqlCommand(sql, connection);
                await command.ExecuteNonQueryAsync(Ct);
            }
        }
    }
}
