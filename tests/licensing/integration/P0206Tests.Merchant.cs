using System.Net;
using System.Net.Http.Headers;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0206Tests
{
    private const string Canary = "p0206-draft-canary";

    [Fact]
    public async Task RuntimeRoleWithoutManagerContext_SeesNoDrafts()
    {
        await using var fixture = await P0206Fixture.StartAsync(database);
        var (manager, _) = await fixture.ManagerAsync();
        using (manager)
            await SaveAsync(manager, Guid.NewGuid(), MultiShop(0, ("name", Canary)), HttpStatusCode.Created);
        var managerId = (Guid)(await fixture.ScalarAsync(
            $"SELECT id FROM admin_identities WHERE lower(email) = lower('{P0114Tests.Invitee}')"))!;
        var ownerId = (Guid)(await fixture.ScalarAsync("SELECT owner_id FROM initial_admin_bootstrap"))!;

        const string visible = """
            SELECT (SELECT count(*) FROM plans) + (SELECT count(*) FROM plan_versions)
                + (SELECT count(*) FROM catalogue_draft_changes)
            """;
        Assert.Equal(0L, await RuntimeAsync(fixture, null, visible));
        Assert.Equal(0L, await RuntimeAsync(fixture, ownerId, visible));
        Assert.Equal(0L, await RuntimeAsync(fixture, null, visible, billing: true));
        Assert.Equal(3L, await RuntimeAsync(fixture, managerId, visible));

        var insert = $"""
            INSERT INTO plans(id, kind, created_at, created_by_admin_id) VALUES (gen_random_uuid(), 1, now(), '{ownerId}')
            """;
        foreach (var context in new Guid?[] { null, ownerId, managerId })
        {
            var refused = await Assert.ThrowsAsync<PostgresException>(() => RuntimeAsync(fixture, context, insert));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, refused.SqlState);
        }
    }

    [Fact]
    public async Task MerchantSurfaces_NeverExposeDrafts()
    {
        using var fixture = await P0201Fixture.CreateAsync(database);
        await LicensingPostgreSqlFixture.ExecuteAsync(database.Bootstrap(fixture.Owner.Database), $"""
            SET session_replication_role = replica;
            INSERT INTO plans(id, kind, created_at, created_by_admin_id)
            VALUES ('00000000-0000-0000-0000-0000000000b1', 2, now(), gen_random_uuid());
            INSERT INTO plan_versions(id, plan_id, version_number, status, revision, name, currency, monthly_price_paise,
                created_at, created_by_admin_id, updated_at, updated_by_admin_id)
            VALUES (gen_random_uuid(), '00000000-0000-0000-0000-0000000000b1', 1, 1, 1, '{Canary}', 'INR', 149900,
                now(), gen_random_uuid(), now(), gen_random_uuid());
            """);
        await using var licensing = P0202Fixture.LicensingApi(fixture);
        using var client = licensing.CreateClient();
        var actor = Guid.NewGuid();
        using (var created = await P0202Fixture.CreateAsync(client, fixture, actor, Guid.NewGuid(), "Acme"))
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using (var listed = await P0202Fixture.ListAsync(client, fixture, actor))
        {
            Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
            var body = await listed.Content.ReadAsStringAsync();
            Assert.DoesNotContain(Canary, body, StringComparison.Ordinal);
            Assert.DoesNotContain("Paise", body, StringComparison.Ordinal);
        }

        var assertion = P0202Fixture.Sign(fixture, actor, P0202Fixture.List, []);
        using var customerPlans = await P0202Fixture.SendAsync(client, assertion, path: "/internal/licensing/v1/customer/plans");
        Assert.Equal(HttpStatusCode.NotFound, customerPlans.StatusCode);
        using var delegated = new HttpRequestMessage(HttpMethod.Get, new Uri(Plans, UriKind.Relative));
        delegated.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
            P0202Fixture.Sign(fixture, actor, P0202Fixture.List, []).Token);
        using var admin = await client.SendAsync(delegated);
        Assert.Equal(HttpStatusCode.Unauthorized, admin.StatusCode);
        Assert.DoesNotContain(Canary, await admin.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private static async Task<object?> RuntimeAsync(P0206Fixture fixture, Guid? catalogueAdmin, string sql,
        bool billing = false)
    {
        await using var connection = new NpgsqlConnection(fixture.Runtime.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var scope = new NpgsqlCommand("""
            SELECT set_config('app.catalogue_admin_id', $1, true), set_config('app.billing_account_id', $2, true),
                set_config('app.billing_actor_id', $2, true)
            """, connection, transaction);
        scope.Parameters.AddWithValue(catalogueAdmin?.ToString("D") ?? string.Empty);
        scope.Parameters.AddWithValue(billing ? Guid.NewGuid().ToString("D") : string.Empty);
        await scope.ExecuteNonQueryAsync();
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        return await command.ExecuteScalarAsync();
    }
}
