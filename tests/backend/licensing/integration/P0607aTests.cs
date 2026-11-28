using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Intelibill.Domain.Enums;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// P06-07a: a source Billing Owner proposes moving one named shop to another billing account and the three required
/// live owners (source billing, destination billing, named shop) each record a purpose-bound consent. Nothing here
/// moves affiliation, money or access; provider approval and application belong to P06-07.
/// Real PostgreSQL, real merchant and licensing APIs, fixed clocks, local simulator only.
/// </summary>
public sealed partial class P0607aTests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    internal static readonly DateTimeOffset RequestedAt = P0606Tests.EarlyStart;

    internal static string RequestPath(Guid account, Guid term) =>
        $"/api/billing/accounts/{account}/subscriptions/{term}/coverage-transfers";

    internal static string TransferPath(Guid account, Guid transfer) =>
        $"/api/billing/accounts/{account}/coverage-transfers/{transfer}";

    internal static string ConsentPath(Guid account, Guid transfer, string purpose) =>
        $"{TransferPath(account, transfer)}/consents/{purpose}";

    internal static string ShopConsentPath(Guid account, Guid shop, Guid transfer) =>
        $"/api/billing/accounts/{account}/coverage-transfer-shops/{shop}/{transfer}/consent";

    [Fact]
    public async Task RequestRecordsProposalOnly()
    {
        using var key = TestRsaKeys.Create(TestRsaKeyPurpose.General);
        await using var s = await StartAsync(database, key);
        var before = await s.BaselineAsync();

        // The source Billing Owner previews the named shop, the destination and the remaining-access treatment.
        using var previewResponse = await s.Source.Payer.Client.PostAsJsonAsync(
            RequestPath(s.Source.Account, s.Term) + "/preview", s.PreviewBody());
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = await previewResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("North", preview.GetProperty("shopName").GetString());
        Assert.Equal(s.North.ShopId, preview.GetProperty("shopId").GetGuid());
        Assert.Equal(s.Destination.Account, preview.GetProperty("destinationAccountId").GetGuid());
        Assert.Equal(February29, preview.GetProperty("sourcePaidThroughAt").GetDateTimeOffset());
        Assert.Equal(s.Permit.ValidUntil, preview.GetProperty("observedPermitDeadline").GetDateTimeOffset());
        Assert.Equal(1, preview.GetProperty("observedPermitCount").GetInt32());
        Assert.True(preview.GetProperty("disconnectedQueuesUnknown").GetBoolean());
        Assert.Equal(before, await s.BaselineAsync());

        // The request freezes the proposal with zero consents.
        using var createdResponse = await s.Source.Payer.Client.PostAsJsonAsync(
            RequestPath(s.Source.Account, s.Term), RequestBody(preview, Guid.NewGuid()));
        Assert.True(createdResponse.StatusCode == HttpStatusCode.OK, await createdResponse.Content.ReadAsStringAsync());
        var created = await createdResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(Guid.Empty, created.GetProperty("transferId").GetGuid());
        Assert.Equal("AwaitingConsents", created.GetProperty("state").GetString());
        Assert.Equal(1L, created.GetProperty("proposalRevision").GetInt64());
        Assert.Equal("RetainUntilPaidThrough", created.GetProperty("treatment").GetString());
        Assert.Equal(s.Source.Account, created.GetProperty("sourceAccountId").GetGuid());
        Assert.Equal(s.Destination.Account, created.GetProperty("destinationAccountId").GetGuid());
        Assert.False(created.GetProperty("consentsCollected").GetBoolean());
        var consents = created.GetProperty("consents").EnumerateArray().ToArray();
        Assert.Equal(["SourceBillingOwner", "DestinationBillingOwner", "ShopOwner"],
            consents.Select(c => c.GetProperty("purpose").GetString()));
        Assert.All(consents, c => Assert.Equal("AwaitingConsent", c.GetProperty("status").GetString()));

        // One proposal fact, no consent fact, and no affiliation, money or access change on either account.
        Assert.Equal(1L, await s.F.CountAsync("coverage_transfer_proposals", $"source_account_id='{s.Source.Account}'"));
        Assert.Equal(0L, await s.F.CountAsync("coverage_transfer_consents"));
        Assert.Equal(before, await s.BaselineAsync());
        Assert.Equal("Allowed", await P0606Tests.FencedTradingAtAsync(s.F, s.North.ShopId, RequestedAt));
    }

    [Fact]
    public async Task ReviewedAllocationRequiresCutoverWithinRemainingPaidAccess()
    {
        using var rsa = TestRsaKeys.Create(TestRsaKeyPurpose.General);
        await using var s = await StartAsync(database, rsa);
        var before = await s.BaselineAsync();
        var client = s.Source.Payer.Client;
        var path = RequestPath(s.Source.Account, s.Term);
        var cutover = RequestedAt.AddHours(12);
        using var response = await client.PostAsJsonAsync(path + "/preview", s.PreviewBody("ReviewedAllocation", cutover));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(cutover, preview.GetProperty("proposedExpiresAt").GetDateTimeOffset());
        foreach (var bound in new[] { RequestedAt, preview.GetProperty("sourcePaidThroughAt").GetDateTimeOffset() })
        {
            using var refused = await client.PostAsJsonAsync(path + "/preview", s.PreviewBody("ReviewedAllocation", bound));
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Equal("Coverage.TransferInvalid", await CodeAsync(refused));
            var request = JsonSerializer.SerializeToNode(RequestBody(preview, Guid.NewGuid()))!;
            request["cutoverAt"] = bound;
            using var invalid = await client.PostAsJsonAsync(path, request);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal("Coverage.TransferInvalid", await CodeAsync(invalid));
        }
        using var created = await client.PostAsJsonAsync(path, RequestBody(preview, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var proposal = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ReviewedAllocation", proposal.GetProperty("treatment").GetString());
        Assert.Equal(cutover, proposal.GetProperty("cutoverAt").GetDateTimeOffset());
        Assert.Equal(cutover, proposal.GetProperty("expiresAt").GetDateTimeOffset());
        Assert.Equal(before, await s.BaselineAsync());
    }

    internal static object RequestBody(JsonElement preview, Guid key, string reason = "Branch sold to the destination company") => new
    {
        operationKey = key,
        destinationAccountId = preview.GetProperty("destinationAccountId").GetGuid(),
        shopId = preview.GetProperty("shopId").GetGuid(),
        treatment = preview.GetProperty("treatment").GetString(),
        cutoverAt = preview.GetProperty("cutoverAt").ValueKind == JsonValueKind.Null
            ? (DateTimeOffset?)null : preview.GetProperty("cutoverAt").GetDateTimeOffset(),
        reason,
        expectedSourceAffiliationId = preview.GetProperty("sourceAffiliationId").GetGuid(),
        expectedSourcePaidThroughAt = preview.GetProperty("sourcePaidThroughAt").GetDateTimeOffset(),
        expectedSourceOwnerRevision = preview.GetProperty("sourceOwnerRevision").GetInt64(),
        expectedEvidenceDigest = preview.GetProperty("evidenceDigest").GetString(),
        acknowledgedNamedShop = true,
        acknowledgedRemainingAccessTreatment = true,
        acknowledgedExistingPermits = true,
        acknowledgedUnknownDisconnectedQueues = true,
        acknowledgedHistoricalBillsStay = true,
    };

    /// <summary>Source account with a paid three-shop term (North trading with one real permit) and an unrelated destination account.</summary>
    internal static async Task<Scenario> StartAsync(LicensingPostgreSqlFixture db, RSA key, bool browser = false,
        bool sameOwner = false)
    {
        var f = await P0606Tests.StartPermitFixtureAsync(db, key, January31, browser);
        var (p, term) = await P0606Tests.PrepareWithTradingNorthAsync(f);
        var payer = sameOwner ? p.Payer : await P0209Fixture.SignInAsync(f.Hosts, ShopRole.Owner);
        var account = await P0209Fixture.CreateAccountAsync(payer.Client, "Destination Company");
        var destination = new Package(payer, account, [payer], p.Plan);
        var destinationTerm = Guid.Empty;
        // One owner of both accounts keeps an empty destination: no subscription there is a valid transfer target.
        if (!sameOwner)
        {
            await P0213Fixture.LinkAsync(account, payer, payer);
            await f.Checkout.Delivery.DrainAsync(true);
            await f.Checkout.Delivery.DrainAsync(false);
            (_, destinationTerm) = await PurchaseAsync(destination, "Month");
            await f.Checkout.Delivery.DrainAsync(true);
            await f.Checkout.Delivery.DrainAsync(false);
        }
        f.At(RequestedAt);
        var permit = await P0606Tests.IssueRealPermitAsync(f, p.Shops[2], RequestedAt, deliverObservation: true);
        return new(f, p, term, destination, destinationTerm, permit);
    }

    internal sealed record Scenario(P0308Fixture F, Package Source, Guid Term, Package Destination, Guid DestinationTerm,
        P0507Tests.Permit Permit) : IAsyncDisposable
    {
        internal P0209Fixture.Merchant North => Source.Shops[2];

        internal object PreviewBody(string treatment = "RetainUntilPaidThrough", DateTimeOffset? cutoverAt = null) => new
        {
            destinationAccountId = Destination.Account,
            shopId = North.ShopId,
            treatment,
            cutoverAt,
        };

        /// <summary>Every affiliation, money, coverage and delivery fact a transfer could move, for both accounts.</summary>
        internal async Task<string> BaselineAsync()
        {
            var parts = new List<string> { await P0605Tests.OriginalHistoryDigestAsync(F, Source, Term) };
            if (DestinationTerm != Guid.Empty)
                parts.Add(await P0605Tests.OriginalHistoryDigestAsync(F, Destination, DestinationTerm));
            foreach (var table in new[] { "subscription_coverage_terms", "payment_fulfilment_shops", "shop_affiliations",
                "complimentary_grants", "paid_slot_handoffs", "refund_requests", "billing_document_versions",
                "entitlement_snapshots", "entitlement_messages", "coverage_replacement_exceptions" })
                parts.Add($"{table}={await F.ScalarAsync($"""
                    SELECT coalesce(encode(sha256(convert_to(jsonb_agg(to_jsonb(t) ORDER BY to_jsonb(t))::text,'UTF8')),'hex'),'-')
                    FROM public.{table} t
                    """)}");
            parts.Add($"states={await F.Checkout.Delivery.MerchantScalarAsync("""
                SELECT encode(sha256(convert_to(coalesce(jsonb_agg(to_jsonb(t) ORDER BY to_jsonb(t))::text,''),'UTF8')),'hex')
                FROM public.shop_license_states t
                """)}");
            return string.Join("|", parts);
        }

        public ValueTask DisposeAsync() => F.DisposeAsync();
    }

    internal static async Task<JsonElement> PreviewAsync(Scenario s)
    {
        using var response = await s.Source.Payer.Client.PostAsJsonAsync(
            RequestPath(s.Source.Account, s.Term) + "/preview", s.PreviewBody());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    internal static async Task<JsonElement> CreateAsync(Scenario s, Guid? key = null)
    {
        using var response = await s.Source.Payer.Client.PostAsJsonAsync(
            RequestPath(s.Source.Account, s.Term), RequestBody(await PreviewAsync(s), key ?? Guid.NewGuid()));
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, raw);
        return JsonSerializer.Deserialize<JsonElement>(raw);
    }

    internal static async Task<JsonElement> ReadAsync(HttpClient client, Guid account, Guid transfer)
    {
        using var response = await client.GetAsync(TransferPath(account, transfer));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>One decision bound to the proposal revision, terms digest and authority revision the caller was shown.</summary>
    internal static Task<HttpResponseMessage> DecideAsync(HttpClient client, string consentPath, string decision,
        JsonElement shown, Guid? key = null, long? authorityRevision = null, string? termsDigest = null) =>
        client.PostAsJsonAsync($"{consentPath}/{decision}", new
        {
            operationKey = key ?? Guid.NewGuid(),
            expectedProposalRevision = shown.GetProperty("proposalRevision").GetInt64(),
            expectedTermsDigest = termsDigest ?? shown.GetProperty("termsDigest").GetString(),
            expectedAuthorityRevision = authorityRevision ?? shown.GetProperty(
                shown.TryGetProperty("authorityRevision", out _) ? "authorityRevision" : "viewerAuthorityRevision").GetInt64(),
        });

    internal static async Task<JsonElement> AcceptAsync(HttpClient client, Guid account, Guid transfer, string purpose)
    {
        using var response = await DecideAsync(client, ConsentPath(account, transfer, purpose), "accept",
            await ReadAsync(client, account, transfer));
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, raw);
        return JsonSerializer.Deserialize<JsonElement>(raw);
    }

    internal static async Task<JsonElement> ShopAcceptAsync(Scenario s, Guid transfer)
    {
        var path = ShopConsentPath(s.Source.Account, s.North.ShopId, transfer);
        using var response = await DecideAsync(s.North.Client, path, "accept",
            await s.North.Client.GetFromJsonAsync<JsonElement>(path));
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, raw);
        await s.F.Checkout.Delivery.DrainAsync(false);
        return JsonSerializer.Deserialize<JsonElement>(raw);
    }

    internal static string Status(JsonElement transfer, string purpose) =>
        transfer.GetProperty("consents").EnumerateArray().Single(c => c.GetProperty("purpose").GetString() == purpose)
            .GetProperty("status").GetString()!;

    internal static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    /// <summary>The production ownership swap: both rows change role and each takes a new membership revision.</summary>
    internal static Task SwapBillingOwnerAsync(Scenario s, Guid account, Guid from, Guid to) =>
        LicensingPostgreSqlFixture.ExecuteAsync(s.F.Hosts.LicensingOwner, $"""
            INSERT INTO billing_account_members(id,account_id,merchant_user_id,role,status,revision,created_at)
            SELECT gen_random_uuid(),'{account}','{to}',2,1,1,now()
            WHERE NOT EXISTS (SELECT FROM billing_account_members WHERE account_id='{account}' AND merchant_user_id='{to}');
            UPDATE billing_account_members SET role=2, revision=revision+1 WHERE account_id='{account}' AND merchant_user_id='{from}';
            UPDATE billing_account_members SET role=1, revision=revision+1 WHERE account_id='{account}' AND merchant_user_id='{to}'
            """);
}
