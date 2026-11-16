using System.Net.Http.Headers;
using System.Security.Cryptography;
using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.LicensingPostgreSqlFixture;

namespace Intelibill.Licensing.Integration.Tests;

internal sealed class P0201Fixture(LicensingPostgreSqlFixture database, string name) : IDisposable
{
    internal const string KeyId = "local-v1";
    internal const string Action = "billing.account.read";
    internal const string RequestHeader = "X-Billing-Request-Id";
    internal RSA Key { get; } = RSA.Create(2048);
    internal P0201Clock Clock { get; } = new();
    internal NpgsqlConnectionStringBuilder Owner => database.Login(name, MigratorRole);
    internal NpgsqlConnectionStringBuilder Runtime => database.Login(name, RuntimeRole);
    internal NpgsqlConnectionStringBuilder MerchantRuntime => database.Login(MerchantDatabase, MerchantRole);

    internal static async Task<P0201Fixture> CreateAsync(LicensingPostgreSqlFixture database)
    {
        var name = "p0201_" + Guid.NewGuid().ToString("N");
        var fixture = new P0201Fixture(database, name);
        try
        {
            await ExecuteAsync(database.Bootstrap(), $"CREATE DATABASE {name} OWNER licensing_migrator");
            await ExecuteAsync(database.Bootstrap(), $"""
                REVOKE ALL ON DATABASE {name} FROM PUBLIC;
                GRANT CONNECT ON DATABASE {name} TO licensing_runtime;
                """);
            await using var context = fixture.OpenMigrationContext();
            await context.Database.MigrateAsync();
            await fixture.PrepareMembershipFixtureAsync();
            return fixture;
        }
        catch
        {
            fixture.Dispose();
            throw;
        }
    }

    internal LicensingDbContext OpenMigrationContext() => new(new DbContextOptionsBuilder<LicensingDbContext>()
        .UseNpgsql(Owner.ConnectionString).UseSnakeCaseNamingConvention().Options);

    private Task PrepareMembershipFixtureAsync() => ExecuteAsync(Owner, """
        REVOKE ALL ON SCHEMA public FROM PUBLIC;
        GRANT USAGE ON SCHEMA public TO licensing_runtime;
        ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE, TRUNCATE ON TABLES TO licensing_runtime;
        GRANT SELECT, INSERT, UPDATE, DELETE, TRUNCATE ON ALL TABLES IN SCHEMA public TO licensing_runtime;
        CREATE SCHEMA billing_fixture;
        GRANT USAGE ON SCHEMA billing_fixture TO licensing_runtime;

        CREATE TABLE billing_fixture.memberships (
            account_id uuid NOT NULL, actor_id uuid NOT NULL, action text NOT NULL, active boolean NOT NULL,
            PRIMARY KEY (account_id, actor_id, action)
        );
        ALTER TABLE billing_fixture.memberships ENABLE ROW LEVEL SECURITY;
        ALTER TABLE billing_fixture.memberships FORCE ROW LEVEL SECURITY;
        CREATE POLICY test_owner ON billing_fixture.memberships TO licensing_migrator USING (true) WITH CHECK (true);
        CREATE POLICY test_runtime ON billing_fixture.memberships TO licensing_runtime USING (
            account_id::text = current_setting('app.billing_account_id', true)
            AND actor_id::text = current_setting('app.billing_actor_id', true)
        ) WITH CHECK (false);
        GRANT SELECT, UPDATE (active) ON billing_fixture.memberships TO licensing_runtime;
        """);

    internal Task GrantAsync(Guid account, Guid actor) => ExecuteAsync(Owner, $"""
        INSERT INTO billing_fixture.memberships VALUES ('{account:D}', '{actor:D}', '{Action}', true)
        """);

    internal Task RevokeAsync(Guid account, Guid actor) => ExecuteAsync(Owner, $"""
        UPDATE billing_fixture.memberships SET active = false WHERE account_id = '{account:D}' AND actor_id = '{actor:D}'
        """);

    internal Dictionary<string, string?> LicensingSettings() => new()
    {
        [$"MerchantBillingVerification:PublicKeys:{KeyId}"] = Key.ExportSubjectPublicKeyInfoPem(),
        ["LicensingDatabase:MaxPoolSize"] = "1",
    };

    internal LicensingApiFactory LicensingApi(bool membership = true, Dictionary<string, string?>? settings = null,
        Action<IServiceCollection>? configure = null) => new(Runtime, clock: Clock, configure: services =>
    {
        // membership: false keeps the #846 "no adapter" case: an unavailable authority must fail closed.
        // P0202Fixture covers the production membership adapter.
        if (membership)
            services.AddScoped<IBillingAccountDelegationAuthority, FixtureBillingAuthority>();
        else
            services.AddScoped<IBillingAccountDelegationAuthority, UnavailableBillingAuthority>();
        configure?.Invoke(services);
    }, settings: settings ?? LicensingSettings());

    internal MerchantStartupProbeFactory MerchantApi(string origin = "https://licensing.test",
        Func<HttpMessageHandler>? handler = null, Action<IServiceCollection>? configure = null,
        string environment = "Development", IReadOnlyDictionary<string, string?>? settings = null) => new(MerchantRuntime, environment, new Dictionary<string, string?>
        {
            ["MerchantBillingDelegation:LicensingOrigin"] = origin,
            ["MerchantBillingDelegation:LocalKeyId"] = KeyId,
            ["MerchantBillingDelegation:LocalPrivateKeyPem"] = Key.ExportPkcs8PrivateKeyPem(),
            ["Email:Enabled"] = environment == "Production" ? "true" : "false",
            ["Email:Host"] = "127.0.0.1",
            ["Email:Port"] = "1",
            ["Email:Username"] = "fixture",
            ["Email:Password"] = "fixture",
            ["Email:FromEmail"] = "fixture@example.test",
            ["Email:FromName"] = "Billing fixture",
            ["Identity:OutboxKey"] = Convert.ToBase64String(new byte[32]),
        }.Concat(settings ?? new Dictionary<string, string?>()).ToDictionary(), services =>
        {
            if (handler is not null)
                services.AddHttpClient("MerchantBillingDelegation").ConfigurePrimaryHttpMessageHandler(handler);
            configure?.Invoke(services);
        });

    internal static Uri InternalPath(Guid account) =>
        new($"/internal/licensing/v1/customer/accounts/{account:D}/access", UriKind.Relative);

    internal static async Task<HttpResponseMessage> SendAssertionAsync(HttpClient client, Guid account,
        Guid requestId, string token, Uri? uri = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri ?? InternalPath(account));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(RequestHeader, requestId.ToString("D"));
        return await client.SendAsync(request);
    }

    public void Dispose() => Key.Dispose();

    private sealed class UnavailableBillingAuthority : IBillingAccountDelegationAuthority
    {
        public Task<ErrorOr<Success>> AuthorizeAsync(Guid accountId, Guid actorId, string action,
            CancellationToken cancellationToken) =>
            Task.FromResult<ErrorOr<Success>>(BillingDelegationErrors.MembershipUnavailable);
    }

    private sealed class FixtureBillingAuthority(
        LicensingDbContext db, P0201Tests.ReplicaGate? gate = null) : IBillingAccountDelegationAuthority
    {
        public async Task<ErrorOr<Success>> AuthorizeAsync(
            Guid accountId, Guid actorId, string action, CancellationToken cancellationToken)
        {
            var members = await db.Database.SqlQuery<bool>($"""
                SELECT active AS "Value"
                FROM billing_fixture.memberships
                WHERE account_id = {accountId} AND actor_id = {actorId} AND action = {action}
                FOR SHARE
                """).ToListAsync(cancellationToken);
            if (members is not [true])
                return BillingDelegationErrors.Denied;

            if (gate is not null)
                await gate.ArriveAsync(cancellationToken);
            return Result.Success;
        }
    }
}

internal sealed class P0201Clock : TimeProvider
{
    internal DateTimeOffset Now { get; set; } = new(2031, 5, 1, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}
