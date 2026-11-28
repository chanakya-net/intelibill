using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0702Tests
{
    [Fact]
    public async Task ScopedRuntimeCannotChangeAnActiveLeaseDirectly()
    {
        await using var smtp = new P0702SmtpPeer();
        var clock = new P0201Clock { Now = P0210Fixture.Start };
        await using var fixture = await FixedLeaseFixtureAsync(smtp.Port, clock);
        var contact = await ApproveStandaloneAsync(fixture);
        var delivery = DeliverAsync(fixture);
        await smtp.Received;
        try
        {
            var account = (Guid)(await fixture.SourceScalarAsync(
                $"SELECT account_id FROM notification_email_deliveries WHERE proof_contact_id = '{contact:D}'"))!;
            var runtime = database.Login(fixture.Hosts.LicensingOwner.Database!, LicensingPostgreSqlFixture.RuntimeRole);
            await using var connection = new NpgsqlConnection(runtime.ConnectionString);
            await connection.OpenAsync();
            await using var tx = await connection.BeginTransactionAsync();
            await using var scope = new NpgsqlCommand("""
                SELECT set_config('app.billing_account_id',$1,true), set_config('app.notification_planner','on',true),
                    set_config('app.notice_email_worker','on',true)
                """, connection, tx);
            scope.Parameters.AddWithValue(account.ToString("D"));
            await scope.ExecuteNonQueryAsync();
            await using var update = new NpgsqlCommand("""
                UPDATE public.notification_email_deliveries SET lease_token = gen_random_uuid() WHERE proof_contact_id = $1
                """, connection, tx);
            update.Parameters.AddWithValue(contact);
            Assert.Equal(0, await update.ExecuteNonQueryAsync());
            await using var finish = new NpgsqlCommand("""
                SELECT public.advance_notice_email(account_id,id,gen_random_uuid(),'Sending','SmtpAccepted',last_attempt_at)
                FROM public.notification_email_deliveries WHERE proof_contact_id = $1
                """, connection, tx);
            finish.Parameters.AddWithValue(contact);
            Assert.Equal(false, await finish.ExecuteScalarAsync());
            await AssertOtherScopeRefusedAsync(connection, tx, contact);
            await tx.RollbackAsync();
        }
        finally
        {
            smtp.Finish(true);
            await delivery;
        }
    }

    private static async Task AssertOtherScopeRefusedAsync(NpgsqlConnection connection, NpgsqlTransaction tx, Guid contact)
    {
        await using var wrongAccount = new NpgsqlCommand("""
            SELECT public.advance_notice_email(gen_random_uuid(),id,lease_token,'Sending','SmtpAccepted',last_attempt_at)
            FROM notification_email_deliveries WHERE proof_contact_id = $1
            """, connection, tx);
        wrongAccount.Parameters.AddWithValue(contact);
        Assert.Equal(false, await wrongAccount.ExecuteScalarAsync());

        await using var actor = new NpgsqlCommand(
            "SELECT set_config('app.provider_admin_id',gen_random_uuid()::text,true)", connection, tx);
        await actor.ExecuteNonQueryAsync();
        await using var mixedScope = new NpgsqlCommand("""
            SELECT public.advance_notice_email(account_id,id,lease_token,'Sending','SmtpAccepted',last_attempt_at)
            FROM notification_email_deliveries WHERE proof_contact_id = $1
            """, connection, tx);
        mixedScope.Parameters.AddWithValue(contact);
        Assert.Equal(false, await mixedScope.ExecuteScalarAsync());

        await using var change = new NpgsqlCommand(
            "SELECT set_config('app.billing_account_id',gen_random_uuid()::text,true)", connection, tx);
        await change.ExecuteNonQueryAsync();
        await using var hidden = new NpgsqlCommand("SELECT count(*) FROM notification_email_deliveries", connection, tx);
        Assert.Equal(0L, await hidden.ExecuteScalarAsync());
    }

    [Fact]
    public async Task AbandonedPreSubmissionLeaseReclaimsAtEqualityAndFencesFirstToken()
    {
        var clock = new P0201Clock { Now = P0210Fixture.Start };
        await using var fixture = await FixedLeaseFixtureAsync(mailpit.SmtpPort, clock);
        var contact = await ApproveStandaloneAsync(fixture);
        var runtime = database.Login(fixture.Hosts.LicensingOwner.Database!, LicensingPostgreSqlFixture.RuntimeRole);
        await using var connection = new NpgsqlConnection(runtime.ConnectionString);
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        await using var settings = new NpgsqlCommand("""
            SELECT set_config('app.notification_planner','on',true),set_config('app.notice_email_worker','on',true)
            """, connection, tx);
        await settings.ExecuteNonQueryAsync();
        await using var claim = new NpgsqlCommand("SELECT token FROM next_notice_email('infinity',$1)", connection, tx);
        claim.Parameters.AddWithValue(clock.Now);
        var first = Assert.IsType<Guid>(await claim.ExecuteScalarAsync());
        await tx.CommitAsync();
        clock.Now = clock.Now.AddSeconds(60).AddTicks(-10);
        Assert.False(await DeliverAsync(fixture));
        clock.Now = clock.Now.AddTicks(10);
        Assert.True(await DeliverAsync(fixture));
        Assert.Equal("SmtpAccepted", await ProofStateAsync(fixture, contact));
        Assert.Equal(2, await fixture.SourceScalarAsync(
            $"SELECT attempts FROM notification_email_deliveries WHERE proof_contact_id = '{contact:D}'"));
        await using var late = await connection.BeginTransactionAsync();
        await using var scope = new NpgsqlCommand("""
            SELECT set_config('app.notification_planner','on',true),set_config('app.notice_email_worker','on',true),
                set_config('app.billing_account_id',$1,true)
            """, connection, late);
        var account = (Guid)(await fixture.SourceScalarAsync(
            $"SELECT account_id FROM notification_email_deliveries WHERE proof_contact_id = '{contact:D}'"))!;
        scope.Parameters.AddWithValue(account.ToString("D"));
        await scope.ExecuteNonQueryAsync();
        await using var fenced = new NpgsqlCommand("""
            SELECT advance_notice_email(account_id,id,$1,'Leased','Sending',$2) FROM notification_email_deliveries
            """, connection, late);
        fenced.Parameters.AddWithValue(first);
        fenced.Parameters.AddWithValue(clock.Now);
        Assert.Equal(false, await fenced.ExecuteScalarAsync());
    }

    [Fact]
    public async Task DowngradeRefusesToDiscardNotificationHistory()
    {
        await using var fixture = await P0213Fixture.StartAsync(database, licensingExtras: mailpit.BillingSettings());
        var contact = await ApproveStandaloneAsync(fixture);
        Assert.True(await DeliverAsync(fixture));
        var migrator = database.Login(fixture.Hosts.LicensingOwner.Database!, LicensingPostgreSqlFixture.MigratorRole);
        var options = new DbContextOptionsBuilder<LicensingDbContext>().UseNpgsql(migrator.ConnectionString).UseSnakeCaseNamingConvention();
        await using var context = new LicensingDbContext(options.Options);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.Database.GetService<IMigrator>().MigrateAsync("20261128040001_PlanLicenseNotices"));
        Assert.Equal("55000", Assert.IsType<PostgresException>(error.GetBaseException()).SqlState);
        Assert.Equal("SmtpAccepted", await ProofStateAsync(fixture, contact));
        Assert.Equal(3L, await fixture.SourceScalarAsync("SELECT count(*) FROM notification_email_events"));
    }

    [Fact]
    public async Task PooledCloneHasOnlyTheScopedDeliveryEntrypoints()
    {
        var (runtime, _) = await database.CreateOwnerDatabaseAsync("p0702_grants_" + Guid.NewGuid().ToString("N"));
        await AssertRuntimePrivilegesAsync(runtime);
    }

    internal static async Task AssertRuntimePrivilegesAsync(NpgsqlConnectionStringBuilder runtime)
    {
        const string sql = """
            SELECT count(*) FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
            WHERE n.nspname = 'public' AND p.proname IN ('next_notice_email','advance_notice_email')
                AND p.prosecdef AND has_function_privilege(current_user,p.oid,'EXECUTE')
                AND NOT EXISTS (SELECT FROM aclexplode(p.proacl) acl WHERE acl.grantee = 0 AND acl.privilege_type = 'EXECUTE')
            """;
        Assert.Equal(2L, await LicensingPostgreSqlFixture.ReadScalarAsync(runtime, sql));
        Assert.Equal(false, await LicensingPostgreSqlFixture.ReadScalarAsync(runtime, """
            SELECT has_function_privilege(current_user,'public.record_notice_email_event()','EXECUTE')
            """));
        Assert.Equal(2L, await LicensingPostgreSqlFixture.ReadScalarAsync(runtime, """
            SELECT count(*) FROM pg_class WHERE relnamespace = 'public'::regnamespace AND relrowsecurity AND relforcerowsecurity
                AND relname IN ('notification_email_events','notification_email_deliveries')
            """));
        Assert.Equal(0L, await LicensingPostgreSqlFixture.ReadScalarAsync(runtime, "SELECT count(*) FROM next_notice_email(now(),now())"));
    }
}
