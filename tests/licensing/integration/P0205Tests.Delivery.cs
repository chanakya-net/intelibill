using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Intelibill.Shared.Contracts.V1;
using Microsoft.IdentityModel.Tokens;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0205Tests
{
    private const string DeliveryPath = ShopAffiliationsV1.DeliveryPath;
    private static readonly JsonSerializerOptions DeliveryJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task DeliveryRejectsInvalidCredentialsAndRequestBindingsWithoutWritingFacts()
    {
        using var key = RSA.Create(2048);
        using var unknownKey = RSA.Create(2048);
        using var captured = new CaptureDelivery();
        await using var host = await StartAsync(captured.Attach, key);
        using var client = host.Licensing.CreateClient();
        using var owner = host.Merchant.CreateClient();
        var (body, path) = await AcceptAndCaptureAsync(host, owner, captured);
        var now = host.Fixture.Clock.Now.ToUnixTimeSeconds();
        var valid = SignDelivery(key, body, now);
        var eventBody = ReadDecision(body);
        var interactive = P0201Tests.SignReadAssertion(host.Fixture, eventBody.AccountId, eventBody.ActorId, out _);
        string?[] rejected =
        [
            null,
            "malformed",
            interactive,
            SignDelivery(host.Fixture.Key, body, now),
            SignDelivery(unknownKey, body, now),
            SignDelivery(key, body, now, tokenType: MerchantBillingDelegationV1.TokenType),
            SignDelivery(key, body, now, fields => fields["iss"] = MerchantBillingDelegationV1.Issuer),
            SignDelivery(key, body, now, fields => fields["aud"] = MerchantBillingDelegationV1.Audience),
            SignDelivery(key, body, now - ShopAffiliationDeliveryV1.LifetimeSeconds),
            SignDelivery(key, body, now, fields => fields["path"] = DeliveryPath + "/other"),
            SignDelivery(key, body, now, fields => fields["method"] = "GET"),
        ];
        foreach (var assertion in rejected)
            await AssertDeliveryProblemAsync(client, body, assertion, HttpStatusCode.Unauthorized, "Billing.DelegationInvalid");
        await AssertDeliveryProblemAsync(client, ChangeOwnerName(body), valid, HttpStatusCode.Unauthorized, "Billing.DelegationInvalid");
        await AssertDeliveryProblemAsync(client, body, valid, HttpStatusCode.Unauthorized, "Billing.DelegationInvalid", DeliveryPath + "?other=1");
        Assert.Equal((0L, 0L, 0L), await DeliveryCountsAsync(host));

        await CompleteDeliveryAsync(owner, path, captured);
    }

    [Fact]
    public async Task DeliveryDistinguishesTransportReplayFromTheOriginalDurableOutcome()
    {
        using var key = RSA.Create(2048);
        using var captured = new CaptureDelivery();
        await using var host = await StartAsync(captured.Attach, key);
        using var client = host.Licensing.CreateClient();
        using var owner = host.Merchant.CreateClient();
        var (body, path) = await AcceptAndCaptureAsync(host, owner, captured);
        var now = host.Fixture.Clock.Now.ToUnixTimeSeconds();
        var assertion = SignDelivery(key, body, now);
        var original = await AcknowledgeDeliveryAsync(client, body, assertion);
        Assert.Equal(new ShopAffiliationAcknowledgementV1(ReadDecision(body).EventId, "Applied"), original);
        Assert.Equal((1L, 1L, 1L), await DeliveryCountsAsync(host));
        var originalFacts = await DeliveryFactsAsync(host);

        await AssertDeliveryProblemAsync(client, body, assertion, HttpStatusCode.Unauthorized, "Billing.DelegationReplayed");
        Assert.Equal((1L, 1L, 1L), await DeliveryCountsAsync(host));

        var changed = ChangeOwnerName(body);
        await AssertDeliveryProblemAsync(client, changed, SignDelivery(key, changed, now),
            HttpStatusCode.Conflict, "Billing.AffiliationConflict");
        Assert.Equal((1L, 1L, 1L), await DeliveryCountsAsync(host));
        Assert.Equal(originalFacts, await DeliveryFactsAsync(host));

        Assert.Equal(original, await AcknowledgeDeliveryAsync(client, body, SignDelivery(key, body, now)));
        Assert.Equal((1L, 1L, 2L), await DeliveryCountsAsync(host));
        Assert.Equal(originalFacts, await DeliveryFactsAsync(host));

        await CompleteDeliveryAsync(owner, path, captured);
    }

    private async Task<(byte[] Body, string ConsentPath)> AcceptAndCaptureAsync(
        Hosts host, HttpClient owner, CaptureDelivery captured)
    {
        using var payer = host.Merchant.CreateClient();
        await SignInAsync(host.Merchant, payer);
        var shop = await SignInAsync(host.Merchant, owner, withShop: true);
        var account = await CreateAccountAsync(payer);
        var request = await RequestAsync(payer, account, shop);
        captured.Watch(request);
        var path = ConsentPath(account, shop, request);
        await DecideAsync(owner, path, "grant");
        Assert.Equal("Accepted", await DecideAsync(owner, path, "accept"));
        return (await captured.Body.WaitAsync(TimeSpan.FromSeconds(15)), path);
    }

    private static async Task CompleteDeliveryAsync(HttpClient owner, string path, CaptureDelivery captured)
    {
        // Each test has a different licensing database but shares the merchant fixture.
        captured.Forward();
        await WaitForConsentAsync(owner, path, "Applied");
    }

    private async Task<(long Affiliations, long Inbox, long TransportUses)> DeliveryCountsAsync(Hosts host) =>
        (await P0202Fixture.CountAsync(database, host.Fixture, "SELECT count(*) FROM public.shop_affiliations"),
         await P0202Fixture.CountAsync(database, host.Fixture, "SELECT count(*) FROM public.shop_affiliation_inbox"),
         await P0202Fixture.CountAsync(database, host.Fixture, "SELECT count(*) FROM public.shop_affiliation_delivery_uses"));

    private Task<object?> DeliveryFactsAsync(Hosts host) => LicensingPostgreSqlFixture.ReadScalarAsync(
        database.Bootstrap(host.Fixture.Owner.Database), """
        SELECT jsonb_build_array(to_jsonb(a), to_jsonb(i))::text
        FROM public.shop_affiliations a JOIN public.shop_affiliation_inbox i USING (event_id)
        """);

    private static ShopAffiliationDecisionV1 ReadDecision(byte[] body) =>
        JsonSerializer.Deserialize<ShopAffiliationDecisionV1>(body, DeliveryJson)!;

    private static byte[] ChangeOwnerName(byte[] body) =>
        JsonSerializer.SerializeToUtf8Bytes(ReadDecision(body) with { OwnerName = "Changed Fixture Owner" }, DeliveryJson);

    private static async Task AssertDeliveryProblemAsync(HttpClient client, byte[] body, string? assertion,
        HttpStatusCode status, string code, string path = DeliveryPath)
    {
        using var response = await DeliverAsync(client, body, assertion, path);
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    private static async Task<ShopAffiliationAcknowledgementV1?> AcknowledgeDeliveryAsync(
        HttpClient client, byte[] body, string assertion)
    {
        using var response = await DeliverAsync(client, body, assertion, DeliveryPath);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<ShopAffiliationAcknowledgementV1>();
    }

    private static async Task<HttpResponseMessage> DeliverAsync(HttpClient client, byte[] body, string? assertion, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        if (assertion is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", assertion);
        return await client.SendAsync(request);
    }

    private static string SignDelivery(RSA key, byte[] body, long now, Action<JsonObject>? change = null,
        string tokenType = ShopAffiliationDeliveryV1.TokenType)
    {
        var header = new { typ = tokenType, alg = "RS256", kid = "local-affiliation" };
        var fields = new JsonObject
        {
            ["v"] = 1, ["iss"] = ShopAffiliationDeliveryV1.Issuer, ["aud"] = ShopAffiliationDeliveryV1.Audience,
            ["jti"] = Guid.NewGuid(), ["iat"] = now, ["nbf"] = now, ["exp"] = now + ShopAffiliationDeliveryV1.LifetimeSeconds,
            ["method"] = "POST", ["path"] = DeliveryPath, ["body_sha256"] = Convert.ToHexStringLower(SHA256.HashData(body)),
        };
        change?.Invoke(fields);
        var input = Base64UrlEncoder.Encode(JsonSerializer.SerializeToUtf8Bytes(header)) + "." +
            Base64UrlEncoder.Encode(JsonSerializer.SerializeToUtf8Bytes(fields));
        return input + "." + Base64UrlEncoder.Encode(key.SignData(Encoding.ASCII.GetBytes(input),
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    private sealed class CaptureDelivery : DelegatingHandler
    {
        private readonly TaskCompletionSource<Guid> _watched = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<byte[]> _captured = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private volatile bool _forward;

        internal Task<byte[]> Body => _captured.Task;
        internal void Watch(Guid request) => _watched.SetResult(request);
        internal void Forward() => _forward = true;

        internal CaptureDelivery Attach(HttpMessageHandler inner)
        {
            InnerHandler = inner;
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_forward || !_watched.Task.IsCompletedSuccessfully || request.RequestUri?.AbsolutePath != DeliveryPath)
                return await base.SendAsync(request, cancellationToken);

            var body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            if (ReadDecision(body).RequestId != await _watched.Task)
                return await base.SendAsync(request, cancellationToken);

            // Leave the real decision pending while the test exercises the licensing HTTP boundary.
            _captured.TrySetResult(body);
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }
    }
}
