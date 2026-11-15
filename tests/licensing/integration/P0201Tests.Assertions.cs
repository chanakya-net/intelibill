using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.Tokens;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0201Tests
{
    [Fact]
    public async Task InvalidAssertion_IsRejectedBeforeMembershipOrReplay()
    {
        using var fixture = await P0201Fixture.CreateAsync(database);
        // The production adapter returns 503 if reached. Every invalid proof must instead
        // be refused before the adapter runs.
        await using var licensing = fixture.LicensingApi(membership: false);
        using var client = licensing.CreateClient();
        var account = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var now = fixture.Clock.Now.ToUnixTimeSeconds();
        Action<JsonObject>[] invalidClaims =
        [
            claims => claims["exp"] = now,
            claims => { claims["iat"] = now + 1; claims["nbf"] = now + 1; },
            claims => claims["nbf"] = now + 1,
            claims => claims["exp"] = now + 61,
            claims => claims["iss"] = "InteliBill.Merchant",
            claims => claims["aud"] = "InteliBill.Licensing.Admin",
            claims => claims["aud"] = new JsonArray("InteliBill.Licensing.Customer"),
            claims => claims["v"] = 2,
            claims => claims["verified_identity"] = false,
            claims => claims["action"] = "billing.account.write",
            claims => claims["path"] = "/internal/licensing/v1/customer/another-path",
            claims => claims["method"] = "POST",
            claims => claims["body_sha256"] = new string('0', 64),
            claims => claims["account_id"] = Guid.NewGuid(),
            claims => claims["jti"] = Guid.Empty,
            claims => claims["iat"] = now.ToString(System.Globalization.CultureInfo.InvariantCulture),
            claims => claims.Remove("sub"),
            claims => claims["email"] = Email,
        ];
        foreach (var change in invalidClaims)
            await AssertInvalidAsync(client, CreateAssertion(fixture, account, actor, change));

        Action<JsonObject>[] invalidHeaders =
        [
            header => header["alg"] = "none",
            header => header["alg"] = "HS256",
            header => header["typ"] = "JWT",
            header => header["kid"] = "unknown",
            header => header["jku"] = "https://never-contact.test/key",
        ];
        foreach (var change in invalidHeaders)
            await AssertInvalidAsync(client, CreateAssertion(fixture, account, actor, mutateHeader: change));

        using var otherKey = RSA.Create(2048);
        await AssertInvalidAsync(client, CreateAssertion(fixture, account, actor, key: otherKey));
        await AssertInvalidAsync(client, CreateAssertion(fixture, account, actor, duplicateAudience: true));

        var valid = CreateAssertion(fixture, account, actor);
        await AssertInvalidAsync(client, valid with { Request = Guid.NewGuid() });
        await AssertInvalidAsync(client, valid, new Uri(P0201Fixture.InternalPath(account) + "?request=other", UriKind.Relative));
        using var positive = await P0201Fixture.SendAssertionAsync(client, account, valid.Request, valid.Token);
        await AssertProblemAsync(positive, HttpStatusCode.ServiceUnavailable, "Billing.MembershipUnavailable");

        using var adminRequest = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/admin/access", UriKind.Relative));
        adminRequest.Headers.Authorization = new("Bearer", valid.Token);
        using var admin = await client.SendAsync(adminRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, admin.StatusCode);
    }

    [Fact]
    public async Task Rotation_RetainsOldVerificationWithoutExtendingExpiry()
    {
        using var fixture = await P0201Fixture.CreateAsync(database);
        using var rotated = RSA.Create(2048);
        var account = Guid.NewGuid();
        var actor = Guid.NewGuid();
        await fixture.GrantAsync(account, actor);

        var lastOld = CreateAssertion(fixture, account, actor);
        var atExpiry = CreateAssertion(fixture, account, actor);
        var settings = fixture.LicensingSettings();
        settings["MerchantBillingVerification:PublicKeys:rotated"] = rotated.ExportSubjectPublicKeyInfoPem();
        await using var licensing = fixture.LicensingApi(settings: settings);
        using var client = licensing.CreateClient();
        fixture.Clock.Now = fixture.Clock.Now.AddSeconds(59).AddMilliseconds(999);

        using var old = await P0201Fixture.SendAssertionAsync(client, account, lastOld.Request, lastOld.Token);
        Assert.Equal(HttpStatusCode.OK, old.StatusCode);
        fixture.Clock.Now = fixture.Clock.Now.AddMilliseconds(1);
        await AssertInvalidAsync(client, atExpiry);

        var newAssertion = CreateAssertion(fixture, account, actor, mutateHeader: header => header["kid"] = "rotated", key: rotated);
        using var current = await P0201Fixture.SendAssertionAsync(client, account, newAssertion.Request, newAssertion.Token);
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);

        settings.Remove("MerchantBillingVerification:PublicKeys:" + P0201Fixture.KeyId);
        await using var retired = fixture.LicensingApi(settings: settings);
        using var retiredClient = retired.CreateClient();
        await AssertInvalidAsync(retiredClient, CreateAssertion(fixture, account, actor));
    }

    private static async Task AssertInvalidAsync(HttpClient client, LocalAssertion assertion, Uri? uri = null)
    {
        using var response = await P0201Fixture.SendAssertionAsync(
            client, assertion.Account, assertion.Request, assertion.Token, uri);
        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "Billing.DelegationInvalid");
    }

    private static LocalAssertion CreateAssertion(P0201Fixture fixture, Guid account, Guid actor,
        Action<JsonObject>? mutateClaims = null, Action<JsonObject>? mutateHeader = null,
        RSA? key = null, bool duplicateAudience = false)
    {
        var now = fixture.Clock.Now.ToUnixTimeSeconds();
        var request = Guid.NewGuid();
        var header = new JsonObject
        {
            ["typ"] = "intelibill-merchant-billing-delegation+jwt",
            ["alg"] = "RS256",
            ["kid"] = P0201Fixture.KeyId,
        };
        var claims = new JsonObject
        {
            ["v"] = 1, ["sub"] = actor, ["iss"] = "InteliBill.Merchant.Billing",
            ["aud"] = "InteliBill.Licensing.Customer", ["verified_identity"] = true,
            ["account_id"] = account, ["action"] = "billing.account.read",
            ["request_id"] = request, ["jti"] = Guid.NewGuid(),
            ["iat"] = now, ["nbf"] = now, ["exp"] = now + 60, ["method"] = "GET",
            ["path"] = P0201Fixture.InternalPath(account).OriginalString,
            ["body_sha256"] = Convert.ToHexStringLower(SHA256.HashData([])),
        };
        mutateClaims?.Invoke(claims);
        mutateHeader?.Invoke(header);
        var payload = claims.ToJsonString();
        if (duplicateAudience)
            payload = payload[..^1] + ",\"aud\":\"InteliBill.Licensing.Customer\"}";

        var input = Base64UrlEncoder.Encode(JsonSerializer.SerializeToUtf8Bytes(header)) + "." +
            Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(payload));
        var signature = (key ?? fixture.Key).SignData(Encoding.ASCII.GetBytes(input),
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return new LocalAssertion(account, request, input + "." + Base64UrlEncoder.Encode(signature));
    }

    private sealed record LocalAssertion(Guid Account, Guid Request, string Token)
    {
        public override string ToString() => "[LOCAL FIXTURE CREDENTIAL]";
    }
}
