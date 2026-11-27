using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0202Tests
{
    [Fact]
    public async Task ListAndAccess_UseLiveMembership()
    {
        using var fixture = await P0201Fixture.CreateAsync(database);
        await using var licensing = P0202Fixture.LicensingApi(fixture);
        using var client = licensing.CreateClient();
        var owner = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        var mine = await CreateAccountAsync(client, fixture, owner, "First");
        var second = await CreateAccountAsync(client, fixture, owner, "Second");
        var foreign = await CreateAccountAsync(client, fixture, Guid.NewGuid(), "Foreign");

        Assert.Equal(new[] { mine, second }.Order(), (await ListIdsAsync(client, fixture, owner)).Order());
        Assert.Empty(await ListIdsAsync(client, fixture, stranger));

        using (var access = await P0202Fixture.AccessAsync(client, fixture, mine, owner))
        {
            Assert.Equal(HttpStatusCode.OK, access.StatusCode);
            var body = await access.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(mine, body.GetProperty("accountId").GetGuid());
        }
        var usesBefore = await P0202Fixture.CountAsync(database, fixture, UseCount);
        foreach (var (account, actor) in new[] { (mine, stranger), (Guid.NewGuid(), owner), (foreign, owner) })
        {
            using var denied = await P0202Fixture.AccessAsync(client, fixture, account, actor);
            await AssertProblemAsync(denied, HttpStatusCode.Forbidden, "Billing.AccountAccessDenied");
        }
        Assert.Equal(usesBefore, await P0202Fixture.CountAsync(database, fixture, UseCount));
    }

    [Fact]
    public async Task MissingOrForeignAccountContext_FailsClosed()
    {
        using var fixture = await P0201Fixture.CreateAsync(database);
        await using var licensing = P0202Fixture.LicensingApi(fixture);
        using var client = licensing.CreateClient();
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var account = await CreateAccountAsync(client, fixture, owner, "Mine");
        var foreign = await CreateAccountAsync(client, fixture, other, "Theirs");

        // Scope must match the action: a read needs its account, creation and listing never carry one.
        var readWithoutAccount = P0202Fixture.Sign(fixture, owner, "billing.account.read", []);
        using (var read = await P0202Fixture.SendAsync(client, readWithoutAccount))
            await AssertProblemAsync(read, HttpStatusCode.Unauthorized, "Billing.DelegationInvalid");
        var listWithAccount = P0202Fixture.Sign(fixture, owner, P0202Fixture.List, [], claims => claims["account_id"] = account);
        using (var list = await P0202Fixture.SendAsync(client, listWithAccount))
            await AssertProblemAsync(list, HttpStatusCode.Unauthorized, "Billing.DelegationInvalid");
        var createWithAccount = P0202Fixture.Sign(fixture, owner, P0202Fixture.Create,
            P0202Fixture.CreateBody(Guid.NewGuid(), "Again"), claims => claims["account_id"] = account);
        using (var create = await P0202Fixture.SendAsync(client, createWithAccount))
            await AssertProblemAsync(create, HttpStatusCode.Unauthorized, "Billing.DelegationInvalid");
        var listOnCreateRoute = P0202Fixture.Sign(fixture, owner, P0202Fixture.List, []);
        using (var mismatch = await P0202Fixture.SendAsync(client, listOnCreateRoute with { Method = HttpMethod.Post },
            P0202Fixture.CreateBody(Guid.NewGuid(), "x")))
            await AssertProblemAsync(mismatch, HttpStatusCode.Unauthorized, "Billing.DelegationInvalid");

        // Runtime role, straight SQL: no context sees nothing; an actor alone sees only its own memberships.
        Assert.Equal(0L, await P0202Fixture.RuntimeScalarAsync(fixture, null, null, AccountCount));
        Assert.Equal(0L, await P0202Fixture.RuntimeScalarAsync(fixture, null, null, MemberCount));
        Assert.Equal(0L, await P0202Fixture.RuntimeScalarAsync(fixture, null, owner, AccountCount));
        Assert.Equal(1L, await P0202Fixture.RuntimeScalarAsync(fixture, null, owner, MemberCount));
        Assert.Equal(account, await P0202Fixture.RuntimeScalarAsync(fixture, null, owner,
            "SELECT account_id FROM billing_account_members"));
        Assert.Equal(1L, await P0202Fixture.RuntimeScalarAsync(fixture, account, owner, AccountCount));
        Assert.Equal(0L, await P0202Fixture.RuntimeScalarAsync(fixture, foreign, owner, AccountCount));
        Assert.Equal(0L, await P0202Fixture.RuntimeScalarAsync(fixture, foreign, owner, MemberCount));
        Assert.Equal(0L, await P0202Fixture.RuntimeScalarAsync(fixture, account, null, AccountCount));
        Assert.Equal(0L, await P0202Fixture.RuntimeScalarAsync(fixture, account, other, AccountCount));

        await AssertRuntimeRefusedAsync(fixture, foreign, owner, PostgresErrorCodes.InsufficientPrivilege, $"""
            INSERT INTO billing_account_members (id, account_id, merchant_user_id, role, status, revision, created_at)
            VALUES ('{Guid.NewGuid():D}', '{account:D}', '{owner:D}', 2, 1, 1, now())
            """);
        await AssertRuntimeRefusedAsync(fixture, foreign, owner, PostgresErrorCodes.InsufficientPrivilege, $"""
            INSERT INTO billing_accounts (id, name, created_by_merchant_user_id, creation_key, creation_request_hash, created_at)
            VALUES ('{account:D}', 'x', '{owner:D}', '{Guid.NewGuid():D}', sha256(''), now())
            """);
        await AssertRuntimeRefusedAsync(fixture, account, owner, PostgresErrorCodes.InsufficientPrivilege,
            "UPDATE billing_account_members SET role = 2");
        foreach (var statement in new[] { "DELETE FROM billing_accounts", "TRUNCATE billing_account_members",
            "DELETE FROM billing_account_members", "UPDATE billing_accounts SET name = 'renamed'" })
            await AssertRuntimeRefusedAsync(fixture, account, owner, "55000", statement);
        Assert.Equal(2, (await P0202Fixture.ReadAccountRowsAsync(database, fixture)).Length);
    }

    [Fact]
    public async Task PooledConnection_DoesNotLeakAccountContext()
    {
        using var fixture = await P0201Fixture.CreateAsync(database);
        await using var licensing = P0202Fixture.LicensingApi(fixture);
        using var client = licensing.CreateClient();
        var dataSource = licensing.Services.GetRequiredService<NpgsqlDataSource>();
        var pid = await ReadPoolAsync(dataSource, "SELECT pg_backend_pid()");
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var key = Guid.NewGuid();

        var owned = new Dictionary<Guid, List<Guid>> { [a] = [], [b] = [] };
        foreach (var actor in new[] { a, b, a })
        {
            owned[actor].Add(await CreateAccountAsync(client, fixture, actor, "Pool account"));
            await AssertPoolClearAsync(dataSource);
            Assert.Equal(owned[actor].Order(), (await ListIdsAsync(client, fixture, actor)).Order());
            await AssertPoolClearAsync(dataSource);
        }
        using (var created = await P0202Fixture.CreateAsync(client, fixture, a, key, "Fixed"))
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using (var conflict = await P0202Fixture.CreateAsync(client, fixture, a, key, "Changed"))
            await AssertProblemAsync(conflict, HttpStatusCode.Conflict, "Billing.OperationPayloadMismatch");
        await AssertPoolClearAsync(dataSource);
        using (var invalid = await P0202Fixture.CreateAsync(client, fixture, b, Guid.NewGuid(), ""))
            await AssertProblemAsync(invalid, HttpStatusCode.BadRequest, "Billing.AccountNameInvalid");
        using (var anonymous = await client.GetAsync(new Uri(P0202Fixture.AccountsPath, UriKind.Relative)))
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        await AssertPoolClearAsync(dataSource);
        Assert.Equal(pid, await ReadPoolAsync(dataSource, "SELECT pg_backend_pid()"));
    }

    private static async Task AssertPoolClearAsync(NpgsqlDataSource dataSource)
    {
        Assert.Equal(string.Empty, await ReadPoolAsync(dataSource,
            "SELECT coalesce(current_setting('app.billing_account_id', true), '') || coalesce(current_setting('app.billing_actor_id', true), '')"));
        Assert.Equal(0L, await ReadPoolAsync(dataSource, AccountCount));
        Assert.Equal(0L, await ReadPoolAsync(dataSource, MemberCount));
    }

    private static async Task AssertRuntimeRefusedAsync(P0201Fixture fixture, Guid account, Guid actor, string state, string sql)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            P0202Fixture.RuntimeScalarAsync(fixture, account, actor, sql));
        Assert.Equal(state, error.SqlState);
    }

    private static async Task<object?> ReadPoolAsync(NpgsqlDataSource dataSource, string sql)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    private static async Task<Guid> CreateAccountAsync(HttpClient client, P0201Fixture fixture, Guid actor, string name)
    {
        using var response = await P0202Fixture.CreateAsync(client, fixture, actor, Guid.NewGuid(), name);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accountId").GetGuid();
    }

    private static async Task<Guid[]> ListIdsAsync(HttpClient client, P0201Fixture fixture, Guid actor)
    {
        using var response = await P0202Fixture.ListAsync(client, fixture, actor);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var accounts = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accounts");
        return [.. accounts.EnumerateArray().Select(account => account.GetProperty("accountId").GetGuid())];
    }
}
