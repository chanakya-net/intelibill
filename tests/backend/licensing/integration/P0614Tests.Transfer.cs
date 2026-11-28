using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using static Intelibill.Licensing.Integration.Tests.P0607aTests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0614Tests
{
    private const string TransferFinance = "p0614.finance@example.test";
    private const long TransferRefund = 3000L;

    /// <summary>
    /// Stories 6–11: a refund requested before an inter-account transfer settles after cutover against the original
    /// order, and a corrective v2 is issued on that order. The original payer keeps both the refund and every document;
    /// the new payer sees neither, its own independent term is not extended and no shop membership is granted.
    /// </summary>
    [Fact]
    public async Task TransferredShopKeepsOriginalRefundAndCorrectiveDocumentsPrivate()
    {
        using var rsa = TestRsaKeys.Create(TestRsaKeyPurpose.General);
        await using var s = await StartAsync(database, rsa);
        var doc = await P0607Tests.AttachOriginalAsync(s);
        var source = s.Source.Payer.Client;
        var confirmation = await P0608Tests.ConfirmationIdAsync(source, s.Source.Account, doc.Order);
        var request = await P0609Tests.RequestRefundAsync(s.Source, doc.Order, confirmation, TransferRefund);
        var confirmationBytes = await source.GetByteArrayAsync(doc.ConfirmationContent);
        var funding = await P0613Tests.FundingFactsAsync(s.F.Checkout, s.Source.Account, doc.Order);
        var destinationTerm = await P0308Tests.TermRowAsync(s.F, s.DestinationTerm);

        var (financeSeed, managerSeed) = await EnrollStageOperatorsAsync(s);
        using (var manager = await s.F.Checkout.Trial.Catalogue.SignInOperatorAsync(managerSeed, TransferManager))
            await P0607Tests.ApplyTransferAsync(s, manager);
        Assert.Equal(s.Destination.Account, await s.F.Checkout.Delivery.MerchantScalarAsync(
            $"SELECT account_id FROM public.shop_license_states WHERE shop_id='{s.North.ShopId:D}'"));
        Assert.Equal(1L, await s.F.CountAsync("coverage_transfer_bindings"));

        var v2 = await SettleAndCorrectAfterCutoverAsync(s, doc, request, financeSeed);
        await AssertSourceKeepsHistoryAsync(s, doc, request, v2);
        await AssertDestinationSeesNoHistoryAsync(s, doc, request, v2.Id);
        Assert.Equal(confirmationBytes, await source.GetByteArrayAsync(doc.ConfirmationContent));
        Assert.Equal(funding, await P0613Tests.FundingFactsAsync(s.F.Checkout, s.Source.Account, doc.Order));
        Assert.Equal(destinationTerm, await P0308Tests.TermRowAsync(s.F, s.DestinationTerm));
        Assert.Equal(0L, await s.F.CountAsync("payment_fulfilments", $"predecessor_term_id='{s.DestinationTerm:D}'"));
        Assert.Equal(0L, await s.F.Checkout.Delivery.MerchantScalarAsync($"""
            SELECT count(*) FROM public.shop_memberships
            WHERE shop_id='{s.North.ShopId:D}' AND user_id IN ('{s.Source.Payer.UserId:D}','{s.Destination.Payer.UserId:D}')
            """));
    }

    private sealed record Correction(Guid Id, byte[] Bytes);

    private const string TransferManager = "p0614.transfer.manager@example.test";

    /// <summary>One owner session enrols this stage's distinct Billing and licensing-manager operators.</summary>
    private static async Task<(string Finance, string Manager)> EnrollStageOperatorsAsync(Scenario s)
    {
        using var owner = await s.F.Checkout.Trial.Catalogue.SignInOwnerAsync();
        return (await P0611Tests.EnrollAsync(s.F.Checkout, owner, TransferFinance, "billing"),
            await P0611Tests.EnrollAsync(s.F.Checkout, owner, TransferManager, "licensing-manager"));
    }

    /// <summary>A distinct fresh-MFA Billing operator approves the pre-transfer request and issues v2 after cutover.</summary>
    private static async Task<Correction> SettleAndCorrectAfterCutoverAsync(Scenario s, P0607Tests.OriginalDocument doc,
        Guid request, string financeSeed)
    {
        var clock = s.F.Checkout.Trial.Catalogue.Clock;
        clock.Advanced += now => P0609Tests.SetCredentialTime(s.F, now);
        using var finance = await s.F.Checkout.Trial.Catalogue.SignInOperatorAsync(financeSeed, TransferFinance);
        P0609Tests.SetCredentialTime(s.F, clock.GetUtcNow());

        var account = s.Source.Account;
        var preview = await P0609Tests.PreviewAsync(finance, account, request, TransferRefund, "Preserve");
        await P0609Tests.ReadAsync(await P0609Tests.ApproveAsync(finance, account, request,
            P0609Tests.Command(preview, TransferRefund, "Preserve")), HttpStatusCode.Created);
        using var processed = await P0212Fixture.PostAsync(finance, P0609Tests.RequestPath(account, request) + "/simulator",
            new { outcome = "Processed" });
        Assert.Equal(HttpStatusCode.OK, processed.StatusCode);
        await P0610Tests.PumpRefundsAsync(s.F);
        await P0610Tests.PumpRefundsAsync(s.F);

        var adminPath = $"/api/admin/accounts/{account:D}/orders/{doc.Order:D}/billing-documents";
        var pdf = P0613Tests.DistinctPdf("P0614 post-transfer correction v2");
        var meta = P0613Tests.SupersedeMetadata(pdf, "p0614-v2.pdf", P0613Tests.SecondIssuer, P0613Tests.SecondReference,
            P0611Tests.IssueDate, P0613Tests.SecondReason);
        var saved = await P0613Tests.SupersedeAsync(finance, adminPath, doc.Version, meta,
            P0613Tests.Digest(await P0613Tests.PreviewSupersedeAsync(finance, adminPath, doc.Version, meta)), pdf);
        Assert.Equal(doc.Version, saved.GetProperty("supersedesVersionId").GetGuid());
        return new(saved.GetProperty("versionId").GetGuid(), pdf);
    }

    private static async Task AssertSourceKeepsHistoryAsync(Scenario s, P0607Tests.OriginalDocument doc, Guid request,
        Correction v2)
    {
        var refunds = await s.Source.Payer.Client.GetFromJsonAsync<JsonElement>(P0608Tests.RefundsPath(s.Source.Account));
        var item = Assert.Single(refunds.GetProperty("items").EnumerateArray());
        Assert.Equal(request, item.GetProperty("refundRequestId").GetGuid());
        Assert.Equal(doc.Order, item.GetProperty("orderId").GetGuid());
        Assert.Equal("Confirmed", item.GetProperty("status").GetString());
        Assert.Equal(1L, await s.F.CountAsync("refund_applications", "disposition='Applied'"));
        Assert.Equal(1L, await s.F.CountAsync("refund_attempts", $"order_id='{doc.Order:D}' AND account_id='{s.Source.Account:D}'"));

        var v1Bytes = P0611Tests.Pdf();
        Assert.NotEqual(P0611Tests.Hash(v1Bytes), P0611Tests.Hash(v2.Bytes));
        foreach (var (version, bytes) in new[] { (doc.Version, v1Bytes), (v2.Id, v2.Bytes) })
        {
            using var download = await s.Source.Payer.Client.GetAsync($"{doc.List}/{version:D}/content");
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
            Assert.True(download.Headers.CacheControl?.NoStore);
            Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
            Assert.Equal(bytes, await download.Content.ReadAsByteArrayAsync());
            Assert.Equal(P0611Tests.Hash(bytes), await s.F.ScalarAsync(
                $"SELECT content_sha256 FROM billing_document_versions WHERE version_id='{version:D}'"));
        }
        Assert.Equal(1L, await s.F.CountAsync("billing_document_versions",
            $"supersedes_version_id='{doc.Version:D}' AND account_id='{s.Source.Account:D}'"));
    }

    private static async Task AssertDestinationSeesNoHistoryAsync(Scenario s, P0607Tests.OriginalDocument doc,
        Guid request, Guid v2)
    {
        var client = s.Destination.Payer.Client;
        var own = await client.GetFromJsonAsync<JsonElement>(P0608Tests.RefundsPath(s.Destination.Account));
        Assert.DoesNotContain(own.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("refundRequestId").GetGuid() == request);
        using (var foreignRefunds = await client.GetAsync(P0608Tests.RefundsPath(s.Source.Account)))
            Assert.Equal(HttpStatusCode.Forbidden, foreignRefunds.StatusCode);
        await P0607Tests.VerifyDocumentsAsync(s, doc);
        var v2Content = $"{doc.List}/{v2:D}/content";
        using (var foreign = await client.GetAsync(v2Content))
            Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        using var guessed = await client.GetAsync(v2Content.Replace(
            s.Source.Account.ToString("D"), s.Destination.Account.ToString("D"), StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.NotFound, guessed.StatusCode);
    }
}
