using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;
using Intelibill.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

/// <summary>
/// The reconciliation console against real PostgreSQL: case reads, the allowlisted resolution
/// actions, their idempotency and row-version checks, and tenant isolation.
/// </summary>
[Collection("Integration Tests")]
public sealed class OfflineReconciliationControllerTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
{
    private const string Device = "device-1";
    private const string Route = "/api/offline-reconciliation";

    // The web client's JSON.stringify output for the invoices array: camelCase, enums as numbers.
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ApiWebApplicationFactory _factory = new(fixture);

    public async Task InitializeAsync() => await _factory.InitializeAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _factory.Dispose();
        GC.SuppressFinalize(this);
    }

    // RF3: printed 5 / consumed 3 is closed only by procurement evidence, never by acknowledgment.
    [Fact]
    public async Task StockShortage_ResolutionLinksCorrection_ClosesBatchAtZero()
    {
        using var client = CreateClient();
        var owner = await CreateOwnerAsync(client);
        var stock = await AddInventoryAsync(client, owner.Token, 3m);
        var invoice = await FirstLeasedInvoiceNumberAsync(client, owner.Token);
        var clientSaleId = NewClientSaleId();

        var synced = await SyncAsync(client, owner.Token, Payload(clientSaleId, invoice, stock, 5m, DateTimeOffset.UtcNow));
        Assert.Equal("SyncedWithWarnings", synced.GetProperty("status").GetString());
        var saleId = synced.GetProperty("saleId").GetGuid();

        var caseId = await FindCaseIdAsync(client, owner.Token, clientSaleId, "StockVariance");
        var detail = await GetCaseAsync(client, owner.Token, caseId);
        Assert.Equal(5m, detail.GetProperty("printedQuantity").GetDecimal());
        Assert.Equal(3m, detail.GetProperty("consumedQuantity").GetDecimal());
        Assert.Equal(2m, detail.GetProperty("unresolvedQuantity").GetDecimal());
        Assert.Equal(saleId, detail.GetProperty("saleId").GetGuid());
        Assert.Equal(invoice, detail.GetProperty("invoiceNumber").GetString());
        Assert.Equal(["LinkStockCorrection", "Escalate"], Strings(detail.GetProperty("availableActions")));
        var version = detail.GetProperty("version").GetUInt32();

        var acknowledged = await ResolveAsync(client, owner.Token, caseId, version, "AcknowledgeVariance", "Looks fine");
        await AssertProblemAsync(acknowledged, HttpStatusCode.BadRequest, Errors.Reconciliation.ActionNotAllowed.Code);

        var recount = await CreateAdjustmentAsync(client, owner.Token, stock.BatchId, InventoryAdjustmentReason.StockCountCorrection, 2m, "Recounted the shelf");
        var countLinked = await ResolveAsync(client, owner.Token, caseId, version, "LinkStockCorrection", "Recount shows two more", recount.Id);
        await AssertProblemAsync(countLinked, HttpStatusCode.BadRequest, Errors.Reconciliation.LinkedAdjustmentInvalid.Code);
        await VoidAdjustmentAsync(client, owner.Token, recount.Id, "A count is not procurement evidence");

        var found = await CreateAdjustmentAsync(client, owner.Token, stock.BatchId, InventoryAdjustmentReason.FoundStock, 2m, "Two found in the back room");
        var linked = await ResolveAsync(client, owner.Token, caseId, version, "LinkStockCorrection", "Found stock covers the shortage", found.Id);

        Assert.Equal(HttpStatusCode.OK, linked.StatusCode);
        var resolved = await linked.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Resolved", resolved.GetProperty("status").GetString());
        Assert.Equal("LinkStockCorrection", resolved.GetProperty("resolution").GetString());
        Assert.Equal(found.Id, resolved.GetProperty("linkedOperationId").GetGuid());
        Assert.Empty(resolved.GetProperty("availableActions").EnumerateArray());
        var linkEvent = Assert.Single(resolved.GetProperty("events").EnumerateArray());
        Assert.Equal("LinkStockCorrection", linkEvent.GetProperty("action").GetString());
        Assert.Equal("Open", linkEvent.GetProperty("fromStatus").GetString());
        Assert.Equal("Resolved", linkEvent.GetProperty("toStatus").GetString());
        Assert.Equal(owner.UserId, linkEvent.GetProperty("actor").GetProperty("userId").GetGuid());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(0m, (await db.InventoryBatches.SingleAsync(b => b.Id == stock.BatchId)).Quantity);
        Assert.Equal(0m, (await db.Inventory.SingleAsync(i => i.ItemId == stock.ItemId)).Quantity);
        var movements = await db.StockTransactions.Where(t => t.InventoryBatchId == stock.BatchId).ToListAsync();
        var original = Assert.Single(movements, t => t.TransactionType == StockTransactionType.Out && t.Quantity == -3m);
        Assert.Equal(invoice, original.ReferenceNumber);
        Assert.Single(movements, t => t.TransactionType == StockTransactionType.In && t.Quantity == 2m && t.ReferenceNumber == found.Number);
        var catchUp = Assert.Single(movements, t => t.TransactionType == StockTransactionType.Out && t.Quantity == -2m);
        Assert.Equal(invoice, catchUp.ReferenceNumber);
        Assert.Equal(owner.UserId, catchUp.CreatedBy);
        var issue = await db.ReconciliationIssues.SingleAsync(i => i.Id == caseId);
        Assert.Equal(catchUp.Id, issue.ResolutionStockTransactionId);
        Assert.Equal(1, await db.ReconciliationCaseEvents.CountAsync(e => e.CaseId == caseId && e.Action == ReconciliationAction.LinkStockCorrection));
        Assert.Equal(1, await db.Sales.CountAsync(s => s.InvoiceNumber == invoice));
    }

    // RF5: two reviewers holding the same version — one effect, one conflict, and a resend replays.
    [Fact]
    public async Task ConcurrentResolution_HasOneEffect()
    {
        using var client = CreateClient();
        var owner = await CreateOwnerAsync(client);
        var stock = await AddInventoryAsync(client, owner.Token, 3m);
        var invoice = await FirstLeasedInvoiceNumberAsync(client, owner.Token);
        var clientSaleId = NewClientSaleId();
        await SyncAsync(client, owner.Token, Payload(clientSaleId, invoice, stock, 5m, DateTimeOffset.UtcNow));
        var caseId = await FindCaseIdAsync(client, owner.Token, clientSaleId, "StockVariance");
        var version = (await GetCaseAsync(client, owner.Token, caseId)).GetProperty("version").GetUInt32();
        var found = await CreateAdjustmentAsync(client, owner.Token, stock.BatchId, InventoryAdjustmentReason.FoundStock, 2m, "Supplier delivered two late");

        using var first = CreateClient();
        using var second = CreateClient();
        var firstOperation = $"resolve-{Guid.NewGuid():N}";
        var secondOperation = $"resolve-{Guid.NewGuid():N}";
        var responses = await Task.WhenAll(
            ResolveAsync(first, owner.Token, caseId, version, "LinkStockCorrection", "Reviewer one", found.Id, firstOperation),
            ResolveAsync(second, owner.Token, caseId, version, "LinkStockCorrection", "Reviewer two", found.Id, secondOperation));

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], responses.Select(r => r.StatusCode).OrderBy(s => (int)s));
        var winnerIndex = responses[0].StatusCode == HttpStatusCode.OK ? 0 : 1;
        var loser = await responses[1 - winnerIndex].Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(
            loser.GetProperty("title").GetString(),
            new[] { Errors.Reconciliation.CaseVersionConflict.Code, Errors.Reconciliation.CaseAlreadyResolved.Code });
        var winnerBody = await responses[winnerIndex].Content.ReadAsStringAsync();

        var replay = await ResolveAsync(
            client,
            owner.Token,
            caseId,
            version,
            "LinkStockCorrection",
            winnerIndex == 0 ? "Reviewer one" : "Reviewer two",
            found.Id,
            winnerIndex == 0 ? firstOperation : secondOperation);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(winnerBody, await replay.Content.ReadAsStringAsync());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.StockTransactions.CountAsync(t => t.InventoryBatchId == stock.BatchId && t.TransactionType == StockTransactionType.Out && t.Quantity == -2m));
        Assert.Equal(0m, (await db.InventoryBatches.SingleAsync(b => b.Id == stock.BatchId)).Quantity);
        Assert.Equal(1, await db.ReconciliationCaseEvents.CountAsync(e => e.CaseId == caseId));
        foreach (var response in responses)
            response.Dispose();
    }

    // RF5: another tenant's case does not exist for the caller.
    [Fact]
    public async Task OtherShop_CannotReadCase()
    {
        using var client = CreateClient();
        var owner = await CreateOwnerAsync(client);
        var stock = await AddInventoryAsync(client, owner.Token, 3m);
        var invoice = await FirstLeasedInvoiceNumberAsync(client, owner.Token);
        var clientSaleId = NewClientSaleId();
        await SyncAsync(client, owner.Token, Payload(clientSaleId, invoice, stock, 5m, DateTimeOffset.UtcNow));
        var caseId = await FindCaseIdAsync(client, owner.Token, clientSaleId, "StockVariance");
        var version = (await GetCaseAsync(client, owner.Token, caseId)).GetProperty("version").GetUInt32();

        var intruder = await CreateOwnerAsync(client);

        using var read = await SendAsync(client, HttpMethod.Get, $"{Route}/{caseId}", intruder.Token);
        await AssertProblemAsync(read, HttpStatusCode.NotFound, Errors.Reconciliation.CaseNotFound.Code);
        var resolve = await ResolveAsync(client, intruder.Token, caseId, version, "Escalate", "Not mine to touch");
        await AssertProblemAsync(resolve, HttpStatusCode.NotFound, Errors.Reconciliation.CaseNotFound.Code);
        var page = await ListAsync(client, intruder.Token, "all");
        Assert.DoesNotContain(page.GetProperty("items").EnumerateArray(), i => i.GetProperty("caseId").GetGuid() == caseId);
        Assert.Equal(0, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, page.GetProperty("unresolvedCount").GetInt32());

        var unchanged = await GetCaseAsync(client, owner.Token, caseId);
        Assert.Equal("Open", unchanged.GetProperty("status").GetString());
        Assert.Equal(version, unchanged.GetProperty("version").GetUInt32());
    }

    // A person id the client asserts is named only through a membership in the case's shop.
    [Fact]
    public async Task ForeignAssertedIssuer_IsNotNamed()
    {
        using var client = CreateClient();
        var owner = await CreateOwnerAsync(client);
        var stock = await AddInventoryAsync(client, owner.Token, 1m);
        var lease = await ReserveInvoiceLeaseAsync(client, owner.Token);
        var staff = await AddStaffAsync(client, owner);
        var foreign = await CreateOwnerAsync(client);

        var clientSaleId = NewClientSaleId();
        await SyncAsync(client, staff.Token, Payload(clientSaleId, InvoiceAt(lease, 0), stock, 2m, DateTimeOffset.UtcNow, foreign.UserId));

        string foreignEmail, foreignPhone;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            foreignPhone = "+919876501234";
            await db.Users.IgnoreQueryFilters().Where(u => u.Id == foreign.UserId)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.PhoneNumber, foreignPhone));
            foreignEmail = (await db.Users.IgnoreQueryFilters().AsNoTracking().SingleAsync(u => u.Id == foreign.UserId)).Email!;
        }

        var page = await ListAsync(client, staff.Token, "open");
        var summary = page.GetProperty("items").EnumerateArray().First(i => i.GetProperty("clientSaleId").GetString() == clientSaleId);
        var caseId = summary.GetProperty("caseId").GetGuid();

        using var response = await SendAsync(client, HttpMethod.Get, $"{Route}/{caseId}", staff.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Olive Owner", raw);
        Assert.DoesNotContain(foreignEmail, raw);
        Assert.DoesNotContain(foreignPhone, raw);

        var detail = JsonDocument.Parse(raw).RootElement;
        var submission = detail.GetProperty("submission");
        Assert.Equal(foreign.UserId, submission.GetProperty("assertedIssuer").GetProperty("userId").GetGuid());
        Assert.Equal(string.Empty, submission.GetProperty("assertedIssuer").GetProperty("displayName").GetString());
        Assert.Equal("Sam Staff", submission.GetProperty("submittedBy").GetProperty("displayName").GetString());
        Assert.Equal("Sam Staff", detail.GetProperty("createdBy").GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task Staff_CanEscalateOwnCase_ButNotAcknowledge()
    {
        using var client = CreateClient();
        var owner = await CreateOwnerAsync(client);
        var stock = await AddInventoryAsync(client, owner.Token, 1m);
        var lease = await ReserveInvoiceLeaseAsync(client, owner.Token);
        var staff = await AddStaffAsync(client, owner);

        var staffSaleId = NewClientSaleId();
        var staffSync = await SyncAsync(client, staff.Token, Payload(staffSaleId, InvoiceAt(lease, 0), stock, 2m, DateTimeOffset.UtcNow));
        Assert.Equal("SyncedWithWarnings", staffSync.GetProperty("status").GetString());
        var ownerSaleId = NewClientSaleId();
        await SyncAsync(client, owner.Token, Payload(ownerSaleId, InvoiceAt(lease, 1), stock, 1m, DateTimeOffset.UtcNow));

        var staffPage = await ListAsync(client, staff.Token, "open");
        var staffCase = Assert.Single(staffPage.GetProperty("items").EnumerateArray());
        Assert.Equal(staffSaleId, staffCase.GetProperty("clientSaleId").GetString());
        Assert.Equal(staff.UserId, staffCase.GetProperty("createdBy").GetProperty("userId").GetGuid());
        Assert.Equal(1, staffPage.GetProperty("unresolvedCount").GetInt32());
        var ownerPage = await ListAsync(client, owner.Token, "open");
        Assert.Equal(2, ownerPage.GetProperty("totalCount").GetInt32());
        var ownerCaseId = ownerPage.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("clientSaleId").GetString() == ownerSaleId)
            .GetProperty("caseId").GetGuid();

        using var othersCase = await SendAsync(client, HttpMethod.Get, $"{Route}/{ownerCaseId}", staff.Token);
        await AssertProblemAsync(othersCase, HttpStatusCode.NotFound, Errors.Reconciliation.CaseNotFound.Code);

        var caseId = staffCase.GetProperty("caseId").GetGuid();
        var detail = await GetCaseAsync(client, staff.Token, caseId);
        Assert.Equal(["Escalate"], Strings(detail.GetProperty("availableActions")));
        var version = detail.GetProperty("version").GetUInt32();

        var acknowledged = await ResolveAsync(client, staff.Token, caseId, version, "AcknowledgeVariance", "It is fine");
        await AssertProblemAsync(acknowledged, HttpStatusCode.Forbidden, Errors.Reconciliation.ActionForbidden.Code);

        var escalated = await ResolveAsync(client, staff.Token, caseId, version, "Escalate", "Needs the owner");
        Assert.Equal(HttpStatusCode.OK, escalated.StatusCode);
        var body = await escalated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Escalated", body.GetProperty("status").GetString());
        Assert.Empty(body.GetProperty("availableActions").EnumerateArray());
        Assert.NotEqual(version, body.GetProperty("version").GetUInt32());

        var afterStaff = await ListAsync(client, staff.Token, "open");
        Assert.Equal(1, afterStaff.GetProperty("unresolvedCount").GetInt32());
        Assert.Equal("Escalated", Assert.Single(afterStaff.GetProperty("items").EnumerateArray()).GetProperty("status").GetString());
        Assert.Single((await ListAsync(client, staff.Token, "escalated")).GetProperty("items").EnumerateArray());
        Assert.Equal(2, (await ListAsync(client, owner.Token, "open")).GetProperty("unresolvedCount").GetInt32());
        var ownerView = await GetCaseAsync(client, owner.Token, caseId);
        Assert.Equal(["LinkStockCorrection"], Strings(ownerView.GetProperty("availableActions")));
        // The committed answer carries the version the next resolution must present.
        Assert.Equal(body.GetProperty("version").GetUInt32(), ownerView.GetProperty("version").GetUInt32());
        var trail = Assert.Single(ownerView.GetProperty("events").EnumerateArray());
        Assert.Equal("Escalate", trail.GetProperty("action").GetString());
        Assert.Equal("Sam Staff", trail.GetProperty("actor").GetProperty("displayName").GetString());
    }

    // RF1: a lease that expired before the server first saw the invoice — reviewed, then posted once under its number.
    [Fact]
    public async Task ReviewedHistoricalSubmission_PreservesNumberAndPostsOnce()
    {
        using var client = CreateClient();
        var owner = await CreateOwnerAsync(client);
        var stock = await AddInventoryAsync(client, owner.Token, 10m);
        var lease = await ReserveInvoiceLeaseAsync(client, owner.Token);
        var invoice = InvoiceAt(lease, 0);
        var soldAt = DateTimeOffset.UtcNow.AddDays(-6);
        await ExpireLeaseAsync(lease.GetProperty("leaseId").GetGuid(), soldAt);
        var clientSaleId = NewClientSaleId();
        var payload = Payload(clientSaleId, invoice, stock, 1m, soldAt);

        var held = await SyncAsync(client, owner.Token, payload);
        Assert.Equal("NeedsReview", held.GetProperty("status").GetString());
        Assert.Equal(Errors.OfflineReconciliation.HistoricalLeaseReviewRequired.Code, held.GetProperty("errors")[0].GetProperty("code").GetString());
        var caseId = held.GetProperty("reviewCaseId").GetGuid();

        var detail = await GetCaseAsync(client, owner.Token, caseId);
        Assert.Equal("IssueTimingReview", detail.GetProperty("issueType").GetString());
        Assert.Equal([Errors.OfflineReconciliation.HistoricalLeaseReviewRequired.Code], Strings(detail.GetProperty("reasonCodes")));
        Assert.True(detail.GetProperty("hasOriginalEnvelope").GetBoolean());
        var submission = detail.GetProperty("submission");
        Assert.Equal("Sync", submission.GetProperty("source").GetString());
        Assert.Equal(owner.UserId, submission.GetProperty("submittedBy").GetProperty("userId").GetGuid());
        Assert.Equal(lease.GetProperty("leaseId").GetGuid(), submission.GetProperty("originalLeaseId").GetGuid());
        var snapshot = submission.GetProperty("invoice");
        Assert.Equal(invoice, snapshot.GetProperty("invoiceNumber").GetString());
        Assert.Equal(118m, snapshot.GetProperty("totalAmount").GetDecimal());
        Assert.Equal("Cash", snapshot.GetProperty("paymentMethod").GetString());
        var line = Assert.Single(snapshot.GetProperty("lines").EnumerateArray());
        Assert.Equal(1m, line.GetProperty("quantity").GetDecimal());
        Assert.Equal("B-001", line.GetProperty("batchNumber").GetString());
        Assert.Equal(["RetryHistoricalLease", "LinkExistingSale", "Escalate"], Strings(detail.GetProperty("availableActions")));

        var retried = await ResolveAsync(client, owner.Token, caseId, detail.GetProperty("version").GetUInt32(), "RetryHistoricalLease", "Cashier was offline all week");
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        var resolved = await retried.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Resolved", resolved.GetProperty("status").GetString());
        Assert.Equal("RetryHistoricalLease", resolved.GetProperty("resolution").GetString());
        var saleId = resolved.GetProperty("saleId").GetGuid();

        var resent = await SyncAsync(client, owner.Token, payload);
        Assert.Equal("duplicate", resent.GetProperty("status").GetString());
        Assert.Equal(saleId, resent.GetProperty("saleId").GetGuid());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sale = await db.Sales.SingleAsync(s => s.ShopId == owner.ShopId && s.InvoiceNumber == invoice);
        Assert.Equal(saleId, sale.Id);
        Assert.Equal(clientSaleId, sale.ClientSaleId);
        Assert.Equal(owner.UserId, sale.ActorUserId);
        var envelope = await db.OfflineSubmissions.SingleAsync(s => s.ClientSaleId == clientSaleId);
        Assert.Equal(saleId, envelope.SaleId);
        Assert.Equal(envelope.PayloadHash, sale.RequestHash);
        // The jsonb round trip must reproduce the stored hash, or a resend would conflict instead of replaying.
        var stored = OfflineSubmissionPayload.Deserialize(envelope.PayloadJson)!;
        Assert.Equal(envelope.PayloadHash, OfflineSaleSyncIdempotencyHasher.ComputeHash(owner.ShopId, Device, stored));
        Assert.Equal(9m, (await db.InventoryBatches.SingleAsync(b => b.Id == stock.BatchId)).Quantity);
        Assert.Equal(1, await db.StockTransactions.CountAsync(t => t.InventoryBatchId == stock.BatchId && t.TransactionType == StockTransactionType.Out));
        var issue = await db.ReconciliationIssues.SingleAsync(i => i.Id == caseId);
        Assert.Equal(saleId, issue.SaleId);
        Assert.Equal(ReconciliationCaseStatus.Resolved, issue.Status);
    }

    // RF2: a changed resend is kept as evidence; only an authorized link closes it, and the sale never changes.
    [Fact]
    public async Task ConflictingEnvelope_RetainedAndLinkedToOriginal()
    {
        using var client = CreateClient();
        var owner = await CreateOwnerAsync(client);
        var stock = await AddInventoryAsync(client, owner.Token, 10m);
        var invoice = await FirstLeasedInvoiceNumberAsync(client, owner.Token);
        var clientSaleId = NewClientSaleId();
        var soldAt = DateTimeOffset.UtcNow;

        var created = await SyncAsync(client, owner.Token, Payload(clientSaleId, invoice, stock, 1m, soldAt));
        Assert.Equal("created", created.GetProperty("status").GetString());
        var saleId = created.GetProperty("saleId").GetGuid();

        var conflicting = await SyncAsync(client, owner.Token, Payload(clientSaleId, invoice, stock, 2m, soldAt));
        Assert.Equal("NeedsReview", conflicting.GetProperty("status").GetString());
        Assert.Equal(Errors.Sale.IdempotencyConflict.Code, conflicting.GetProperty("errors")[0].GetProperty("code").GetString());
        var caseId = conflicting.GetProperty("reviewCaseId").GetGuid();

        var detail = await GetCaseAsync(client, owner.Token, caseId);
        Assert.Equal(saleId, detail.GetProperty("saleId").GetGuid());
        Assert.Equal(236m, detail.GetProperty("submission").GetProperty("invoice").GetProperty("totalAmount").GetDecimal());
        Assert.Equal(["LinkExistingSale", "Escalate"], Strings(detail.GetProperty("availableActions")));

        var linked = await ResolveAsync(client, owner.Token, caseId, detail.GetProperty("version").GetUInt32(), "LinkExistingSale", "The first upload is the printed bill", saleId);
        Assert.Equal(HttpStatusCode.OK, linked.StatusCode);
        var resolved = await linked.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Resolved", resolved.GetProperty("status").GetString());
        Assert.Equal(saleId, resolved.GetProperty("linkedOperationId").GetGuid());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sale = await db.Sales.SingleAsync(s => s.ClientSaleId == clientSaleId);
        Assert.Equal(118m, sale.TotalAmount);
        var envelopes = await db.OfflineSubmissions.Where(s => s.ClientSaleId == clientSaleId).ToListAsync();
        Assert.Equal(2, envelopes.Count);
        Assert.Equal(saleId, envelopes.Single(e => e.PayloadHash == sale.RequestHash).SaleId);
        Assert.Null(envelopes.Single(e => e.PayloadHash != sale.RequestHash).SaleId);
        Assert.Equal(9m, (await db.InventoryBatches.SingleAsync(b => b.Id == stock.BatchId)).Quantity);
    }

    // A removed cashier's unsynced invoice comes back through the owner: reviewed, posted once as the cashier's, access never restored.
    [Fact]
    public async Task RevokedCashierBundle_OwnerRecoveryPreservesIssuerAndPostsOnce()
    {
        using var client = CreateClient();
        var owner = await CreateOwnerAsync(client);
        var stock = await AddInventoryAsync(client, owner.Token, 10m);
        var lease = await ReserveInvoiceLeaseAsync(client, owner.Token);
        var staff = await AddStaffAsync(client, owner);

        // The cashier's earlier sync from the device is the device history that verifies them later.
        var synced = await SyncAsync(client, staff.Token, Payload(NewClientSaleId(), InvoiceAt(lease, 0), stock, 1m, DateTimeOffset.UtcNow, staff.UserId));
        Assert.Equal("created", synced.GetProperty("status").GetString());
        await RemoveMembershipAsync(client, owner, staff.UserId);

        var clientSaleId = NewClientSaleId();
        var invoice = InvoiceAt(lease, 1);
        var invoicesJson = InvoicesJson(Payload(clientSaleId, invoice, stock, 2m, DateTimeOffset.UtcNow.AddMinutes(-30), staff.UserId));
        var operationId = $"recovery-{Guid.NewGuid():N}";

        using var submitted = await SubmitBundleAsync(client, owner.Token, owner.ShopId, invoicesJson, operationId);
        var body = await ReadRecoveryAsync(submitted);
        Assert.Equal(operationId, body.GetProperty("operationId").GetString());
        var row = Assert.Single(body.GetProperty("results").EnumerateArray());
        Assert.Equal("ReviewOpened", row.GetProperty("status").GetString());
        Assert.Equal(clientSaleId, row.GetProperty("clientSaleId").GetString());
        Assert.Equal(invoice, row.GetProperty("invoiceNumber").GetString());
        Assert.Equal(Errors.OfflineReconciliation.RecoveryReviewRequired.Code, row.GetProperty("errors")[0].GetProperty("code").GetString());
        var caseId = row.GetProperty("caseId").GetGuid();

        using (var replayed = await SubmitBundleAsync(client, owner.Token, owner.ShopId, invoicesJson, operationId))
            Assert.Equal(caseId, (await ReadRecoveryAsync(replayed)).GetProperty("results")[0].GetProperty("caseId").GetGuid());

        var detail = await GetCaseAsync(client, owner.Token, caseId);
        Assert.Equal("RecoveryReview", detail.GetProperty("issueType").GetString());
        Assert.Equal("Open", detail.GetProperty("status").GetString());
        Assert.Equal([Errors.OfflineReconciliation.RecoveryReviewRequired.Code], Strings(detail.GetProperty("reasonCodes")));
        var submission = detail.GetProperty("submission");
        Assert.Equal("Recovery", submission.GetProperty("source").GetString());
        Assert.Equal(staff.UserId, submission.GetProperty("originalIssuer").GetProperty("userId").GetGuid());
        Assert.Equal("Sam Staff", submission.GetProperty("originalIssuer").GetProperty("displayName").GetString());
        Assert.Equal(owner.UserId, submission.GetProperty("submittedBy").GetProperty("userId").GetGuid());
        Assert.Equal(operationId, submission.GetProperty("recoveryOperationId").GetString());
        Assert.Equal(lease.GetProperty("leaseId").GetGuid(), submission.GetProperty("originalLeaseId").GetGuid());
        Assert.Equal(["RetryHistoricalLease", "LinkExistingSale", "Escalate"], Strings(detail.GetProperty("availableActions")));

        var retried = await ResolveAsync(client, owner.Token, caseId, detail.GetProperty("version").GetUInt32(), "RetryHistoricalLease", "Recovered from the retired till");
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        var saleId = (await retried.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("saleId").GetGuid();

        using var resubmitted = await SubmitBundleAsync(client, owner.Token, owner.ShopId, invoicesJson);
        var again = Assert.Single((await ReadRecoveryAsync(resubmitted)).GetProperty("results").EnumerateArray());
        Assert.Equal("AlreadyPosted", again.GetProperty("status").GetString());
        Assert.Equal(saleId, again.GetProperty("saleId").GetGuid());

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var sale = await db.Sales.SingleAsync(s => s.ShopId == owner.ShopId && s.ClientSaleId == clientSaleId);
            Assert.Equal(saleId, sale.Id);
            Assert.Equal(invoice, sale.InvoiceNumber);
            Assert.Equal(staff.UserId, sale.ActorUserId);
            var movement = await db.StockTransactions.SingleAsync(t => t.ShopId == owner.ShopId && t.ReferenceNumber == invoice);
            Assert.Equal(-2m, movement.Quantity);
            Assert.Equal(owner.UserId, movement.CreatedBy);
            Assert.Equal(staff.UserId, movement.PerformedBy);
            Assert.Equal(7m, (await db.InventoryBatches.SingleAsync(b => b.Id == stock.BatchId)).Quantity);
            var envelope = await db.OfflineSubmissions.SingleAsync(s => s.ClientSaleId == clientSaleId);
            Assert.Equal(OfflineSubmissionSource.Recovery, envelope.Source);
            Assert.Equal(saleId, envelope.SaleId);
            Assert.Equal(MembershipStatus.Removed, (await db.ShopMemberships.AsNoTracking().SingleAsync(m => m.ShopId == owner.ShopId && m.UserId == staff.UserId)).Status);
        }

        // Recovery never restores the cashier's access.
        using var staffRead = await SendAsync(client, HttpMethod.Get, $"{Route}?status=open", staff.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, staffRead.StatusCode);
    }

    [Fact]
    public async Task RecoveryBundle_ForgedIssuerOrForeignLease_Rejected()
    {
        using var client = CreateClient();
        var owner = await CreateOwnerAsync(client);
        var stock = await AddInventoryAsync(client, owner.Token, 10m);
        var lease = await ReserveInvoiceLeaseAsync(client, owner.Token);
        var otherDeviceLease = await ReserveInvoiceLeaseAsync(client, owner.Token, "device-2");
        var staff = await AddStaffAsync(client, owner);
        var foreign = await CreateOwnerAsync(client);

        var forgedId = NewClientSaleId();
        var foreignLeaseId = NewClientSaleId();
        var invoicesJson = InvoicesJson(
            Payload(forgedId, InvoiceAt(lease, 0), stock, 1m, DateTimeOffset.UtcNow, foreign.UserId),
            Payload(foreignLeaseId, InvoiceAt(otherDeviceLease, 0), stock, 1m, DateTimeOffset.UtcNow, owner.UserId));

        using var submitted = await SubmitBundleAsync(client, owner.Token, owner.ShopId, invoicesJson);
        var results = (await ReadRecoveryAsync(submitted)).GetProperty("results");
        Assert.Equal(2, results.GetArrayLength());
        Assert.Equal("Rejected", results[0].GetProperty("status").GetString());
        Assert.Equal(Errors.Recovery.IssuerMismatch.Code, results[0].GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Equal(JsonValueKind.Null, results[0].GetProperty("caseId").ValueKind);
        Assert.Equal("Rejected", results[1].GetProperty("status").GetString());
        Assert.Equal(Errors.Recovery.LeaseNotOwned.Code, results[1].GetProperty("errors")[0].GetProperty("code").GetString());

        // The invoices text no longer matches the checksum computed over what was exported.
        var checksum = Checksum(invoicesJson);
        var edited = invoicesJson.Replace("\"quantity\":1", "\"quantity\":3", StringComparison.Ordinal);
        Assert.NotEqual(invoicesJson, edited);
        using var tampered = await SubmitBundleAsync(client, owner.Token, owner.ShopId, edited, checksum: checksum);
        await AssertProblemAsync(tampered, HttpStatusCode.BadRequest, Errors.Recovery.BundleChecksumMismatch.Code);

        using var otherShop = await SubmitBundleAsync(client, owner.Token, foreign.ShopId, invoicesJson);
        await AssertProblemAsync(otherShop, HttpStatusCode.BadRequest, Errors.Recovery.BundleShopMismatch.Code);

        using var missingChecksum = await PostRecoveryAsync(
            client,
            owner.Token,
            $$$"""{"operationId":"recovery-{{{Guid.NewGuid():N}}}","bundle":{"format":"intelibill.offline-recovery","version":1,"shopId":"{{{owner.ShopId}}}","deviceId":"{{{Device}}}","exportedAt":"{{{DateTimeOffset.UtcNow:O}}}","invoices":{{{invoicesJson}}}}}""");
        await AssertProblemAsync(missingChecksum, HttpStatusCode.BadRequest, Errors.Recovery.BundleInvalid.Code);

        using var staffCaller = await SubmitBundleAsync(client, staff.Token, owner.ShopId, invoicesJson);
        Assert.Equal(HttpStatusCode.Forbidden, staffCaller.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.OfflineSubmissions.AnyAsync(s => s.ShopId == owner.ShopId));
        Assert.False(await db.ReconciliationIssues.AnyAsync(i => i.ShopId == owner.ShopId));
        Assert.False(await db.Sales.AnyAsync(s => s.ShopId == owner.ShopId));
    }

    // A recovery review and the cashier's own late sync of the same invoice end in one sale.
    [Fact]
    public async Task RecoveryAndLateSync_OneEconomicEffect()
    {
        using var client = CreateClient();
        var owner = await CreateOwnerAsync(client);
        var stock = await AddInventoryAsync(client, owner.Token, 10m);
        var lease = await ReserveInvoiceLeaseAsync(client, owner.Token);
        var staff = await AddStaffAsync(client, owner);
        await SyncAsync(client, staff.Token, Payload(NewClientSaleId(), InvoiceAt(lease, 0), stock, 1m, DateTimeOffset.UtcNow, staff.UserId));

        var clientSaleId = NewClientSaleId();
        var invoice = InvoiceAt(lease, 1);
        var payload = Payload(clientSaleId, invoice, stock, 1m, DateTimeOffset.UtcNow, staff.UserId);
        using var submitted = await SubmitBundleAsync(client, owner.Token, owner.ShopId, InvoicesJson(payload));
        var row = Assert.Single((await ReadRecoveryAsync(submitted)).GetProperty("results").EnumerateArray());
        Assert.Equal("ReviewOpened", row.GetProperty("status").GetString());
        var caseId = row.GetProperty("caseId").GetGuid();

        var late = await SyncAsync(client, staff.Token, payload);
        Assert.Equal("created", late.GetProperty("status").GetString());
        var saleId = late.GetProperty("saleId").GetGuid();

        var detail = await GetCaseAsync(client, owner.Token, caseId);
        var retried = await ResolveAsync(client, owner.Token, caseId, detail.GetProperty("version").GetUInt32(), "RetryHistoricalLease", "The cashier synced it after all");
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        var resolved = await retried.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Resolved", resolved.GetProperty("status").GetString());
        Assert.Equal(saleId, resolved.GetProperty("saleId").GetGuid());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.Sales.CountAsync(s => s.ShopId == owner.ShopId && s.ClientSaleId == clientSaleId));
        Assert.Equal(1, await db.StockTransactions.CountAsync(t => t.ShopId == owner.ShopId && t.ReferenceNumber == invoice));
        Assert.Equal(8m, (await db.InventoryBatches.SingleAsync(b => b.Id == stock.BatchId)).Quantity);
        var envelope = await db.OfflineSubmissions.SingleAsync(s => s.ClientSaleId == clientSaleId);
        Assert.Equal(saleId, envelope.SaleId);
    }

    [Fact]
    public async Task UnknownLegacyProvenance_EscalatesWithoutPosting()
    {
        using var client = CreateClient();
        var owner = await CreateOwnerAsync(client);
        var stock = await AddInventoryAsync(client, owner.Token, 10m);
        var lease = await ReserveInvoiceLeaseAsync(client, owner.Token);

        var clientSaleId = NewClientSaleId();
        using var submitted = await SubmitBundleAsync(client, owner.Token, owner.ShopId, InvoicesJson(Payload(clientSaleId, InvoiceAt(lease, 0), stock, 1m, DateTimeOffset.UtcNow)));
        var row = Assert.Single((await ReadRecoveryAsync(submitted)).GetProperty("results").EnumerateArray());
        Assert.Equal("Escalated", row.GetProperty("status").GetString());
        Assert.Equal(Errors.OfflineReconciliation.ProvenanceUnknown.Code, row.GetProperty("errors")[0].GetProperty("code").GetString());
        var caseId = row.GetProperty("caseId").GetGuid();

        var detail = await GetCaseAsync(client, owner.Token, caseId);
        Assert.Equal("ProvenanceReview", detail.GetProperty("issueType").GetString());
        Assert.Equal("Escalated", detail.GetProperty("status").GetString());
        Assert.Empty(detail.GetProperty("availableActions").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("submission").GetProperty("originalIssuer").ValueKind);
        var escalation = Assert.Single(detail.GetProperty("events").EnumerateArray());
        Assert.Equal("Escalate", escalation.GetProperty("action").GetString());
        Assert.Equal(owner.UserId, escalation.GetProperty("actor").GetProperty("userId").GetGuid());
        Assert.Equal(Errors.OfflineReconciliation.ProvenanceUnknown.Description, escalation.GetProperty("reason").GetString());

        var forced = await ResolveAsync(client, owner.Token, caseId, detail.GetProperty("version").GetUInt32(), "RetryHistoricalLease", "Post it anyway");
        await AssertProblemAsync(forced, HttpStatusCode.BadRequest, Errors.Reconciliation.ActionNotAllowed.Code);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.Sales.AnyAsync(s => s.ShopId == owner.ShopId));
        Assert.Equal(10m, (await db.InventoryBatches.SingleAsync(b => b.Id == stock.BatchId)).Quantity);
        Assert.Null((await db.OfflineSubmissions.SingleAsync(s => s.ClientSaleId == clientSaleId)).SaleId);
    }

    [Fact]
    public async Task List_RejectsUnknownStatusFilter()
    {
        using var client = CreateClient();
        var owner = await CreateOwnerAsync(client);

        using var response = await SendAsync(client, HttpMethod.Get, $"{Route}?status=pending", owner.Token);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, Errors.Reconciliation.InvalidStatusFilter.Code);
    }

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
    });

    private static string NewClientSaleId() => $"offline-{Guid.NewGuid():N}";

    private static List<string> Strings(JsonElement array) =>
        array.EnumerateArray().Select(e => e.GetString()!).ToList();

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string uri, string token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"Expected {status} {code} but got {response.StatusCode}: {content}");
        using var problem = JsonDocument.Parse(content);
        Assert.Equal(code, problem.RootElement.GetProperty("title").GetString());
    }

    private static async Task<OwnerContext> CreateOwnerAsync(HttpClient client)
    {
        var enrolled = await client.EnrollAsync(new
        {
            email = $"recon-{Guid.NewGuid():N}@test.com",
            password = "Pass123!Aa",
            firstName = "Olive",
            lastName = "Owner",
            phoneNumber = $"+91{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}",
        });
        enrolled.EnsureSuccessStatusCode();
        var token = (await enrolled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;

        using var shop = await SendAsync(client, HttpMethod.Post, "/api/shops", token, new
        {
            name = $"Shop {Guid.NewGuid():N}",
            address = "42 MG Road",
            city = "Bengaluru",
            state = "Karnataka",
            pincode = "560001",
        });
        shop.EnsureSuccessStatusCode();
        var shopToken = (await shop.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(shopToken);
        return new OwnerContext(
            shopToken,
            Guid.Parse(jwt.Claims.Single(c => c.Type == "active_shop_id").Value),
            Guid.Parse(jwt.Subject));
    }

    private static async Task<StaffContext> AddStaffAsync(HttpClient client, OwnerContext owner)
    {
        var email = $"recon-staff-{Guid.NewGuid():N}@test.com";
        const string password = "StaffPass1!";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/users");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", owner.Token);
        request.Content = JsonContent.Create(new
        {
            shopIds = new[] { owner.ShopId },
            email,
            firstName = "Sam",
            lastName = "Staff",
            phoneNumber = $"+91{Random.Shared.NextInt64(6_000_000_000, 9_999_999_999)}",
            password,
            confirmPassword = password,
            role = "Staff",
        });
        var seeded = await client.SeedVerifiedStaffAsync(request);
        seeded.EnsureSuccessStatusCode();

        var login = await client.PostAsJsonAsync("/api/auth/login/email", new { email, password });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
        return new StaffContext(token, Guid.Parse(new JwtSecurityTokenHandler().ReadJwtToken(token).Subject));
    }

    private static async Task<StockContext> AddInventoryAsync(HttpClient client, string token, decimal quantity)
    {
        var barcode = $"RECON-{Guid.NewGuid():N}";
        using var response = await SendAsync(client, HttpMethod.Post, "/api/inventory/inbound", token, new
        {
            itemName = "Test Item",
            barcode,
            itemDescription = (string?)null,
            hsnCode = (string?)null,
            uom = "kg",
            batchNumber = "B-001",
            quantity,
            totalPurchaseCost = 80m * quantity,
            mrp = 120m,
            salesPrice = 100m,
            taxRatePercent = 18m,
            taxIncluded = false,
            purchaseTaxIncluded = false,
            expiryDate = (DateOnly?)null,
            manufacturingDate = (DateOnly?)null,
            supplierId = (Guid?)null,
            referenceNumber = (string?)null,
            notes = (string?)null,
            performedAt = (DateTimeOffset?)null,
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new StockContext(barcode, body.GetProperty("itemId").GetGuid(), body.GetProperty("inventoryBatchId").GetGuid());
    }

    private static async Task<JsonElement> ReserveInvoiceLeaseAsync(HttpClient client, string token, string deviceId = Device)
    {
        using var response = await SendAsync(client, HttpMethod.Post, "/api/sales/invoice-leases/reserve", token, new { deviceId, blockSize = 25 });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static string InvoiceAt(JsonElement lease, int offset) =>
        $"{lease.GetProperty("prefix").GetString()}{(lease.GetProperty("rangeStart").GetInt32() + offset).ToString($"D{lease.GetProperty("numberPadding").GetInt32()}", System.Globalization.CultureInfo.InvariantCulture)}";

    private static async Task<string> FirstLeasedInvoiceNumberAsync(HttpClient client, string token) =>
        InvoiceAt(await ReserveInvoiceLeaseAsync(client, token), 0);

    /// <summary>
    /// Moves the lease into the past (reserved nine days ago, expired two days ago) through the owner
    /// connection, which bypasses row-level security. The fiscal year follows the claimed issue time
    /// so the test means the same thing on every calendar day.
    /// </summary>
    private async Task ExpireLeaseAsync(Guid leaseId, DateTimeOffset claimedSoldAt)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTimeOffset.UtcNow;
        var fiscalYearStart = FiscalYear.ForDate(claimedSoldAt).StartYear;
        var updated = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE invoice_leases
            SET reserved_at = {now.AddDays(-9)}, expires_at = {now.AddDays(-2)}, fiscal_year_start = {fiscalYearStart}
            WHERE id = {leaseId}
            """);
        Assert.Equal(1, updated);
    }

    private static object Payload(string clientSaleId, string invoiceNumber, StockContext stock, decimal quantity, DateTimeOffset soldAt, Guid? issuedByUserId = null)
    {
        var preTax = 100m * quantity;
        var tax = 18m * quantity;
        var total = preTax + tax;
        return new
        {
            clientSaleId,
            invoiceNumber,
            soldAt,
            issuedByUserId,
            paymentMethod = (int)PaymentMethod.Cash,
            paidAmount = total,
            dueAmount = 0m,
            subtotalBeforeDiscount = preTax,
            totalBeforeDiscount = total,
            totalDiscountAmount = 0m,
            totalTaxAmount = tax,
            totalAmount = total,
            saleDiscountOverrideType = (int)InstantDiscountType.None,
            saleDiscountOverrideValue = 0m,
            items = new[]
            {
                new
                {
                    barcode = stock.Barcode,
                    batchNumber = "B-001",
                    itemName = "Test Item",
                    quantity,
                    costPrice = 80m,
                    salesPrice = 100m,
                    mrp = 120m,
                    taxRatePercent = 18m,
                    isPriceIncludingTax = false,
                    inventoryBatchId = stock.BatchId,
                    preTaxAmountBeforeDiscount = preTax,
                    itemDiscountAmount = 0m,
                    saleDiscountAmount = 0m,
                    taxableAmount = preTax,
                    taxAmount = tax,
                    totalAmount = total,
                    itemDiscountOverrideType = (int)InstantDiscountType.None,
                    itemDiscountOverrideValue = 0m,
                },
            },
        };
    }

    private static async Task<JsonElement> SyncAsync(HttpClient client, string token, object sale)
    {
        using var response = await SendAsync(client, HttpMethod.Post, "/api/sales/offline-sync", token, new { deviceId = Device, sales = new[] { sale } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("results")[0];
    }

    private static async Task<JsonElement> ListAsync(HttpClient client, string token, string status)
    {
        using var response = await SendAsync(client, HttpMethod.Get, $"{Route}?status={status}&page=1&pageSize=20", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<Guid> FindCaseIdAsync(HttpClient client, string token, string clientSaleId, string issueType)
    {
        var page = await ListAsync(client, token, "open");
        return page.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("clientSaleId").GetString() == clientSaleId && i.GetProperty("issueType").GetString() == issueType)
            .GetProperty("caseId").GetGuid();
    }

    private static async Task<JsonElement> GetCaseAsync(HttpClient client, string token, Guid caseId)
    {
        using var response = await SendAsync(client, HttpMethod.Get, $"{Route}/{caseId}", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static Task<HttpResponseMessage> ResolveAsync(
        HttpClient client,
        string token,
        Guid caseId,
        uint expectedVersion,
        string action,
        string reason,
        Guid? linkedOperationId = null,
        string? operationId = null) =>
        SendAsync(client, HttpMethod.Post, $"{Route}/{caseId}/resolve", token, new
        {
            operationId = operationId ?? $"resolve-{Guid.NewGuid():N}",
            expectedVersion,
            action,
            reason,
            linkedOperationId,
        });

    private static async Task<AdjustmentContext> CreateAdjustmentAsync(
        HttpClient client,
        string token,
        Guid batchId,
        InventoryAdjustmentReason reason,
        decimal quantity,
        string notes)
    {
        using var response = await SendAsync(client, HttpMethod.Post, $"/api/inventory/batches/{batchId}/adjust", token, new
        {
            direction = InventoryAdjustmentDirection.Increase,
            reason,
            quantity,
            performedAt = (DateTimeOffset?)null,
            notes,
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new AdjustmentContext(body.GetProperty("adjustmentId").GetGuid(), body.GetProperty("adjustmentNumber").GetString()!);
    }

    private static async Task VoidAdjustmentAsync(HttpClient client, string token, Guid adjustmentId, string reason)
    {
        using var response = await SendAsync(client, HttpMethod.Post, $"/api/inventory/adjustments/{adjustmentId}/void", token, new { reason });
        response.EnsureSuccessStatusCode();
    }

    private async Task RemoveMembershipAsync(HttpClient client, OwnerContext owner, Guid userId)
    {
        long version;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            version = (await db.ShopMemberships.AsNoTracking().SingleAsync(m => m.ShopId == owner.ShopId && m.UserId == userId)).AuthorityVersion;
        }

        using var response = await SendAsync(client, HttpMethod.Patch, $"/api/shops/{owner.ShopId}/members/{userId}", owner.Token, new
        {
            role = "Staff",
            status = "Removed",
            expectedVersion = version,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>The bundle's invoices exactly as the web client builds them: each element is one sync request.</summary>
    private static string InvoicesJson(params object[] invoices) =>
        JsonSerializer.Serialize(invoices, WebJsonOptions);

    private static string Checksum(string invoicesJson) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(invoicesJson)));

    /// <summary>
    /// Splices the invoices text into the request unchanged, so the server reads the very bytes the
    /// checksum was computed over.
    /// </summary>
    private static Task<HttpResponseMessage> SubmitBundleAsync(
        HttpClient client,
        string token,
        Guid shopId,
        string invoicesJson,
        string? operationId = null,
        string? checksum = null) =>
        PostRecoveryAsync(
            client,
            token,
            $$$"""
            {"operationId":"{{{operationId ?? $"recovery-{Guid.NewGuid():N}"}}}","bundle":{"format":"intelibill.offline-recovery","version":1,"shopId":"{{{shopId}}}","deviceId":"{{{Device}}}","exportedAt":"{{{DateTimeOffset.UtcNow:O}}}","checksum":"{{{checksum ?? Checksum(invoicesJson)}}}","invoices":{{{invoicesJson}}}}}
            """);

    private static async Task<HttpResponseMessage> PostRecoveryAsync(HttpClient client, string token, string body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Route}/recovery-submissions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> ReadRecoveryAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Expected 200 but got {response.StatusCode}: {content}");
        using var document = JsonDocument.Parse(content);
        return document.RootElement.Clone();
    }

    private sealed record OwnerContext(string Token, Guid ShopId, Guid UserId);

    private sealed record StaffContext(string Token, Guid UserId);

    private sealed record StockContext(string Barcode, Guid ItemId, Guid BatchId);

    private sealed record AdjustmentContext(Guid Id, string Number);
}
