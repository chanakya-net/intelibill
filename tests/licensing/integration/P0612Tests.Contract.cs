using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using ErrorOr;
using Intelibill.Infrastructure.Data;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.Documents;
using Intelibill.Licensing.Application.Payments;
using Intelibill.Licensing.Domain.Policies;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0612Tests
{
    [Fact]
    public void MerchantRelayRejectsWrongTypeLengthDispositionAndDigest()
    {
        var version = Guid.NewGuid();
        var bytes = P0611Tests.Pdf();
        var hash = P0611Tests.Hash(bytes);

        using var valid = BuildValidResponse(version, bytes, hash);
        Assert.True(ValidBillingDocument(valid, version, bytes));

        using (var wrongType = BuildValidResponse(version, bytes, hash))
        {
            wrongType.Content.Headers.ContentType!.MediaType = "text/html";
            Assert.False(ValidBillingDocument(wrongType, version, bytes));
        }
        using (var wrongLength = BuildValidResponse(version, bytes, hash))
        {
            wrongLength.Content.Headers.ContentLength = bytes.Length + 1;
            Assert.False(ValidBillingDocument(wrongLength, version, bytes));
        }
        using (var inlineDisp = BuildValidResponse(version, bytes, hash))
        {
            inlineDisp.Content.Headers.ContentDisposition!.DispositionType = "inline";
            Assert.False(ValidBillingDocument(inlineDisp, version, bytes));
        }
        using (var badName = BuildValidResponse(version, bytes, hash))
        {
            badName.Content.Headers.ContentDisposition!.FileName = "../foreign.pdf";
            Assert.False(ValidBillingDocument(badName, version, bytes));
        }
        using (var noCache = BuildValidResponse(version, bytes, hash))
        {
            noCache.Headers.CacheControl = null;
            Assert.False(ValidBillingDocument(noCache, version, bytes));
        }
        using (var noSniffMissing = BuildValidResponse(version, bytes, hash))
        {
            noSniffMissing.Headers.Remove("X-Content-Type-Options");
            Assert.False(ValidBillingDocument(noSniffMissing, version, bytes));
        }
        using (var badDigest = BuildValidResponse(version, bytes, new string('b', 64)))
            Assert.False(ValidBillingDocument(badDigest, version, bytes));
        using (var missingDigest = BuildValidResponse(version, bytes, hash))
        {
            missingDigest.Headers.Remove(BillingDocumentsV1.DigestHeader);
            Assert.False(ValidBillingDocument(missingDigest, version, bytes));
        }
        var nonPdf = "<html>not a pdf</html>"u8.ToArray();
        using (var nonPdfResponse = BuildValidResponse(version, nonPdf, P0611Tests.Hash(nonPdf)))
            Assert.False(ValidBillingDocument(nonPdfResponse, version, nonPdf));
    }

    [Fact]
    public async Task HandlerFailsClosedWhenStoredBytesDoNotMatchRecordedHash()
    {
        var account = Guid.NewGuid();
        var order = Guid.NewGuid();
        var version = Guid.NewGuid();
        var pdf = P0611Tests.Pdf();
        var tampered = pdf.ToArray();
        tampered[^2] = (byte)'X';
        var view = new BillingDocumentVersionView(account, order, Guid.NewGuid(), version, 1, null,
            BillingDocumentsV1.AccountingInvoice, "fixture.pdf", BillingDocumentsV1.MediaType, pdf.Length,
            P0611Tests.Hash(pdf), P0611Tests.Issuer, P0611Tests.Reference, P0611Tests.IssueDate,
            Guid.NewGuid(), Guid.NewGuid(), P0611Tests.Instant, P0611Tests.Instant);
        var handler = new ReadBillingDocumentHandler(new PassThroughDelegation(), new UnusedOrderStore(),
            new StubDocumentStore(new StoredBillingDocumentContent(view, tampered)));
        var proof = new VerifiedBillingDelegation(Guid.NewGuid(), account, BillingDocumentsV1.Download,
            Guid.NewGuid(), Guid.NewGuid(), "fixture", P0611Tests.Instant, P0611Tests.Instant.AddMinutes(5),
            "GET", BillingDocumentsV1.ContentPath(account, order, version), MerchantBillingDelegationV1.EmptyBodySha256);

        var result = await handler.ContentAsync(proof, account, order, version, CancellationToken.None);
        Assert.True(result.IsError);
        Assert.Equal("Billing.DocumentIntegrityFailed", result.FirstError.Code);
    }

    [Fact]
    public void FullDocumentListFitsRelayBoundAtMaximumContractValues()
    {
        var account = Guid.NewGuid();
        var order = Guid.NewGuid();
        var docId = Guid.NewGuid();
        var ids = Enumerable.Range(0, BillingDocumentsV1.PageSize).Select(_ => Guid.NewGuid()).ToArray();
        var rows = Enumerable.Range(0, BillingDocumentsV1.PageSize).Select(index =>
        {
            var version = BillingDocumentsV1.PageSize - index;
            Guid? supersedes = version == 1 ? null : ids[index + 1];
            return new BillingDocumentV1(
                account, order, docId, ids[index], version, supersedes, BillingDocumentsV1.AccountingInvoice,
                BillingDocumentsV1.MediaType, BillingDocumentsV1.MaxDocumentBytes, new string('a', 64),
                new string('店', BillingDocumentsV1.MaxIssuerNameLength),
                new string('R', BillingDocumentsV1.MaxIssuerReferenceLength),
                new DateOnly(2027, 3, 10), P0209Fixture.Utc("9998-12-31T23:59:59.999999Z"));
        }).ToArray();
        var list = new BillingDocumentListV1(account, order, rows);
        Assert.True(BillingDocumentsV1.IsValidList(list, account, order));
        Assert.True(BillingDocumentsV1.IsValidList(list, account, order, BillingDocumentsV1.PageSize + 1));
        Assert.False(BillingDocumentsV1.IsValidList(list, account, order, BillingDocumentsV1.PageSize));
        Assert.InRange(JsonSerializer.SerializeToUtf8Bytes(list, JsonSerializerOptions.Web).Length, 1, BillingDocumentsV1.MaxListBytes);
    }

    [Fact]
    public void ContractFixtureMatchesSharedConstantsAndNoInlinePdfSurfaceExists()
    {
        var fixturePath = Path.Combine(ProjectPaths.Root, "tests/contracts/licensing/billing-document-v1.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(fixturePath));
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(BillingDocumentsV1.Policy, root.GetProperty("policy").GetString());
        Assert.Equal(BillingDocumentsV1.List, root.GetProperty("actions").GetProperty("list").GetString());
        Assert.Equal(BillingDocumentsV1.Download, root.GetProperty("actions").GetProperty("download").GetString());
        Assert.Equal(BillingDocumentsV1.AccountingInvoice, root.GetProperty("kind").GetString());
        Assert.Equal(BillingDocumentsV1.MediaType, root.GetProperty("mediaType").GetString());
        Assert.Equal(BillingDocumentsV1.MaxDocumentBytes, root.GetProperty("maxDocumentBytes").GetInt32());
        Assert.Equal(BillingDocumentsV1.PageSize, root.GetProperty("pageSize").GetInt32());
        Assert.Equal(BillingDocumentsV1.MaxListBytes, root.GetProperty("maxListBytes").GetInt32());
        Assert.Equal(BillingDocumentsV1.MaxIssuerNameLength, root.GetProperty("maxIssuerNameLength").GetInt32());
        Assert.Equal(BillingDocumentsV1.MaxIssuerReferenceLength, root.GetProperty("maxIssuerReferenceLength").GetInt32());
        Assert.Equal(BillingDocumentsV1.EarliestIssueDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            root.GetProperty("earliestIssueDate").GetString());
        Assert.Equal(BillingDocumentsV1.NotFound, root.GetProperty("notFound").GetString());
        Assert.Equal(BillingDocumentsV1.DigestHeader, root.GetProperty("digestHeader").GetString());
        Assert.Equal("/internal/licensing/v1/customer/accounts/{account}/orders/{order}/billing-documents/before/{beforeVersion}",
            root.GetProperty("listBeforePathTemplate").GetString());
        var sample = root.GetProperty("sample").Deserialize<BillingDocumentListV1>(JsonSerializerOptions.Web)!;
        Assert.True(BillingDocumentsV1.IsValidList(sample, sample.AccountId, sample.OrderId));

        var uiDir = Path.Combine(ProjectPaths.Root, "src/frontend/src/app/features/billing/documents");
        var sources = Directory.EnumerateFiles(uiDir, "*.*", SearchOption.AllDirectories)
            .Where(path => (path.EndsWith(".ts", StringComparison.Ordinal) || path.EndsWith(".html", StringComparison.Ordinal))
                && !path.EndsWith(".spec.ts", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(sources);
        foreach (var path in sources)
        {
            var source = File.ReadAllText(path);
            foreach (var forbidden in new[] { "openPdf", "<iframe", "<embed", "<object", "bypassSecurityTrust" })
                Assert.DoesNotContain(forbidden, source, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static HttpResponseMessage BuildValidResponse(Guid version, byte[] bytes, string digest)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(BillingDocumentsV1.MediaType);
        response.Content.Headers.ContentLength = bytes.Length;
        response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
        {
            FileName = BillingDocumentsV1.FileName(version),
        };
        response.Headers.CacheControl = new CacheControlHeaderValue { NoStore = true };
        response.Headers.Add("X-Content-Type-Options", "nosniff");
        response.Headers.Add(BillingDocumentsV1.DigestHeader, digest);
        return response;
    }

    private static bool ValidBillingDocument(HttpResponseMessage response, Guid version, byte[] bytes) =>
        (bool)typeof(ApplicationDbContext).Assembly.GetType("Intelibill.Infrastructure.Billing.MerchantBillingClient")!
            .GetMethod("IsValidBillingDocument", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [response, version, bytes])!;

    private sealed class PassThroughDelegation : IBillingDelegationAccess
    {
        public Task<ErrorOr<BillingAccountAccessV1>> AuthorizeAsync(VerifiedBillingDelegation delegation, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<ErrorOr<T>> ExecuteAsync<T>(VerifiedBillingDelegation delegation,
            Func<IBillingDelegationScope, CancellationToken, Task<ErrorOr<T>>> operation, CancellationToken ct) =>
            operation(new StubScope(delegation.ActorId), ct);
        public Task<ErrorOr<T>> ExecutePairAsync<T>(VerifiedBillingDelegation delegation,
            Func<CancellationToken, Task<Guid?>> counterparty,
            Func<IBillingDelegationScope, CancellationToken, Task<ErrorOr<T>>> operation, CancellationToken ct) =>
            throw new NotSupportedException();
        private sealed class StubScope(Guid actor) : IBillingDelegationScope
        {
            public Guid ActorId => actor;
            public Task UseAccountAsync(Guid accountId, CancellationToken ct) => Task.CompletedTask;
        }
    }

    private sealed class StubDocumentStore(StoredBillingDocumentContent? stored) : IBillingDocumentStore
    {
        public Task<BillingDocumentVersionView[]> ListAsync(Guid account, Guid order, int? beforeVersion, int limit,
            CancellationToken ct) =>
            Task.FromResult(Array.Empty<BillingDocumentVersionView>());
        public Task<BillingDocumentVersionView?> FindVersionAsync(Guid account, Guid order, Guid version, CancellationToken ct) =>
            Task.FromResult<BillingDocumentVersionView?>(null);
        public Task<BillingDocumentVersionView?> FindLatestAsync(Guid account, Guid order, CancellationToken ct) =>
            Task.FromResult<BillingDocumentVersionView?>(null);
        public Task<StoredBillingDocumentContent?> FindContentAsync(Guid account, Guid order, Guid version, CancellationToken ct) =>
            Task.FromResult(stored);
        public Task<StoredBillingDocument?> FindOperationAsync(Guid account, Guid key, CancellationToken ct) =>
            Task.FromResult<StoredBillingDocument?>(null);
        public Task<int> CountConfirmationsAsync(Guid account, Guid order, CancellationToken ct) => Task.FromResult(0);
        public Task<bool> InsertAsync(StoredBillingDocument document, Guid session, byte[] content, CancellationToken ct) =>
            Task.FromResult(false);
    }

    private sealed class UnusedOrderStore : ICheckoutOrderStore
    {
        public Task<CoverageReplacementOrderV1?> ReplacementAsync(Guid account, Guid term, int capacity, int allowance,
            DateTimeOffset boundary, DateTimeOffset now, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> IncreaseStillCurrentAsync(CheckoutQuoteV1 quote, DateTimeOffset now, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<bool> IsIncreaseOwnerAsync(Guid account, Guid order, Guid actor, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<bool> ReductionOrderCurrentAsync(CheckoutQuoteV1 quote, DateTimeOffset now, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<bool> IsReductionOwnerAsync(Guid account, Guid order, Guid actor, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<bool> HasConflictingPaidCoverageAsync(Guid account, Guid[] shops, Guid? excludeTerm,
            DateTimeOffset startsAt, DateTimeOffset endsAt, CancellationToken ct) => throw new NotSupportedException();
        public Task<StoredCheckoutQuote?> FindAsync(Guid account, Guid id, bool byOperation, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<CheckoutSource?> SourceAsync(Guid account, Guid? term, Guid[] requested, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task SaveAsync(CheckoutQuoteV1 quote, CheckoutSource source, Guid actor, Guid key, string requestHash,
            Guid replay, CancellationToken ct) => throw new NotSupportedException();
        public Task<CheckoutShopPageV1> PageAsync(Guid account, Guid? order, Guid? after, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<ReviewSourceShop[]> ReviewShopsAsync(Guid account, Guid order, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<ReviewCancellationWithdrawal[]> WithdrawnRevisionsAsync(Guid account, Guid term, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<Guid[]> ShopIdsAsync(Guid account, Guid order, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<StoredCheckoutAcceptance?> FindAcceptanceAsync(Guid account, Guid key, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<int> AcceptAsync(CheckoutQuoteV1 quote, CheckoutAcceptanceV1 acceptance, Guid actor, Guid request,
            Guid replay, string requestHash, CancellationToken ct) => throw new NotSupportedException();
    }
}
