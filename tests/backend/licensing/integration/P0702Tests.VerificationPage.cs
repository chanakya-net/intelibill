using System.Net;
using System.Net.Http.Json;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0702Tests
{
    [Theory]
    [InlineData("verify-only", HttpStatusCode.NoContent)]
    [InlineData("expire", HttpStatusCode.BadRequest)]
    [InlineData("revoke", HttpStatusCode.BadRequest)]
    public async Task HolderMustExplicitlyConsentWithAnActiveProof(string scenario, HttpStatusCode expected)
    {
        var clock = new P0201Clock { Now = P0210Fixture.Start };
        await using var fixture = await P0213Fixture.StartAsync(database, licensingExtras: mailpit.BillingSettings(), businessClock: clock);
        var owner = await SignInAsync(fixture.Hosts);
        var account = await CreateAccountAsync(owner.Client);
        var address = P0202Fixture.UniqueEmail();
        var contacts = $"/api/billing/accounts/{account:D}/notice-contacts";
        using var approved = await owner.Client.PostAsJsonAsync(contacts,
            new ApproveBillingContactRequestV1(Guid.NewGuid(), null, address, "ta-IN"));
        var contact = (await approved.Content.ReadFromJsonAsync<BillingContactV1>())!;
        var proof = await mailpit.ReadProofAsync(fixture.Hosts.Licensing.Services, address, contact.ContactId);
        await AssertProofProtectedAsync(fixture, contact.ContactId, address, proof);
        const string origin = "http://localhost:5081";
        using var page = await fixture.LicensingApi.GetAsync(origin + "/billing-contact/verify?lang=ta-IN");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.True(page.Headers.CacheControl?.NoStore);
        Assert.Equal("no-referrer", page.Headers.GetValues("Referrer-Policy").Single());
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("lang=\"ta-IN\"", html);
        Assert.DoesNotContain(" checked", html);
        var key = Guid.NewGuid();
        Task<HttpStatusCode> Post(string email, string source, Guid operation) => ConfirmPublicAsync(fixture.LicensingApi, origin, source,
            new { accountId = account, contactId = contact.ContactId, operationKey = operation, verifiedEmail = email, proofToken = proof,
                optedIn = false, locale = "ta-IN" });
        Assert.Equal(HttpStatusCode.Forbidden, await Post(address, "https://wrong.example.test", key));
        Assert.Equal(HttpStatusCode.BadRequest, await Post(P0202Fixture.UniqueEmail(), origin, key));
        if (scenario == "expire")
            clock.Now = clock.Now.AddHours(24);
        if (scenario == "revoke")
            (await owner.Client.PostAsJsonAsync(contacts + $"/{contact.ContactId:D}/revoke",
                new RevokeBillingContactRequestV1(Guid.NewGuid()))).EnsureSuccessStatusCode();
        Assert.Equal(expected, await Post(address, origin, key));
        if (expected == HttpStatusCode.NoContent)
        {
            Assert.Equal(HttpStatusCode.NoContent, await Post(address, origin, key));
            Assert.Equal(HttpStatusCode.BadRequest, await Post(address, origin, Guid.NewGuid()));
        }
        Assert.Equal(expected == HttpStatusCode.NoContent, await fixture.SourceScalarAsync($"""
            SELECT verified_at IS NOT NULL FROM billing_notice_contacts WHERE id = '{contact.ContactId:D}'
            """));
        Assert.Equal(0L, await fixture.SourceScalarAsync("SELECT count(*) FROM billing_notice_contacts WHERE opted_in_at IS NOT NULL"));
    }

    private static async Task<HttpStatusCode> ConfirmPublicAsync(HttpClient api, string origin, string source, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, origin + "/billing-contact/verify");
        request.Content = JsonContent.Create(body);
        request.Headers.Add("Origin", source);
        using var reply = await api.SendAsync(request);
        Assert.True(reply.Headers.CacheControl?.NoStore);
        Assert.Empty(await reply.Content.ReadAsStringAsync());
        return reply.StatusCode;
    }

    private static async Task AssertProofProtectedAsync(P0213Fixture fixture, Guid contact, string address, string proof)
    {
        var persisted = (string)(await fixture.SourceScalarAsync($"""
            SELECT concat(to_jsonb(c), to_jsonb(d), (SELECT jsonb_agg(to_jsonb(e)) FROM notification_email_events e
                WHERE e.delivery_id = d.id))
            FROM billing_notice_contacts c JOIN notification_email_deliveries d ON d.proof_contact_id = c.id
            WHERE c.id = '{contact:D}'
            """))!;
        Assert.False(persisted.Contains(address, StringComparison.OrdinalIgnoreCase));
        Assert.False(persisted.Contains(proof, StringComparison.Ordinal));
        Assert.False(persisted.Contains("/billing-contact/verify", StringComparison.Ordinal));
    }
}
