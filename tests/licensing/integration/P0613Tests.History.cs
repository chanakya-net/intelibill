using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0607aTests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0613Tests
{
    private static readonly int[] ThreeVersions = [3, 2, 1];
    private static readonly int[] TwoVersions = [2, 1];

    [Fact]
    public async Task MemberCanReachEveryVersionAcrossPages()
    {
        var clock = new P0201Clock { Now = P0611Tests.Instant };
        await using var fixture = await P0302Fixture.StartAsync(database, clock, P0306Tests.Settings);
        var purchase = await P0311Tests.VerifiedAsync(fixture);
        var emptyOrder = await P0311Tests.VerifiedAsync(fixture);
        using var billing = await P0611Tests.SignInBillingAsync(fixture);
        var adminPath = P0611Tests.Path(purchase.Quote);
        var customerPath = P0612Tests.DocumentsPath(purchase.Quote);

        var versions = await IssueEqualTimestampVersionsAsync(billing, adminPath, 26);
        await AssertPaginationAcrossMidAppendAsync(purchase, billing, adminPath, customerPath, versions);
        await AssertPagedDownloadsAndInvalidCursorsAsync(purchase, emptyOrder, billing, adminPath, customerPath, versions);
    }

    [Fact]
    public async Task AppliedCoverageTransferKeepsCorrectionHistoryWithOriginalPayer()
    {
        using var rsa = RSA.Create(2048);
        await using var s = await StartAsync(database, rsa);
        var doc = await P0607Tests.AttachOriginalAsync(s);
        var adminPath = $"/api/admin/accounts/{s.Source.Account:D}/orders/{doc.Order:D}/billing-documents";
        var confirmationBefore = await s.Source.Payer.Client.GetStringAsync(doc.Confirmation);
        var confirmationBytesBefore = await s.Source.Payer.Client.GetByteArrayAsync(doc.ConfirmationContent);

        var manager = await P0209Fixture.SignInAsync(s.F.Hosts, ShopRole.Owner);
        var memberId = await P0204Tests.InviteManagerAsync(s.Source.Payer.Client, s.Source.Account, manager.Client, manager.Email);
        var (financeSeed, transferManagerSeed) = await EnrollTransferOperatorsAsync(s);

        var pdf1 = P0611Tests.Pdf();
        var pdf2 = DistinctPdf("Pre-transfer correction v2");
        var v2Id = await SupersedeAsFinanceAsync(s, financeSeed, adminPath, doc.Version, pdf2, "pre-transfer-v2.pdf",
            SecondIssuer, SecondReference, SecondReason);

        using (var transferManager = await s.F.Checkout.Trial.Catalogue.SignInOperatorAsync(
            transferManagerSeed, "p0613.transfer.manager@example.test"))
            await P0607Tests.ApplyTransferAsync(s, transferManager);
        var moved = await s.F.Checkout.Delivery.MerchantScalarAsync(
            $"SELECT account_id FROM public.shop_license_states WHERE shop_id='{s.North.ShopId:D}'");
        Assert.Equal(s.Destination.Account, moved);

        var pdf3 = DistinctPdf("Post-transfer correction v3");
        var v3Id = await SupersedeAsFinanceAsync(s, financeSeed, adminPath, v2Id, pdf3, "post-transfer-v3.pdf",
            ThirdIssuer, ThirdReference, ThirdReason);

        var expected = new[] { (v3Id, 3, pdf3), (v2Id, 2, pdf2), (doc.Version, 1, pdf1) };
        await AssertSourceMembersReadAllVersionsAsync(doc, s.Source.Payer.Client, manager.Client, expected);
        await AssertRevokedAndDestinationDeniedAsync(s, doc, manager.Client, memberId, v2Id, v3Id);

        Assert.Equal(confirmationBefore, await s.Source.Payer.Client.GetStringAsync(doc.Confirmation));
        Assert.Equal(confirmationBytesBefore, await s.Source.Payer.Client.GetByteArrayAsync(doc.ConfirmationContent));
    }

    private static async Task<List<(int Version, Guid VersionId, byte[] Bytes)>> IssueEqualTimestampVersionsAsync(
        P0110CookieBrowser billing, string adminPath, int count)
    {
        var versions = new List<(int Version, Guid VersionId, byte[] Bytes)>();
        var pdf1 = DistinctPdf("Paged version 1");
        var v1 = await P0611Tests.AttachAsync(billing, adminPath, pdf1);
        versions.Add((1, v1.GetProperty("versionId").GetGuid(), pdf1));

        for (var version = 2; version <= count; version++)
        {
            var pdf = DistinctPdf($"Paged version {version}");
            var meta = SupersedeMetadata(pdf, $"paged-v{version}.pdf", "Paged Issuer", $"PAGE-REF-{version:D2}",
                P0611Tests.IssueDate, $"Reason for version {version}");
            var prev = versions[^1].VersionId;
            var preview = await PreviewSupersedeAsync(billing, adminPath, prev, meta);
            var saved = await SupersedeAsync(billing, adminPath, prev, meta, Digest(preview), pdf);
            versions.Add((version, saved.GetProperty("versionId").GetGuid(), pdf));
        }
        return versions;
    }

    private static async Task AssertPaginationAcrossMidAppendAsync(
        P0302Fixture.Purchase purchase, P0110CookieBrowser billing, string adminPath, string customerPath,
        List<(int Version, Guid VersionId, byte[] Bytes)> versions)
    {
        var page1 = await ReadCustomerPageAsync(purchase.Payer.Client, customerPath);
        Assert.Equal(25, page1.Length);
        Assert.Equal(Enumerable.Range(2, 25).Reverse().ToArray(), page1.Select(r => r.GetProperty("version").GetInt32()).ToArray());
        foreach (var row in page1)
        {
            Assert.False(row.TryGetProperty("supersessionReason", out _));
            Assert.False(row.TryGetProperty("uploadedBy", out _));
        }

        // Append version 27 between page 1 and page 2: cursor /before/2 still returns version 1 cleanly.
        var pdf27 = DistinctPdf("Paged version 27 appended mid-pagination");
        var v27Meta = SupersedeMetadata(pdf27, "paged-v27.pdf", "Paged Issuer", "PAGE-REF-27",
            P0611Tests.IssueDate, "Mid-pagination correction");
        var v27 = await SupersedeAsync(billing, adminPath, versions[^1].VersionId, v27Meta,
            Digest(await PreviewSupersedeAsync(billing, adminPath, versions[^1].VersionId, v27Meta)), pdf27);
        versions.Add((27, v27.GetProperty("versionId").GetGuid(), pdf27));

        var page2 = await ReadCustomerPageAsync(purchase.Payer.Client, $"{customerPath}/before/2");
        var oldest = Assert.Single(page2);
        Assert.Equal(1, oldest.GetProperty("version").GetInt32());
        Assert.Equal(versions[0].VersionId, oldest.GetProperty("versionId").GetGuid());
        Assert.Equal(JsonValueKind.Null, oldest.GetProperty("supersedesVersionId").ValueKind);

        using (var adminPage2 = await billing.SendAsync(HttpMethod.Get, $"{adminPath}/before/2"))
        {
            var adminOldest = Assert.Single((await ReadAsync(adminPage2, HttpStatusCode.OK))
                .GetProperty("documents").EnumerateArray());
            Assert.Equal(1, adminOldest.GetProperty("version").GetInt32());
        }

        var refreshed = await ReadCustomerPageAsync(purchase.Payer.Client, customerPath);
        Assert.Equal(27, refreshed[0].GetProperty("version").GetInt32());
        Assert.Equal(3, refreshed[^1].GetProperty("version").GetInt32());
    }

    private static async Task AssertPagedDownloadsAndInvalidCursorsAsync(
        P0302Fixture.Purchase purchase, P0302Fixture.Purchase emptyOrder, P0110CookieBrowser billing,
        string adminPath, string customerPath, List<(int Version, Guid VersionId, byte[] Bytes)> versions)
    {
        foreach (var target in new[] { versions[0], versions[^1] })
        {
            using var download = await purchase.Payer.Client.GetAsync($"{customerPath}/{target.VersionId:D}/content");
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
            Assert.True(download.Headers.CacheControl?.NoStore);
            Assert.Equal(target.Bytes, await download.Content.ReadAsByteArrayAsync());
        }

        foreach (var invalid in new[] { 1, 0, -3 })
        {
            using var badMember = await purchase.Payer.Client.GetAsync($"{customerPath}/before/{invalid}");
            Assert.Equal(HttpStatusCode.BadRequest, badMember.StatusCode);
            using var badAdmin = await billing.SendAsync(HttpMethod.Get, $"{adminPath}/before/{invalid}");
            Assert.Equal(HttpStatusCode.BadRequest, badAdmin.StatusCode);
        }
        Assert.Empty(await ReadCustomerPageAsync(emptyOrder.Payer.Client, $"{P0612Tests.DocumentsPath(emptyOrder.Quote)}/before/2"));
        var foreignList = $"{customerPath}/before/2".Replace(
            purchase.Account.ToString("D"), emptyOrder.Account.ToString("D"), StringComparison.Ordinal);
        using (var foreign = await emptyOrder.Payer.Client.GetAsync(foreignList))
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    private static async Task<(string FinanceSeed, string TransferManagerSeed)> EnrollTransferOperatorsAsync(Scenario s)
    {
        using var owner = await s.F.Checkout.Trial.Catalogue.SignInOwnerAsync();
        var financeSeed = await P0611Tests.EnrollAsync(s.F.Checkout, owner, "p0613.finance@example.test", "billing");
        var managerSeed = await P0611Tests.EnrollAsync(s.F.Checkout, owner, "p0613.transfer.manager@example.test", "licensing-manager");
        return (financeSeed, managerSeed);
    }

    private static async Task<Guid> SupersedeAsFinanceAsync(Scenario s, string financeSeed, string adminPath,
        Guid predecessorId, byte[] pdf, string fileName, string issuer, string reference, string reason)
    {
        using var finance = await s.F.Checkout.Trial.Catalogue.SignInOperatorAsync(financeSeed, "p0613.finance@example.test");
        var meta = SupersedeMetadata(pdf, fileName, issuer, reference, P0611Tests.IssueDate, reason);
        var saved = await SupersedeAsync(finance, adminPath, predecessorId, meta,
            Digest(await PreviewSupersedeAsync(finance, adminPath, predecessorId, meta)), pdf);
        return saved.GetProperty("versionId").GetGuid();
    }

    private static async Task AssertSourceMembersReadAllVersionsAsync(P0607Tests.OriginalDocument doc,
        HttpClient ownerClient, HttpClient managerClient, (Guid VersionId, int Version, byte[] Bytes)[] expected)
    {
        foreach (var client in new[] { ownerClient, managerClient })
        {
            var listed = await ReadCustomerPageAsync(client, doc.List);
            Assert.Equal(ThreeVersions, listed.Select(r => r.GetProperty("version").GetInt32()).ToArray());
            var older = await ReadCustomerPageAsync(client, $"{doc.List}/before/3");
            Assert.Equal(TwoVersions, older.Select(r => r.GetProperty("version").GetInt32()).ToArray());
            foreach (var item in expected)
            {
                using var dl = await client.GetAsync($"{doc.List}/{item.VersionId:D}/content");
                Assert.Equal(HttpStatusCode.OK, dl.StatusCode);
                Assert.True(dl.Headers.CacheControl?.NoStore);
                Assert.Equal(item.Bytes, await dl.Content.ReadAsByteArrayAsync());
            }
        }
    }

    private static async Task AssertRevokedAndDestinationDeniedAsync(Scenario s, P0607Tests.OriginalDocument doc,
        HttpClient managerClient, Guid memberId, Guid v2Id, Guid v3Id)
    {
        using (var removed = await s.Source.Payer.Client.PostAsJsonAsync(
            $"/api/billing/accounts/{s.Source.Account:D}/members/{memberId:D}/remove",
            new RemoveBillingManagerV1(Guid.NewGuid(), 1, "Removed after transfer check")))
            Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        foreach (var path in new[] { doc.List, $"{doc.List}/before/3", $"{doc.List}/{v3Id:D}/content" })
        {
            using var denied = await managerClient.GetAsync(path);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }

        foreach (var path in new[]
        {
            doc.List, $"{doc.List}/before/3", doc.Content, $"{doc.List}/{v2Id:D}/content", $"{doc.List}/{v3Id:D}/content"
        })
        {
            using var underSource = await s.Destination.Payer.Client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Forbidden, underSource.StatusCode);
            var underDest = path.Replace(s.Source.Account.ToString("D"), s.Destination.Account.ToString("D"), StringComparison.Ordinal);
            using var guessed = await s.Destination.Payer.Client.GetAsync(underDest);
            Assert.Equal(HttpStatusCode.NotFound, guessed.StatusCode);
        }
    }

    private static async Task<JsonElement[]> ReadCustomerPageAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("documents").EnumerateArray().ToArray();
    }
}
