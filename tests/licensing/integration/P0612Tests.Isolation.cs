using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Intelibill.Shared.Contracts.V1;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0612Tests
{
    [Fact]
    public async Task ForeignAccountGuessedIdsAndUnknownVersionsAreRefused()
    {
        await using var fixture = await P0611Tests.StartAsync(database);
        var purchase = await P0311Tests.VerifiedAsync(fixture);
        var secondQuote = await AcceptSecondOrderOnSameAccountAsync(fixture, purchase);
        using var billing = await P0611Tests.SignInBillingAsync(fixture);
        var uploaded = await P0611Tests.AttachAsync(billing, P0611Tests.Path(purchase.Quote), P0611Tests.Pdf());
        var versionId = uploaded.GetProperty("versionId").GetGuid();

        var listPath = DocumentsPath(purchase.Quote);
        var contentPath = $"{listPath}/{versionId:D}/content";

        // Non-member outsider on account A's exact list and content paths -> 403.
        var outsider = await P0209Fixture.SignInAsync(fixture.Hosts, ShopRole.Owner);
        await AssertRefusedAsync(await outsider.Client.GetAsync(listPath), HttpStatusCode.Forbidden);
        await AssertRefusedAsync(await outsider.Client.GetAsync(contentPath), HttpStatusCode.Forbidden);

        // Same payer under their other account B with A's orderId/versionId -> 404 Billing.DocumentNotFound.
        var accountB = await P0209Fixture.CreateAccountAsync(purchase.Payer.Client, "Second account");
        var foreignList = listPath.Replace(purchase.Account.ToString("D"), accountB.ToString("D"), StringComparison.Ordinal);
        var foreignContent = contentPath.Replace(purchase.Account.ToString("D"), accountB.ToString("D"), StringComparison.Ordinal);
        await AssertRefusedAsync(await purchase.Payer.Client.GetAsync(foreignList), HttpStatusCode.NotFound, BillingDocumentsV1.NotFound);
        await AssertRefusedAsync(await purchase.Payer.Client.GetAsync(foreignContent), HttpStatusCode.NotFound, BillingDocumentsV1.NotFound);

        // Random version under A's order -> 404 Billing.DocumentNotFound.
        await AssertRefusedAsync(await purchase.Payer.Client.GetAsync($"{listPath}/{Guid.NewGuid():D}/content"),
            HttpStatusCode.NotFound, BillingDocumentsV1.NotFound);

        // Valid version ID queried under another order of A -> 404 Billing.DocumentNotFound.
        await AssertRefusedAsync(await purchase.Payer.Client.GetAsync($"{DocumentsPath(secondQuote)}/{versionId:D}/content"),
            HttpStatusCode.NotFound, BillingDocumentsV1.NotFound);

        // Unauthenticated client -> 401.
        using var anonymous = fixture.Hosts.Merchant.CreateClient();
        await AssertRefusedAsync(await anonymous.GetAsync(listPath), HttpStatusCode.Unauthorized);
        await AssertRefusedAsync(await anonymous.GetAsync(contentPath), HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ManagerMayDownloadAndRemovedMemberMayNot()
    {
        await using var fixture = await P0611Tests.StartAsync(database);
        var purchase = await P0311Tests.VerifiedAsync(fixture);
        using var billing = await P0611Tests.SignInBillingAsync(fixture);
        var pdf = P0611Tests.Pdf();
        var uploaded = await P0611Tests.AttachAsync(billing, P0611Tests.Path(purchase.Quote), pdf);
        var versionId = uploaded.GetProperty("versionId").GetGuid();

        var manager = await P0209Fixture.SignInAsync(fixture.Hosts, ShopRole.Owner);
        var memberId = await P0204Tests.InviteManagerAsync(purchase.Payer.Client, purchase.Account, manager.Client, manager.Email);

        var listPath = DocumentsPath(purchase.Quote);
        var contentPath = $"{listPath}/{versionId:D}/content";
        using (var allowedList = await manager.Client.GetAsync(listPath))
            Assert.Equal(HttpStatusCode.OK, allowedList.StatusCode);
        using (var allowedDownload = await manager.Client.GetAsync(contentPath))
        {
            Assert.Equal(HttpStatusCode.OK, allowedDownload.StatusCode);
            Assert.Equal(pdf, await allowedDownload.Content.ReadAsByteArrayAsync());
        }

        using var removed = await purchase.Payer.Client.PostAsJsonAsync(
            $"/api/billing/accounts/{purchase.Account:D}/members/{memberId:D}/remove",
            new RemoveBillingManagerV1(Guid.NewGuid(), 1, "Manager removed after fixture check"));
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);

        await AssertRefusedAsync(await manager.Client.GetAsync(listPath), HttpStatusCode.Forbidden);
        await AssertRefusedAsync(await manager.Client.GetAsync(contentPath), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task HistoryStaysWithTheIssuingAccount()
    {
        await using var fixture = await P0611Tests.StartAsync(database);
        var purchase = await P0311Tests.VerifiedAsync(fixture);
        var accountB = await P0209Fixture.CreateAccountAsync(purchase.Payer.Client, "Payer second account");
        var outsider = await P0209Fixture.SignInAsync(fixture.Hosts, ShopRole.Owner);
        var outsiderAccount = await P0209Fixture.CreateAccountAsync(outsider.Client, "Outsider account");
        using var billing = await P0611Tests.SignInBillingAsync(fixture);
        var pdf = P0611Tests.Pdf();
        var uploaded = await P0611Tests.AttachAsync(billing, P0611Tests.Path(purchase.Quote), pdf);
        var versionId = uploaded.GetProperty("versionId").GetGuid();

        var newOwner = await TransferOwnershipAndMutateAsync(fixture, purchase);

        var listPath = DocumentsPath(purchase.Quote);
        var contentPath = $"{listPath}/{versionId:D}/content";
        Assert.Equal(pdf, await purchase.Payer.Client.GetByteArrayAsync(contentPath));
        Assert.Equal(pdf, await newOwner.Client.GetByteArrayAsync(contentPath));

        var foreignB = contentPath.Replace(purchase.Account.ToString("D"), accountB.ToString("D"), StringComparison.Ordinal);
        await AssertRefusedAsync(await purchase.Payer.Client.GetAsync(foreignB), HttpStatusCode.NotFound, BillingDocumentsV1.NotFound);
        var foreignOutsider = contentPath.Replace(purchase.Account.ToString("D"), outsiderAccount.ToString("D"), StringComparison.Ordinal);
        await AssertRefusedAsync(await outsider.Client.GetAsync(foreignOutsider), HttpStatusCode.NotFound, BillingDocumentsV1.NotFound);
        await AssertRefusedAsync(await outsider.Client.GetAsync(contentPath), HttpStatusCode.Forbidden);

        // Visibility is keyed by billing account alone: no shop or affiliation column exists and account_id never changes.
        Assert.Equal(0L, await P0611Tests.ScalarAsync(fixture, """
            SELECT count(*) FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'billing_document_versions'
                AND (column_name LIKE '%shop%' OR column_name LIKE '%affiliation%')
            """));
        Assert.Equal(purchase.Account, (Guid)(await P0611Tests.ScalarAsync(fixture,
            "SELECT account_id FROM public.billing_document_versions"))!);
    }

    [Fact]
    public async Task SupportOperatorAndAdminRoutesAreUnaffected()
    {
        await using var fixture = await P0611Tests.StartAsync(database);
        var purchase = await P0311Tests.VerifiedAsync(fixture);
        using var billing = await P0611Tests.SignInBillingAsync(fixture);
        var uploaded = await P0611Tests.AttachAsync(billing, P0611Tests.Path(purchase.Quote), P0611Tests.Pdf());
        var versionId = uploaded.GetProperty("versionId").GetGuid();

        using var owner = await fixture.Trial.Catalogue.SignInOwnerAsync();
        const string supportEmail = "p0612.support@example.test";
        var seed = await P0611Tests.EnrollAsync(fixture, owner, supportEmail, "support");
        using var support = await fixture.Trial.Catalogue.SignInOperatorAsync(seed, supportEmail);
        using var supportRead = await support.SendAsync(HttpMethod.Get, P0611Tests.Path(purchase.Quote));
        Assert.Equal(HttpStatusCode.Forbidden, supportRead.StatusCode);

        using var adminList = await billing.SendAsync(HttpMethod.Get, P0611Tests.Path(purchase.Quote));
        Assert.Equal(HttpStatusCode.OK, adminList.StatusCode);

        // No admin byte-download route is introduced in P06-12.
        using var adminDownload = await billing.SendAsync(HttpMethod.Get, $"{P0611Tests.Path(purchase.Quote)}/{versionId:D}/content");
        Assert.Equal(HttpStatusCode.NotFound, adminDownload.StatusCode);
    }

    private static async Task<CheckoutQuoteV1> AcceptSecondOrderOnSameAccountAsync(
        P0302Fixture fixture, P0302Fixture.Purchase purchase)
    {
        var secondShop = await P0209Fixture.SignInAsync(fixture.Hosts, ShopRole.Owner);
        await P0213Fixture.LinkAsync(purchase.Account, purchase.Payer, secondShop);
        await fixture.Delivery.DrainAsync(source: true);
        await fixture.Delivery.DrainAsync(source: false);
        var quote = await P0301Tests.CreateQuoteAsync(purchase.Payer.Client, purchase.Account,
            new(Guid.NewGuid(), purchase.Quote.PlanVersionId, "Month", [secondShop.ShopId], 0, 1));
        using var accepted = await purchase.Payer.Client.PostAsJsonAsync(P0301Tests.AcceptPath(quote), P0301Tests.Accept(quote));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        return (await accepted.Content.ReadFromJsonAsync<CheckoutQuoteV1>())!;
    }

    private static async Task<P0209Fixture.Merchant> TransferOwnershipAndMutateAsync(
        P0302Fixture fixture, P0302Fixture.Purchase purchase)
    {
        var named = await P0209Fixture.SignInAsync(fixture.Hosts, ShopRole.Owner);
        using var issued = await purchase.Payer.Client.PostAsJsonAsync($"/api/billing/accounts/{purchase.Account:D}/ownership-transfers",
            new
            {
                operationKey = Guid.NewGuid(), expectedVersion = 1, recipientKind = "email", recipient = named.Email,
                reason = "Fixture responsibility handover after invoice", confirmTransfer = true,
            });
        Assert.Equal(HttpStatusCode.Accepted, issued.StatusCode);
        var inbox = await named.Client.GetFromJsonAsync<JsonElement>("/api/billing/ownership-transfers");
        var transfer = Assert.Single(inbox.GetProperty("transfers").EnumerateArray()).GetProperty("transferId").GetGuid();
        using var accepted = await named.Client.PostAsJsonAsync($"/api/billing/ownership-transfers/{transfer:D}/accept",
            new
            {
                operationKey = Guid.NewGuid(), expectedVersion = 1,
                responsibilityPolicyVersion = "prospective-billing-owner/1", acceptResponsibility = true,
            });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        await P0301Tests.RetirePlanAsync(fixture.Trial, purchase.Quote);
        await P0301Tests.PublishPlanAsync(fixture.Trial, monthlyPricePaise: 99000);
        await LicensingPostgreSqlFixture.ExecuteAsync(new NpgsqlConnectionStringBuilder(fixture.Delivery.MerchantDb.ConnectionString),
            $"UPDATE public.shops SET name = 'Renamed after invoice' WHERE id = '{purchase.Payer.ShopId:D}'; UPDATE public.users SET first_name = 'Changed' WHERE id = '{purchase.Payer.UserId:D}'");
        return named;
    }

    private static async Task AssertRefusedAsync(HttpResponseMessage response, HttpStatusCode expected, string? code = null)
    {
        using (response)
        {
            Assert.Equal(expected, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("%PDF", body, StringComparison.Ordinal);
            if (code is not null)
            {
                using var doc = JsonDocument.Parse(body);
                Assert.Equal(code, doc.RootElement.GetProperty("code").GetString());
            }
        }
    }
}
