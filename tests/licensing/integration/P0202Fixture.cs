using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// Classes that host the merchant API and assert on its captured logs. The merchant host binds Serilog's
/// static logger, so overlapping merchant hosts can take each other's sinks; these classes run one at a time.
/// </summary>
[CollectionDefinition(Name)]
public sealed class MerchantBillingHosts
{
    internal const string Name = "Merchant billing hosts";
}

internal static class P0202Fixture
{
    /// <summary>Licensing host with production wiring: the real membership adapter is never replaced.</summary>
    internal static LicensingApiFactory LicensingApi(P0201Fixture fixture, Action<IServiceCollection>? configure = null,
        TimeProvider? clock = null, Dictionary<string, string?>? settings = null) =>
        new(fixture.Runtime, clock: clock ?? fixture.Clock, configure: configure,
            settings: settings ?? fixture.LicensingSettings());

    // The merchant bearer validator uses system time; the issuer caps expiry by the session.
    internal static DateTimeOffset WholeSecondNow() =>
        DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    internal static string UniqueEmail() => $"billing-{Guid.NewGuid():N}@example.test";

    internal sealed record OwnerRow(Guid AccountId, Guid MerchantUserId, int Role, int Status);

    /// <summary>Every member row of every account, read past RLS through the superuser.</summary>
    internal static async Task<OwnerRow[]> ReadAccountRowsAsync(LicensingPostgreSqlFixture database, P0201Fixture fixture)
    {
        await using var connection = new NpgsqlConnection(database.Bootstrap(fixture.Owner.Database).ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT a.id, m.merchant_user_id, m.role, m.status
            FROM billing_accounts a LEFT JOIN billing_account_members m ON m.account_id = a.id
            ORDER BY a.created_at, a.id, m.created_at
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<OwnerRow>();
        while (await reader.ReadAsync())
            rows.Add(new OwnerRow(reader.GetGuid(0), reader.GetGuid(1), reader.GetInt32(2), reader.GetInt32(3)));
        return [.. rows];
    }

    internal static async Task<long> CountAsync(LicensingPostgreSqlFixture database, P0201Fixture fixture, string sql) =>
        (long)(await LicensingPostgreSqlFixture.ReadScalarAsync(database.Bootstrap(fixture.Owner.Database), sql))!;

    internal const string AccountsPath = "/internal/licensing/v1/customer/accounts";
    internal const string Create = "billing.account.create";
    internal const string List = "billing.account.list";

    internal static byte[] CreateBody(Guid operationKey, string name) =>
        JsonSerializer.SerializeToUtf8Bytes(new { operationKey, name });

    /// <summary>
    /// Independently built actor-scoped assertion (never the production issuer), so a defect shared by the
    /// merchant issuer and the licensing verifier cannot hide behind itself.
    /// </summary>
    internal static ActorAssertion Sign(P0201Fixture fixture, Guid actor, string action, byte[] body,
        Action<JsonObject>? mutate = null)
    {
        var now = fixture.Clock.Now.ToUnixTimeSeconds();
        var request = Guid.NewGuid();
        var method = action == Create ? "POST" : "GET";
        var claims = new JsonObject
        {
            ["v"] = 1, ["sub"] = actor, ["iss"] = "InteliBill.Merchant.Billing",
            ["aud"] = "InteliBill.Licensing.Customer", ["verified_identity"] = true, ["action"] = action,
            ["request_id"] = request, ["jti"] = Guid.NewGuid(), ["iat"] = now, ["nbf"] = now, ["exp"] = now + 60,
            ["method"] = method, ["path"] = AccountsPath, ["body_sha256"] = Convert.ToHexStringLower(SHA256.HashData(body)),
        };
        mutate?.Invoke(claims);
        var header = new JsonObject { ["alg"] = "RS256", ["typ"] = "intelibill-merchant-billing-delegation+jwt", ["kid"] = P0201Fixture.KeyId };
        var input = Base64UrlEncoder.Encode(JsonSerializer.SerializeToUtf8Bytes(header)) + "." +
            Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(claims.ToJsonString()));
        var signature = fixture.Key.SignData(Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return new ActorAssertion(actor, request, input + "." + Base64UrlEncoder.Encode(signature), body, new HttpMethod(method));
    }

    internal static async Task<HttpResponseMessage> SendAsync(HttpClient client, ActorAssertion assertion,
        byte[]? body = null, string path = AccountsPath)
    {
        using var request = new HttpRequestMessage(assertion.Method, new Uri(path, UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", assertion.Token);
        request.Headers.Add(P0201Fixture.RequestHeader, assertion.Request.ToString("D"));
        var bytes = body ?? assertion.Body;
        if (bytes.Length > 0)
        {
            request.Content = new ByteArrayContent(bytes);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }
        return await client.SendAsync(request);
    }

    internal static Task<HttpResponseMessage> CreateAsync(HttpClient client, P0201Fixture fixture, Guid actor,
        Guid operationKey, string name) => SendAsync(client, Sign(fixture, actor, Create, CreateBody(operationKey, name)));

    internal static Task<HttpResponseMessage> ListAsync(HttpClient client, P0201Fixture fixture, Guid actor) =>
        SendAsync(client, Sign(fixture, actor, List, []));

    internal static Task<HttpResponseMessage> AccessAsync(HttpClient client, P0201Fixture fixture, Guid account, Guid actor)
    {
        var token = P0201Tests.SignReadAssertion(fixture, account, actor, out var request);
        return P0201Fixture.SendAssertionAsync(client, account, request, token);
    }

    internal sealed record ActorAssertion(Guid Actor, Guid Request, string Token, byte[] Body, HttpMethod Method)
    {
        public override string ToString() => "[LOCAL FIXTURE CREDENTIAL]";
    }

    /// <summary>Run one statement as the restricted runtime login inside a transaction with the given context.</summary>
    internal static async Task<object?> RuntimeScalarAsync(P0201Fixture fixture, Guid? account, Guid? actor, string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.Runtime.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var scope = new NpgsqlCommand(
            "SELECT set_config('app.billing_account_id', $1, true), set_config('app.billing_actor_id', $2, true)",
            connection, transaction);
        scope.Parameters.AddWithValue(account?.ToString("D") ?? string.Empty);
        scope.Parameters.AddWithValue(actor?.ToString("D") ?? string.Empty);
        await scope.ExecuteNonQueryAsync();
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        return await command.ExecuteScalarAsync();
    }
}
