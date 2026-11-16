using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0202Tests
{
    private const string AccountCount = "SELECT count(*) FROM billing_accounts";
    private const string MemberCount = "SELECT count(*) FROM billing_account_members";
    private const string UseCount = "SELECT count(*) FROM merchant_billing_delegation_uses";

    [Fact]
    public async Task InternalCreate_YieldsOneAccountWithExactlyOneOwner()
    {
        using var fixture = await P0201Fixture.CreateAsync(database);
        await using var licensing = P0202Fixture.LicensingApi(fixture);
        using var client = licensing.CreateClient();
        var actor = Guid.NewGuid();

        var expired = P0202Fixture.Sign(fixture, actor, P0202Fixture.Create, P0202Fixture.CreateBody(Guid.NewGuid(), "Late"));
        var tampered = P0202Fixture.Sign(fixture, actor, P0202Fixture.Create, P0202Fixture.CreateBody(Guid.NewGuid(), "Signed"));
        using (var changed = await P0202Fixture.SendAsync(client, tampered,
            P0202Fixture.CreateBody(Guid.NewGuid(), "Unsigned")))
            await AssertProblemAsync(changed, HttpStatusCode.Unauthorized, "Billing.DelegationInvalid");
        Assert.Equal(0L, await P0202Fixture.CountAsync(database, fixture, AccountCount));
        Assert.Equal(0L, await P0202Fixture.CountAsync(database, fixture, UseCount));

        var proof = P0202Fixture.Sign(fixture, actor, P0202Fixture.Create, P0202Fixture.CreateBody(Guid.NewGuid(), "Acme"));
        using var created = await P0202Fixture.SendAsync(client, proof);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.True(created.Headers.CacheControl?.NoStore);
        var account = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = account.GetProperty("accountId").GetGuid();
        Assert.Equal("Acme", account.GetProperty("name").GetString());
        Assert.Equal("Owner", account.GetProperty("role").GetString());
        Assert.Equal(fixture.Clock.Now, account.GetProperty("createdAt").GetDateTimeOffset());
        Assert.Equal([new P0202Fixture.OwnerRow(id, actor, 1, 1)], await P0202Fixture.ReadAccountRowsAsync(database, fixture));
        Assert.Equal(1L, await P0202Fixture.CountAsync(database, fixture,
            $"SELECT count(*) FROM merchant_billing_delegation_uses WHERE account_id IS NULL AND actor_id = '{actor:D}' AND action = 'billing.account.create'"));

        using (var replayed = await P0202Fixture.SendAsync(client, proof))
            await AssertProblemAsync(replayed, HttpStatusCode.Unauthorized, "Billing.DelegationReplayed");

        var second = await Assert.ThrowsAsync<PostgresException>(() => LicensingPostgreSqlFixture.ExecuteAsync(
            database.Bootstrap(fixture.Owner.Database), $"""
            INSERT INTO billing_account_members (id, account_id, merchant_user_id, role, status, revision, created_at)
            VALUES ('{Guid.NewGuid():D}', '{id:D}', '{Guid.NewGuid():D}', 1, 1, 1, now())
            """));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, second.SqlState);

        fixture.Clock.Now = fixture.Clock.Now.AddSeconds(60);
        using (var late = await P0202Fixture.SendAsync(client, expired))
            await AssertProblemAsync(late, HttpStatusCode.Unauthorized, "Billing.DelegationInvalid");
        Assert.Equal(1L, await P0202Fixture.CountAsync(database, fixture, AccountCount));
        Assert.Equal(1L, await P0202Fixture.CountAsync(database, fixture, MemberCount));
    }

    [Fact]
    public async Task Retry_SameKey_ReturnsTheSameAccount()
    {
        using var fixture = await P0201Fixture.CreateAsync(database);
        await using var licensing = P0202Fixture.LicensingApi(fixture);
        using var client = licensing.CreateClient();
        var actor = Guid.NewGuid();
        var key = Guid.NewGuid();

        using var first = await P0202Fixture.CreateAsync(client, fixture, actor, key, "Acme");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var original = await first.Content.ReadFromJsonAsync<JsonElement>();
        fixture.Clock.Now = fixture.Clock.Now.AddMinutes(5);

        using var retry = await P0202Fixture.CreateAsync(client, fixture, actor, key, "Acme");
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(original.GetRawText(), (await retry.Content.ReadFromJsonAsync<JsonElement>()).GetRawText());

        using (var changed = await P0202Fixture.CreateAsync(client, fixture, actor, key, "Other name"))
            await AssertProblemAsync(changed, HttpStatusCode.Conflict, "Billing.OperationPayloadMismatch");

        var other = Guid.NewGuid();
        using var separate = await P0202Fixture.CreateAsync(client, fixture, other, key, "Acme");
        Assert.Equal(HttpStatusCode.Created, separate.StatusCode);
        var separateId = (await separate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accountId").GetGuid();
        Assert.NotEqual(original.GetProperty("accountId").GetGuid(), separateId);

        var rows = await P0202Fixture.ReadAccountRowsAsync(database, fixture);
        Assert.Equal(2, rows.Length);
        Assert.Single(rows, row => row.MerchantUserId == actor && row.Role == 1 && row.Status == 1);
        Assert.Single(rows, row => row.MerchantUserId == other && row.AccountId == separateId);

        using (var blank = await P0202Fixture.CreateAsync(client, fixture, actor, Guid.NewGuid(), "   "))
            await AssertProblemAsync(blank, HttpStatusCode.BadRequest, "Billing.AccountNameInvalid");
        using (var noKey = await P0202Fixture.CreateAsync(client, fixture, actor, Guid.Empty, "Acme"))
            await AssertProblemAsync(noKey, HttpStatusCode.BadRequest, "Billing.OperationKeyInvalid");
        // Only the two creations and the retry record a use: the 409 rolled back with its use, and
        // validation refuses before the transaction.
        Assert.Equal(3L, await P0202Fixture.CountAsync(database, fixture, UseCount));
    }

    [Fact]
    public async Task TwoReplicas_ConcurrentRetries_CreateOneAccountWithOneOwner()
    {
        using var fixture = await P0201Fixture.CreateAsync(database);
        await using var first = P0202Fixture.LicensingApi(fixture);
        await using var second = P0202Fixture.LicensingApi(fixture);
        using var a = first.CreateClient();
        using var b = second.CreateClient();
        var actor = Guid.NewGuid();
        var key = Guid.NewGuid();
        var superuser = database.Bootstrap(fixture.Owner.Database);

        await using var holder = new NpgsqlConnection(superuser.ConnectionString);
        await holder.OpenAsync();
        await using var hold = await holder.BeginTransactionAsync();
        await using (var lockAccounts = new NpgsqlCommand(
            "LOCK TABLE billing_accounts IN SHARE ROW EXCLUSIVE MODE", holder, hold))
            await lockAccounts.ExecuteNonQueryAsync();

        var requests = new[]
        {
            P0202Fixture.CreateAsync(a, fixture, actor, key, "Acme"),
            P0202Fixture.CreateAsync(b, fixture, actor, key, "Acme"),
        };
        await WaitForLockWaitersAsync(superuser, 2);
        await hold.CommitAsync();

        var responses = await Task.WhenAll(requests);
        using var one = responses[0];
        using var two = responses[1];
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Created],
            responses.Select(response => response.StatusCode).Order().ToArray());
        var bodies = await Task.WhenAll(responses.Select(response => response.Content.ReadAsStringAsync()));
        Assert.Equal(bodies[0], bodies[1]);
        Assert.Single(await P0202Fixture.ReadAccountRowsAsync(database, fixture));
    }

    [Fact]
    public async Task OwnerInsertFailure_LeavesNoAccount()
    {
        using var fixture = await P0201Fixture.CreateAsync(database);
        await using var licensing = P0202Fixture.LicensingApi(fixture);
        using var client = licensing.CreateClient();
        var actor = Guid.NewGuid();
        var key = Guid.NewGuid();
        var owner = database.Bootstrap(fixture.Owner.Database);

        await LicensingPostgreSqlFixture.ExecuteAsync(owner, "REVOKE INSERT ON billing_account_members FROM licensing_runtime");
        using (var failed = await P0202Fixture.CreateAsync(client, fixture, actor, key, "Acme"))
            await AssertProblemAsync(failed, HttpStatusCode.ServiceUnavailable, "Billing.DelegationUnavailable");
        Assert.Equal(0L, await P0202Fixture.CountAsync(database, fixture, AccountCount));
        Assert.Equal(0L, await P0202Fixture.CountAsync(database, fixture, UseCount));

        await LicensingPostgreSqlFixture.ExecuteAsync(owner, "GRANT INSERT ON billing_account_members TO licensing_runtime");
        using var recovered = await P0202Fixture.CreateAsync(client, fixture, actor, key, "Acme");
        Assert.Equal(HttpStatusCode.Created, recovered.StatusCode);
        Assert.Single(await P0202Fixture.ReadAccountRowsAsync(database, fixture));
    }

    private static async Task WaitForLockWaitersAsync(NpgsqlConnectionStringBuilder superuser, long count)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (true)
        {
            var waiting = (long)(await LicensingPostgreSqlFixture.ReadScalarAsync(superuser, """
                SELECT count(*) FROM pg_locks
                WHERE NOT granted AND relation = 'public.billing_accounts'::regclass
                """))!;
            if (waiting >= count)
                return;
            await Task.Delay(50, deadline.Token);
        }
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, problem.GetProperty("code").GetString());
    }
}
