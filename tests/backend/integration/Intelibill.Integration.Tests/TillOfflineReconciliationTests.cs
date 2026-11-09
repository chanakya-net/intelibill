using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Options;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using static Intelibill.Integration.Tests.TillTestContext;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed partial class TillOfflineReconciliationTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
{
    private const string Device = "device-1";
    private const string Route = "/api/offline-reconciliation";
    private readonly ApiWebApplicationFactory _factory = new(fixture);
    public Task InitializeAsync() => _factory.InitializeAsync();
    public Task DisposeAsync() { _factory.Dispose(); return Task.CompletedTask; }
    public void Dispose() { _factory.Dispose(); GC.SuppressFinalize(this); }

    [Fact]
    public async Task OfflineAfterClose_CreatesVisibleReconciliation()
    {
        using var client = CreateClient();
        var (owner, payload, session) = await ArrangeAsync(client);
        await CloseAsync(owner, session);
        var result = await SyncAsync(client, owner.Token, payload);
        Assert.Equal("NeedsReview", result.GetProperty("status").GetString());
        var detail = await GetCaseAsync(client, owner.Token, result.GetProperty("reviewCaseId").GetGuid());
        Assert.Equal("TillReview", detail.GetProperty("issueType").GetString());
        Assert.Contains("ReconcileTillSale", Strings(detail.GetProperty("availableActions")));
        Assert.Contains("RetryHistoricalLease", Strings(detail.GetProperty("availableActions")));
        using var refused = await ResolveAsync(client, owner.Token, detail.GetProperty("caseId").GetGuid(), detail.GetProperty("version").GetUInt32(), "RetryHistoricalLease", "Closed drawers cannot accept ordinary replay");
        Assert.False(refused.IsSuccessStatusCode);
        Assert.Contains("Till.SessionClosed", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var resend = await SyncAsync(client, owner.Token, payload);
        Assert.Equal(result.GetProperty("reviewCaseId").GetGuid(), resend.GetProperty("reviewCaseId").GetGuid());
    }

    [Fact]
    public async Task OfflineBeforeClose_IncludedOnce()
    {
        using var client = CreateClient();
        var (owner, payload, session) = await ArrangeAsync(client);
        var first = await SyncAsync(client, owner.Token, payload);
        Assert.True(first.GetProperty("status").GetString() is "created" or "SyncedWithWarnings");
        Assert.Equal("duplicate", (await SyncAsync(client, owner.Token, payload)).GetProperty("status").GetString());
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var movement = Assert.Single(await db.TillMovements.Where(x => x.SessionId == session.Id).ToArrayAsync());
        Assert.Equal(118m, movement.SignedAmount);
        Assert.Equal(TillMovementSource.OfflineSale, movement.SourceType);
        Assert.Equal(first.GetProperty("saleId").GetGuid(), movement.SourceId);
        Assert.Equal(1, await db.TillSourceRecords.CountAsync(x => x.SessionId == session.Id));
        Assert.False(await db.TillLatePostings.AnyAsync(x => x.ShopId == owner.ShopId));
    }

    [Fact]
    public async Task ReconcileTillSale_PreservesInvoiceDateAndCloseBytes_AndNoCurrentDrawerCash()
    {
        using var client = CreateClient();
        var (owner, payload, session) = await ArrangeAsync(client);
        await CloseAsync(owner, session);
        var held = await SyncAsync(client, owner.Token, payload);
        var caseId = held.GetProperty("reviewCaseId").GetGuid();
        var detail = await GetCaseAsync(client, owner.Token, caseId);
        await using var app = new TillTestContext(fixture.MigratorConnectionString, owner.ShopId, new() { Now = DateTimeOffset.UtcNow });
        var original = await app.Db.TillCloses.AsNoTracking().SingleAsync(x => x.SessionId == session.Id);
        var closeBytes = JsonSerializer.Serialize(original);
        var envelope = await app.Db.OfflineSubmissions.AsNoTracking().SingleAsync(x => x.ShopId == owner.ShopId);
        var current = await app.OpenAsync(owner.ShopId, owner.UserId, session.TillId);
        using var resolved = await ResolveTillAsync(client, owner, caseId, detail.GetProperty("version").GetUInt32(), payload);
        Assert.True(resolved.IsSuccessStatusCode, await resolved.Content.ReadAsStringAsync());
        var body = await resolved.Content.ReadFromJsonAsync<JsonElement>();
        var sale = await app.Db.Sales.AsNoTracking().SingleAsync(x => x.ShopId == owner.ShopId);
        Assert.Equal(payload["invoiceNumber"]!.GetValue<string>(), sale.InvoiceNumber);
        AssertSameInstant(payload["soldAt"]!.GetValue<DateTimeOffset>(), sale.SoldAt);
        Assert.Equal(session.BusinessDate, sale.BusinessDate);
        Assert.Equal(envelope.PayloadHash, sale.RequestHash);
        Assert.Equal(envelope.PayloadJson, (await app.Db.OfflineSubmissions.AsNoTracking().SingleAsync(x => x.Id == envelope.Id)).PayloadJson);
        Assert.Equal(closeBytes, JsonSerializer.Serialize(await app.Db.TillCloses.AsNoTracking().SingleAsync(x => x.Id == original.Id)));
        var late = await app.Db.TillLatePostings.AsNoTracking().SingleAsync(x => x.ShopId == owner.ShopId);
        Assert.Equal(session.Id, late.SessionId);
        Assert.Equal(original.Id, late.CloseId);
        Assert.False(late.IncludedInCount);
        Assert.Equal(118m, Assert.Single(late.TenderLines).AppliedAmount);
        Assert.False(await app.Db.TillMovements.AnyAsync(x => x.ShopId == owner.ShopId));
        Assert.Equal(1000m, Success(await app.Reads.SessionAsync(owner.ShopId, owner.UserId, current.Id, 1, 20, default)).Session.ExpectedCash);
        Assert.Equal("Recorded", body.GetProperty("tillLatePosting").GetProperty("evidenceStatus").GetString());
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("Recorded")]
    public async Task LegacyMissingEvidence_RemainsExplicit(string status)
    {
        using var client = CreateClient();
        var (owner, payload, _) = await ArrangeAsync(client);
        var managerEvidence = payload.DeepClone().AsObject();
        foreach (var name in new[] { "till", "tenderLines", "tillBusinessDate", "tillCashierId" }) payload.Remove(name);
        var held = await SyncAsync(client, owner.Token, payload);
        var caseId = held.GetProperty("reviewCaseId").GetGuid();
        var detail = await GetCaseAsync(client, owner.Token, caseId);
        using var resolved = await ResolveTillAsync(client, owner, caseId, detail.GetProperty("version").GetUInt32(), managerEvidence, status);
        Assert.True(resolved.IsSuccessStatusCode, await resolved.Content.ReadAsStringAsync());
        var late = (await resolved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tillLatePosting");
        Assert.Equal(status, late.GetProperty("evidenceStatus").GetString());
        if (status == "Unknown") Assert.Equal(JsonValueKind.Null, late.GetProperty("supplementalAmount").ValueKind);
        else Assert.Equal(118m, late.GetProperty("supplementalAmount").GetDecimal());
        Assert.Equal(JsonValueKind.Null, late.GetProperty("sessionId").ValueKind);
        Assert.Equal(status == "Unknown" ? 0 : 1, late.GetProperty("tenderLines").GetArrayLength());
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Null((await db.Sales.SingleAsync(x => x.ShopId == owner.ShopId)).TenderLines);
        Assert.False(await db.TillMovements.AnyAsync(x => x.ShopId == owner.ShopId));
        Assert.Contains("Verified payment evidence", (await db.TillLatePostings.SingleAsync(x => x.ShopId == owner.ShopId)).EvidenceReason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("cashier")]
    [InlineData("time")]
    [InlineData("missing")]
    public async Task WrongCashierOrIssueTime_NotAssigned(string mismatch)
    {
        using var client = CreateClient();
        var (owner, payload, session) = await ArrangeAsync(client);
        if (mismatch == "cashier") payload["tillCashierId"] = Guid.NewGuid();
        if (mismatch == "time") payload["soldAt"] = session.OpenedAt.AddSeconds(-1);
        if (mismatch == "missing") payload["till"]!["sessionId"] = Guid.NewGuid();
        var held = await SyncAsync(client, owner.Token, payload);
        var caseId = held.GetProperty("reviewCaseId").GetGuid();
        var detail = await GetCaseAsync(client, owner.Token, caseId);
        using var resolved = await ResolveTillAsync(client, owner, caseId, detail.GetProperty("version").GetUInt32(), payload);
        Assert.True(resolved.IsSuccessStatusCode, await resolved.Content.ReadAsStringAsync());
        var late = (await resolved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tillLatePosting");
        Assert.Equal(JsonValueKind.Null, late.GetProperty("sessionId").ValueKind);
        Assert.Equal(JsonValueKind.Null, late.GetProperty("closeId").ValueKind);
        Assert.False(late.GetProperty("includedInCount").GetBoolean());
    }

    [Fact]
    public async Task RetryHistoricalLease_CannotBypassTillReview_AfterOriginalPeriodReopens()
    {
        using var client = CreateClient();
        var (owner, payload, session) = await ArrangeAsync(client);
        await CloseAsync(owner, session);
        await using var app = new TillTestContext(fixture.MigratorConnectionString, owner.ShopId);
        var period = await app.Db.PostingPeriods.SingleAsync(x => x.ShopId == owner.ShopId);
        var closed = period.Close(owner.UserId, DateTimeOffset.UtcNow, period.EndDate.AddDays(1), "Test month closure");
        Assert.False(closed.IsError);
        app.Db.PostingPeriodEvents.Add(closed.Value);
        await app.Db.SaveChangesAsync();
        var held = await SyncAsync(client, owner.Token, payload);
        var caseId = held.GetProperty("reviewCaseId").GetGuid();
        var detail = await GetCaseAsync(client, owner.Token, caseId);
        Assert.Equal("ClosedPeriodReview", detail.GetProperty("issueType").GetString());
        Assert.Contains("ReconcileTillSale", Strings(detail.GetProperty("availableActions")));
        using var refusedWhileClosed = await ResolveTillAsync(client, owner, caseId, detail.GetProperty("version").GetUInt32(), payload);
        Assert.False(refusedWhileClosed.IsSuccessStatusCode);
        Assert.Contains("PostingPeriod.Closed", await refusedWhileClosed.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var reopened = period.Reopen(owner.UserId, DateTimeOffset.UtcNow, "Owner reopened original month");
        app.Db.PostingPeriodEvents.Add(Success(reopened));
        await app.Db.SaveChangesAsync();
        var resent = await SyncAsync(client, owner.Token, payload);
        Assert.Equal(caseId, resent.GetProperty("reviewCaseId").GetGuid());
        using var refused = await ResolveAsync(client, owner.Token, caseId, detail.GetProperty("version").GetUInt32(), "RetryHistoricalLease", "Historical review alone");
        Assert.False(refused.IsSuccessStatusCode);
        Assert.Contains("Till.SessionClosed", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.False(await app.Db.Sales.AnyAsync(x => x.ShopId == owner.ShopId));
        using var explicitReview = await ResolveTillAsync(client, owner, caseId, detail.GetProperty("version").GetUInt32(), payload);
        Assert.True(explicitReview.IsSuccessStatusCode, await explicitReview.Content.ReadAsStringAsync());
        Assert.Single(await app.Db.ReconciliationIssues.Where(x => x.OfflineSubmissionId != null && x.ShopId == owner.ShopId).ToArrayAsync());
    }

    [Fact]
    public async Task HeldEnvelope_DisableCannotTurnIntoLegacyPosting_AndResolvedReplaySurvivesDisable()
    {
        using var client = CreateClient();
        var (owner, payload, session) = await ArrangeAsync(client);
        await CloseAsync(owner, session);
        var held = await SyncAsync(client, owner.Token, payload);
        var caseId = held.GetProperty("reviewCaseId").GetGuid();
        var detail = await GetCaseAsync(client, owner.Token, caseId);
        var options = _factory.Services.GetRequiredService<IOptions<TillSessionOptions>>().Value;
        options.Enabled = false;
        Assert.Equal(caseId, (await SyncAsync(client, owner.Token, payload)).GetProperty("reviewCaseId").GetGuid());
        var operation = Guid.NewGuid().ToString();
        using var refused = await ResolveTillAsync(client, owner, caseId, detail.GetProperty("version").GetUInt32(), payload, operation: operation);
        Assert.False(refused.IsSuccessStatusCode);
        Assert.Contains("Till.Disabled", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        options.Enabled = true;
        using var resolved = await ResolveTillAsync(client, owner, caseId, detail.GetProperty("version").GetUInt32(), payload, operation: operation);
        Assert.True(resolved.IsSuccessStatusCode, await resolved.Content.ReadAsStringAsync());
        var saved = await resolved.Content.ReadAsStringAsync();
        options.Enabled = false;
        using var replay = await ResolveTillAsync(client, owner, caseId, detail.GetProperty("version").GetUInt32(), payload, operation: operation);
        Assert.Equal(saved, await replay.Content.ReadAsStringAsync());
        Assert.Equal("duplicate", (await SyncAsync(client, owner.Token, payload)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task TwoManagers_DuplicateResolutionRace_PostsOneSupplementOnly()
    {
        using var client = CreateClient();
        var (owner, payload, session) = await ArrangeAsync(client);
        await CloseAsync(owner, session);
        var held = await SyncAsync(client, owner.Token, payload);
        var caseId = held.GetProperty("reviewCaseId").GetGuid();
        var detail = await GetCaseAsync(client, owner.Token, caseId);
        using var secondClient = CreateClient();
        var manager = await AddManagerAsync(secondClient, owner);
        var tasks = await Task.WhenAll(
            ResolveTillAsync(client, owner, caseId, detail.GetProperty("version").GetUInt32(), payload),
            ResolveTillAsync(secondClient, manager, caseId, detail.GetProperty("version").GetUInt32(), payload));
        try
        {
            Assert.Single(tasks, x => x.IsSuccessStatusCode);
            Assert.Equal(HttpStatusCode.Conflict, Assert.Single(tasks, x => !x.IsSuccessStatusCode).StatusCode);
        }
        finally { foreach (var response in tasks) response.Dispose(); }
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.TillLatePostings.CountAsync(x => x.ShopId == owner.ShopId));
        Assert.Equal(1, await db.Sales.CountAsync(x => x.ShopId == owner.ShopId));
        Assert.False(await db.TillMovements.AnyAsync(x => x.ShopId == owner.ShopId));
    }

    [Fact]
    public async Task TwoManagers_CrossCaseRace_DoesNotDuplicateSaleOrSupplement()
    {
        using var client = CreateClient();
        var (owner, payload, session) = await ArrangeAsync(client);
        await CloseAsync(owner, session);
        var held = await SyncAsync(client, owner.Token, payload);
        var caseId = held.GetProperty("reviewCaseId").GetGuid();
        var detail = await GetCaseAsync(client, owner.Token, caseId);
        await using var app = new TillTestContext(fixture.MigratorConnectionString, owner.ShopId);
        var submission = await app.Db.OfflineSubmissions.SingleAsync(x => x.ShopId == owner.ShopId);
        var second = ReconciliationIssue.Create(owner.ShopId, null, submission.ClientSaleId, Device,
            ReconciliationIssueType.TillReview, "Till.ReviewRequired", "Earlier independent hold", owner.UserId);
        second.AttachEvidence(submission.InvoiceNumber, submission.Id);
        app.Db.ReconciliationIssues.Add(second);
        await app.Db.SaveChangesAsync();
        using var otherClient = CreateClient();
        var manager = await AddManagerAsync(otherClient, owner);
        var responses = await Task.WhenAll(
            ResolveTillAsync(client, owner, caseId, detail.GetProperty("version").GetUInt32(), payload),
            ResolveTillAsync(otherClient, manager, second.Id, second.Version, payload));
        try
        {
            Assert.Contains(responses, x => x.IsSuccessStatusCode);
            Assert.All(responses, x => Assert.True(x.IsSuccessStatusCode || x.StatusCode == HttpStatusCode.Conflict));
        }
        finally { foreach (var response in responses) response.Dispose(); }
        Assert.Equal(1, await app.Db.Sales.CountAsync(x => x.ShopId == owner.ShopId));
        Assert.Equal(1, await app.Db.TillLatePostings.CountAsync(x => x.ShopId == owner.ShopId));
        Assert.False(await app.Db.TillMovements.AnyAsync(x => x.ShopId == owner.ShopId));
        Assert.Equal(9m, (await app.Db.InventoryBatches.SingleAsync(x => x.ShopId == owner.ShopId)).Quantity);
    }

    [Theory]
    [InlineData("tender")]
    [InlineData("override")]
    public async Task UnstorableFrozenTillText_RefusedBeforeEnvelope(string field)
    {
        using var client = CreateClient();
        var (owner, payload, _) = await ArrangeAsync(client);
        if (field == "tender") payload["tenderLines"]![0]!["externalReference"] = "bad\0reference";
        else payload["till"]!["overrideReason"] = "bad\0reason";
        var result = await SyncAsync(client, owner.Token, payload);
        Assert.Equal("Sale.OfflineTextInvalid", result.GetProperty("errors")[0].GetProperty("code").GetString());
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.OfflineSubmissions.AnyAsync(x => x.ShopId == owner.ShopId));
    }

    [Fact]
    public async Task UnstorableManagerTender_RefusedAndLeavesHeldEnvelope()
    {
        using var client = CreateClient();
        var (owner, payload, _) = await ArrangeAsync(client);
        foreach (var name in new[] { "till", "tenderLines", "tillBusinessDate", "tillCashierId" }) payload.Remove(name);
        var held = await SyncAsync(client, owner.Token, payload);
        var caseId = held.GetProperty("reviewCaseId").GetGuid();
        var detail = await GetCaseAsync(client, owner.Token, caseId);
        var evidencePayload = payload.DeepClone().AsObject();
        evidencePayload["tenderLines"] = JsonNode.Parse("""[{"method":2,"appliedAmount":118}]""");
        evidencePayload["tenderLines"]![0]!["externalReference"] = "bad\0reference";
        using var refused = await ResolveTillAsync(client, owner, caseId, detail.GetProperty("version").GetUInt32(), evidencePayload);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("OfflineTextInvalid", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal("Open", (await GetCaseAsync(client, owner.Token, caseId)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task OverdueOpenSession_HoldsOriginalInvoice_AndReviewedPostingDoesNotAppend()
    {
        using var client = CreateClient();
        var (owner, payload, session) = await ArrangeAsync(client);
        var soldAt = DateTimeOffset.UtcNow.AddDays(-1);
        var date = DateOnly.FromDateTime(soldAt.AddHours(5.5).DateTime);
        payload["soldAt"] = soldAt;
        payload["tillBusinessDate"] = date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        await using var app = new TillTestContext(fixture.MigratorConnectionString, owner.ShopId);
        await app.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE till_sessions SET opened_at={soldAt.AddMinutes(-1)}, business_date={date} WHERE id={session.Id}");
        await app.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE invoice_leases SET reserved_at={soldAt.AddHours(-1)} WHERE shop_id={owner.ShopId}");
        var held = await SyncAsync(client, owner.Token, payload);
        var caseId = held.GetProperty("reviewCaseId").GetGuid();
        var detail = await GetCaseAsync(client, owner.Token, caseId);
        Assert.Equal("TillReview", detail.GetProperty("issueType").GetString());
        Assert.Contains("Till.Overdue", Strings(detail.GetProperty("reasonCodes")));
        using var resolved = await ResolveTillAsync(client, owner, caseId, detail.GetProperty("version").GetUInt32(), payload);
        Assert.True(resolved.IsSuccessStatusCode, await resolved.Content.ReadAsStringAsync());
        var late = await app.Db.TillLatePostings.SingleAsync(x => x.ShopId == owner.ShopId);
        Assert.Equal(session.Id, late.SessionId);
        Assert.Null(late.CloseId);
        Assert.False(late.IncludedInCount);
        Assert.False(await app.Db.TillMovements.AnyAsync(x => x.ShopId == owner.ShopId));
        var original = await app.Db.TillSessions.SingleAsync(x => x.Id == session.Id);
        Assert.Equal(TillSessionState.Open, original.State);
        Assert.Equal(0, original.SourceCount);
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("amount")]
    [InlineData("method")]
    public async Task ManagerCannotRelabelFrozenEvidenceOrNewEvidenceAsUnknown(string change)
    {
        using var client = CreateClient();
        var (owner, payload, session) = await ArrangeAsync(client);
        await CloseAsync(owner, session);
        var held = await SyncAsync(client, owner.Token, payload);
        var caseId = held.GetProperty("reviewCaseId").GetGuid();
        var detail = await GetCaseAsync(client, owner.Token, caseId);
        var evidence = payload.DeepClone().AsObject();
        if (change == "amount") evidence["tenderLines"]![0]!["appliedAmount"] = 117m;
        if (change == "method") evidence["tenderLines"]![0]!["method"] = 3;
        using var refused = await ResolveTillAsync(client, owner, caseId, detail.GetProperty("version").GetUInt32(), evidence, change == "Unknown" ? "Unknown" : "Recorded");
        Assert.False(refused.IsSuccessStatusCode);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.Sales.AnyAsync(x => x.ShopId == owner.ShopId));
        Assert.False(await db.TillLatePostings.AnyAsync(x => x.ShopId == owner.ShopId));
    }

    [Theory]
    [InlineData("ReconcileTillSale")]
    [InlineData("RetryHistoricalLease")]
    public async Task StaffCannotReplayManagerResolution_EvenWhenTheyFiledTheCase(string action)
    {
        using var client = CreateClient();
        var (owner, payload, session) = await ArrangeAsync(client);
        var options = _factory.Services.GetRequiredService<IOptions<TillSessionOptions>>().Value;
        if (action == "ReconcileTillSale") await CloseAsync(owner, session);
        else options.Enabled = false;
        await SyncAsync(client, owner.Token, payload);
        options.Enabled = true;
        using var staffClient = CreateClient();
        var staff = await AddManagerAsync(staffClient, owner, "Staff");
        await using var app = new TillTestContext(fixture.MigratorConnectionString, owner.ShopId);
        var submission = await app.Db.OfflineSubmissions.SingleAsync(x => x.ShopId == owner.ShopId);
        var own = ReconciliationIssue.Create(owner.ShopId, null, submission.ClientSaleId, Device, ReconciliationIssueType.TillReview,
            "Till.ReviewRequired", "Own case", staff.UserId);
        own.AttachEvidence(submission.InvoiceNumber, submission.Id);
        app.Db.ReconciliationIssues.Add(own);
        await app.Db.SaveChangesAsync();
        var operation = Guid.NewGuid().ToString();
        using var resolved = action == "ReconcileTillSale"
            ? await ResolveTillAsync(client, owner, own.Id, own.Version, payload, operation: operation)
            : await ResolveAsync(client, owner.Token, own.Id, own.Version, action, "Manager normal replay", operationId: operation);
        Assert.True(resolved.IsSuccessStatusCode, await resolved.Content.ReadAsStringAsync());
        using var forbidden = action == "ReconcileTillSale"
            ? await ResolveTillAsync(staffClient, staff, own.Id, own.Version, payload, operation: operation)
            : await ResolveAsync(staffClient, staff.Token, own.Id, own.Version, action, "Manager normal replay", operationId: operation);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var changedEvidence = action == "ReconcileTillSale"
            ? await ResolveTillAsync(client, owner, own.Id, own.Version, payload, "Unknown", operation)
            : await ResolveAsync(client, owner.Token, own.Id, own.Version, action, "Changed reason", operationId: operation);
        Assert.Contains("Operation.PayloadMismatch", await changedEvidence.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CurrentValidTill_UnrelatedHistoricalHold_CannotChooseSupplementInsteadOfDrawerCash()
    {
        using var client = CreateClient();
        var (owner, payload, session) = await ArrangeAsync(client);
        await using var app = new TillTestContext(fixture.MigratorConnectionString, owner.ShopId);
        var frozen = JsonSerializer.Deserialize<OfflineSaleSyncCommand>(payload.ToJsonString(), JsonSerializerOptions.Web)!;
        var lease = await app.Db.InvoiceLeases.SingleAsync(x => x.ShopId == owner.ShopId);
        var envelope = OfflineSubmission.Record(owner.ShopId, Device, frozen.ClientSaleId, frozen.InvoiceNumber,
            OfflineSaleSyncIdempotencyHasher.ComputeHash(owner.ShopId, Device, frozen), OfflineSubmissionPayload.Serialize(frozen),
            frozen.SoldAt, DateTimeOffset.UtcNow, OfflineSubmissionSource.Sync, owner.UserId, owner.UserId, owner.UserId, lease.Id, null);
        app.Db.OfflineSubmissions.Add(envelope);
        var issue = ReconciliationIssue.Create(owner.ShopId, null, frozen.ClientSaleId, Device, ReconciliationIssueType.IssueTimingReview,
            "OfflineReconciliation.HistoricalLeaseReviewRequired", "Preexisting historical hold", owner.UserId);
        issue.AttachEvidence(frozen.InvoiceNumber, envelope.Id);
        app.Db.ReconciliationIssues.Add(issue);
        await app.Db.SaveChangesAsync();
        using var refused = await ResolveTillAsync(client, owner, issue.Id, issue.Version, payload);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("Till.ReviewNotRequired", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.False(await app.Db.Sales.AnyAsync(x => x.ShopId == owner.ShopId));
        using var normal = await ResolveAsync(client, owner.Token, issue.Id, issue.Version, "RetryHistoricalLease", "Post to the original still open drawer");
        Assert.True(normal.IsSuccessStatusCode, await normal.Content.ReadAsStringAsync());
        Assert.False(await app.Db.TillLatePostings.AnyAsync(x => x.ShopId == owner.ShopId));
        var movement = await app.Db.TillMovements.SingleAsync(x => x.ShopId == owner.ShopId);
        Assert.Equal(session.Id, movement.SessionId);
        Assert.Equal(TillMovementSource.OfflineSale, movement.SourceType);
        Assert.Equal(118m, movement.SignedAmount);
    }

    [Fact]
    public async Task DisabledFirstSync_ReenabledTillReview_OrdinaryRetryIncludesOriginalDrawerOnce()
    {
        using var client = CreateClient();
        var (owner, payload, session) = await ArrangeAsync(client);
        var options = _factory.Services.GetRequiredService<IOptions<TillSessionOptions>>().Value;
        options.Enabled = false;
        var held = await SyncAsync(client, owner.Token, payload);
        var caseId = held.GetProperty("reviewCaseId").GetGuid();
        var detail = await GetCaseAsync(client, owner.Token, caseId);
        Assert.Equal("TillReview", detail.GetProperty("issueType").GetString());
        Assert.Contains("Till.Disabled", Strings(detail.GetProperty("reasonCodes")));
        Assert.Contains("RetryHistoricalLease", Strings(detail.GetProperty("availableActions")));
        options.Enabled = true;
        Assert.Equal(caseId, (await SyncAsync(client, owner.Token, payload)).GetProperty("reviewCaseId").GetGuid());
        await using var app = new TillTestContext(fixture.MigratorConnectionString, owner.ShopId);
        Assert.False(await app.Db.Sales.AnyAsync(x => x.ShopId == owner.ShopId));
        var original = await app.Db.OfflineSubmissions.AsNoTracking().SingleAsync(x => x.ShopId == owner.ShopId);
        using var unnecessary = await ResolveTillAsync(client, owner, caseId, detail.GetProperty("version").GetUInt32(), payload);
        Assert.Contains("Till.ReviewNotRequired", await unnecessary.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var operation = Guid.NewGuid().ToString();
        using var normal = await ResolveAsync(client, owner.Token, caseId, detail.GetProperty("version").GetUInt32(), "RetryHistoricalLease", "Re-enabled original drawer", operationId: operation);
        Assert.True(normal.IsSuccessStatusCode, await normal.Content.ReadAsStringAsync());
        var response = await normal.Content.ReadAsStringAsync();
        using var replay = await ResolveAsync(client, owner.Token, caseId, detail.GetProperty("version").GetUInt32(), "RetryHistoricalLease", "Re-enabled original drawer", operationId: operation);
        Assert.Equal(response, await replay.Content.ReadAsStringAsync());
        Assert.Equal("duplicate", (await SyncAsync(client, owner.Token, payload)).GetProperty("status").GetString());
        var sale = await app.Db.Sales.SingleAsync(x => x.ShopId == owner.ShopId);
        Assert.Equal(payload["invoiceNumber"]!.GetValue<string>(), sale.InvoiceNumber);
        AssertSameInstant(payload["soldAt"]!.GetValue<DateTimeOffset>(), sale.SoldAt);
        Assert.Equal(session.BusinessDate, sale.BusinessDate);
        Assert.Equal(original.PayloadHash, sale.RequestHash);
        Assert.Equal(original.PayloadJson, (await app.Db.OfflineSubmissions.AsNoTracking().SingleAsync(x => x.Id == original.Id)).PayloadJson);
        var source = await app.Db.TillSourceRecords.SingleAsync(x => x.ShopId == owner.ShopId);
        Assert.Equal(TillMovementSource.OfflineSale, source.SourceType);
        Assert.Equal(session.Id, source.SessionId);
        Assert.Equal(sale.Id, source.SourceId);
        Assert.Equal(118m, (await app.Db.TillMovements.SingleAsync(x => x.ShopId == owner.ShopId)).SignedAmount);
        Assert.False(await app.Db.TillLatePostings.AnyAsync(x => x.ShopId == owner.ShopId));
    }

    [Theory]
    [InlineData(PaymentMethod.Cash, ReceiptTenderMethod.Upi)]
    [InlineData(PaymentMethod.UPI, ReceiptTenderMethod.Card)]
    [InlineData(PaymentMethod.Card, ReceiptTenderMethod.Cash)]
    public async Task LegacyRecordedTender_MismatchedExternalMethod_Refused(PaymentMethod originalMethod, ReceiptTenderMethod actualMethod)
    {
        using var client = CreateClient();
        var (owner, payload, _) = await ArrangeAsync(client);
        RemoveTillEvidence(payload);
        payload["paymentMethod"] = (int)originalMethod;
        var held = await SyncAsync(client, owner.Token, payload);
        var caseId = held.GetProperty("reviewCaseId").GetGuid();
        var detail = await GetCaseAsync(client, owner.Token, caseId);
        var evidence = payload.DeepClone().AsObject();
        evidence["tenderLines"] = JsonSerializer.SerializeToNode(new[] { new { method = (int)actualMethod, appliedAmount = 118m } });
        using var refused = await ResolveTillAsync(client, owner, caseId, detail.GetProperty("version").GetUInt32(), evidence);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("Till.TenderMethodMismatch", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await using var app = new TillTestContext(fixture.MigratorConnectionString, owner.ShopId);
        Assert.False(await app.Db.Sales.AnyAsync(x => x.ShopId == owner.ShopId));
        Assert.False(await app.Db.TillLatePostings.AnyAsync(x => x.ShopId == owner.ShopId));
        Assert.Equal("Open", (await GetCaseAsync(client, owner.Token, caseId)).GetProperty("status").GetString());
    }

    [Theory]
    [InlineData(PaymentMethod.Cash, ReceiptTenderMethod.Cash)]
    [InlineData(PaymentMethod.UPI, ReceiptTenderMethod.Upi)]
    [InlineData(PaymentMethod.Card, ReceiptTenderMethod.Card)]
    public async Task LegacyRecordedTender_WrongAmount_Refused(PaymentMethod originalMethod, ReceiptTenderMethod actualMethod)
    {
        using var client = CreateClient();
        var (owner, payload, _) = await ArrangeAsync(client);
        RemoveTillEvidence(payload);
        payload["paymentMethod"] = (int)originalMethod;
        var held = await SyncAsync(client, owner.Token, payload);
        var caseId = held.GetProperty("reviewCaseId").GetGuid();
        var detail = await GetCaseAsync(client, owner.Token, caseId);
        var evidence = payload.DeepClone().AsObject();
        evidence["tenderLines"] = JsonSerializer.SerializeToNode(new[] { new { method = (int)actualMethod, appliedAmount = 117m } });
        using var refused = await ResolveTillAsync(client, owner, caseId, detail.GetProperty("version").GetUInt32(), evidence);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("Till.TenderAmountMismatch", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await using var app = new TillTestContext(fixture.MigratorConnectionString, owner.ShopId);
        Assert.False(await app.Db.Sales.AnyAsync(x => x.ShopId == owner.ShopId));
        Assert.False(await app.Db.TillLatePostings.AnyAsync(x => x.ShopId == owner.ShopId));
    }

    [Theory]
    [InlineData(PaymentMethod.Cash, ReceiptTenderMethod.Cash)]
    [InlineData(PaymentMethod.UPI, ReceiptTenderMethod.Upi)]
    [InlineData(PaymentMethod.Card, ReceiptTenderMethod.Card)]
    public async Task LegacyRecordedTender_MatchingMethod_PostsSeparateEvidence(PaymentMethod originalMethod, ReceiptTenderMethod actualMethod)
    {
        using var client = CreateClient();
        var (owner, payload, _) = await ArrangeAsync(client);
        RemoveTillEvidence(payload);
        payload["paymentMethod"] = (int)originalMethod;
        var held = await SyncAsync(client, owner.Token, payload);
        var caseId = held.GetProperty("reviewCaseId").GetGuid();
        var detail = await GetCaseAsync(client, owner.Token, caseId);
        await using var app = new TillTestContext(fixture.MigratorConnectionString, owner.ShopId);
        var frozen = await app.Db.OfflineSubmissions.AsNoTracking().SingleAsync(x => x.ShopId == owner.ShopId);
        var evidence = payload.DeepClone().AsObject();
        evidence["tenderLines"] = JsonSerializer.SerializeToNode(new[] { new { method = (int)actualMethod, appliedAmount = 118m } });
        using var resolved = await ResolveTillAsync(client, owner, caseId, detail.GetProperty("version").GetUInt32(), evidence);
        Assert.True(resolved.IsSuccessStatusCode, await resolved.Content.ReadAsStringAsync());
        var sale = await app.Db.Sales.SingleAsync(x => x.ShopId == owner.ShopId);
        var late = await app.Db.TillLatePostings.SingleAsync(x => x.ShopId == owner.ShopId);
        Assert.Equal(originalMethod, sale.PaymentMethod);
        Assert.Equal(118m, sale.PaidAmount);
        Assert.Null(sale.TenderLines);
        Assert.Equal(actualMethod, Assert.Single(late.TenderLines).Method);
        Assert.Equal(118m, late.TenderLines[0].AppliedAmount);
        Assert.Equal("Recorded", late.EvidenceStatus);
        Assert.False(late.IncludedInCount);
        Assert.Equal(frozen.PayloadJson, (await app.Db.OfflineSubmissions.AsNoTracking().SingleAsync(x => x.Id == frozen.Id)).PayloadJson);
        Assert.False(await app.Db.TillMovements.AnyAsync(x => x.ShopId == owner.ShopId));
    }

    [Theory]
    [InlineData("Unknown", 58, true)]
    [InlineData("Recorded", 58, true)]
    [InlineData("Recorded", 57, false)]
    public async Task LegacyCreditWithPaidAmount_PreservesAmbiguityAndConservesActualTender(string status, int actualAmount, bool accepted)
    {
        using var client = CreateClient();
        var (owner, payload, _) = await ArrangeAsync(client);
        await using var app = new TillTestContext(fixture.MigratorConnectionString, owner.ShopId);
        var customer = Customer.Create(owner.ShopId, "Legacy credit customer", "+919222222223", null, true);
        app.Db.Customers.Add(customer);
        await app.Db.SaveChangesAsync();
        RemoveTillEvidence(payload);
        payload["paymentMethod"] = (int)PaymentMethod.Credit;
        payload["customerId"] = customer.Id;
        payload["paidAmount"] = 58m;
        payload["dueAmount"] = 60m;
        var held = await SyncAsync(client, owner.Token, payload);
        var caseId = held.GetProperty("reviewCaseId").GetGuid();
        var detail = await GetCaseAsync(client, owner.Token, caseId);
        var frozen = await app.Db.OfflineSubmissions.AsNoTracking().SingleAsync(x => x.ShopId == owner.ShopId);
        var evidence = payload.DeepClone().AsObject();
        evidence["tenderLines"] = JsonSerializer.SerializeToNode(new[] { new { method = (int)ReceiptTenderMethod.BankTransfer, appliedAmount = (decimal)actualAmount } });
        using var resolved = await ResolveTillAsync(client, owner, caseId, detail.GetProperty("version").GetUInt32(), evidence, status);
        if (!accepted)
        {
            Assert.Equal(HttpStatusCode.Conflict, resolved.StatusCode);
            Assert.Contains("Till.TenderAmountMismatch", await resolved.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.False(await app.Db.Sales.AnyAsync(x => x.ShopId == owner.ShopId));
            Assert.False(await app.Db.TillLatePostings.AnyAsync(x => x.ShopId == owner.ShopId));
            Assert.Equal(frozen.PayloadJson, (await app.Db.OfflineSubmissions.AsNoTracking().SingleAsync(x => x.Id == frozen.Id)).PayloadJson);
            return;
        }
        Assert.True(resolved.IsSuccessStatusCode, await resolved.Content.ReadAsStringAsync());
        var sale = await app.Db.Sales.SingleAsync(x => x.ShopId == owner.ShopId);
        var late = await app.Db.TillLatePostings.SingleAsync(x => x.ShopId == owner.ShopId);
        Assert.Equal(PaymentMethod.Credit, sale.PaymentMethod);
        Assert.Equal(58m, sale.PaidAmount);
        Assert.Equal(60m, sale.DueAmount);
        Assert.Null(sale.TenderLines);
        Assert.Equal(status, late.EvidenceStatus);
        Assert.Equal("Verified payment evidence separately", late.EvidenceReason);
        if (status == "Unknown") Assert.Empty(late.TenderLines);
        else
        {
            Assert.Equal(ReceiptTenderMethod.BankTransfer, Assert.Single(late.TenderLines).Method);
            Assert.Equal(58m, late.TenderLines[0].AppliedAmount);
        }
        Assert.Equal(frozen.PayloadJson, (await app.Db.OfflineSubmissions.AsNoTracking().SingleAsync(x => x.Id == frozen.Id)).PayloadJson);
        Assert.False(late.IncludedInCount);
        Assert.False(await app.Db.TillMovements.AnyAsync(x => x.ShopId == owner.ShopId));
    }

    private static void RemoveTillEvidence(JsonObject payload)
    {
        foreach (var name in new[] { "till", "tenderLines", "tillBusinessDate", "tillCashierId" }) payload.Remove(name);
    }

    [Fact]
    public async Task StaffCannotReplayManagerAcknowledgment_ButOwnEscalationReplayRemainsAvailable()
    {
        using var client = CreateClient();
        var (owner, _, _) = await ArrangeAsync(client);
        using var staffClient = CreateClient();
        var staff = await AddManagerAsync(staffClient, owner, "Staff");
        await using var app = new TillTestContext(fixture.MigratorConnectionString, owner.ShopId);
        var own = ReconciliationIssue.Create(owner.ShopId, null, NewClientSaleId(), Device, ReconciliationIssueType.PricingVariance,
            "offline_sync.pricing_variance", "Own informational case", staff.UserId);
        app.Db.ReconciliationIssues.Add(own);
        await app.Db.SaveChangesAsync();
        var escalationKey = Guid.NewGuid().ToString();
        using var escalated = await ResolveAsync(staffClient, staff.Token, own.Id, own.Version, "Escalate", "Ask manager to review", operationId: escalationKey);
        Assert.True(escalated.IsSuccessStatusCode, await escalated.Content.ReadAsStringAsync());
        var escalationBody = await escalated.Content.ReadAsStringAsync();
        var current = JsonSerializer.Deserialize<JsonElement>(escalationBody);
        var managerKey = Guid.NewGuid().ToString();
        using var acknowledged = await ResolveAsync(client, owner.Token, own.Id, current.GetProperty("version").GetUInt32(), "AcknowledgeVariance", "Manager verified original prices", operationId: managerKey);
        Assert.True(acknowledged.IsSuccessStatusCode, await acknowledged.Content.ReadAsStringAsync());
        using var forbidden = await ResolveAsync(staffClient, staff.Token, own.Id, current.GetProperty("version").GetUInt32(), "AcknowledgeVariance", "Manager verified original prices", operationId: managerKey);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var ownReplay = await ResolveAsync(staffClient, staff.Token, own.Id, own.Version, "Escalate", "Ask manager to review", operationId: escalationKey);
        Assert.True(ownReplay.IsSuccessStatusCode, await ownReplay.Content.ReadAsStringAsync());
        Assert.Equal(escalationBody, await ownReplay.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TillReviewOrdinaryRetry_StillRecordsCustomerCreditVariance()
    {
        using var client = CreateClient();
        var (owner, payload, session) = await ArrangeAsync(client);
        await using var app = new TillTestContext(fixture.MigratorConnectionString, owner.ShopId);
        var customer = Customer.Create(owner.ShopId, "No credit customer", "+919222222224", null, true);
        Success(customer.SetCreditPolicy(CreditPolicyMode.NoCredit, null));
        app.Db.Customers.Add(customer);
        await app.Db.SaveChangesAsync();
        payload["customerId"] = customer.Id;
        payload["paidAmount"] = 58m;
        payload["dueAmount"] = 60m;
        payload["tenderLines"]![0]!["appliedAmount"] = 58m;
        var options = _factory.Services.GetRequiredService<IOptions<TillSessionOptions>>().Value;
        options.Enabled = false;
        var held = await SyncAsync(client, owner.Token, payload);
        var caseId = held.GetProperty("reviewCaseId").GetGuid();
        var detail = await GetCaseAsync(client, owner.Token, caseId);
        options.Enabled = true;
        using var resolved = await ResolveAsync(client, owner.Token, caseId, detail.GetProperty("version").GetUInt32(), "RetryHistoricalLease", "Recover original drawer posting");
        Assert.True(resolved.IsSuccessStatusCode, await resolved.Content.ReadAsStringAsync());
        var sale = await app.Db.Sales.SingleAsync(x => x.ShopId == owner.ShopId);
        var issues = await app.Db.ReconciliationIssues.Where(x => x.SaleId == sale.Id && x.IssueType == ReconciliationIssueType.CustomerVariance).ToArrayAsync();
        Assert.Contains(issues, x => x.Code == "offline_sync.credit_policy_violation");
        var movement = await app.Db.TillMovements.SingleAsync(x => x.ShopId == owner.ShopId);
        Assert.Equal(session.Id, movement.SessionId);
        Assert.Equal(58m, movement.SignedAmount);
        Assert.False(await app.Db.TillLatePostings.AnyAsync(x => x.ShopId == owner.ShopId));
    }

    [Theory]
    [InlineData("legacy")]
    [InlineData("till")]
    [InlineData("tenderLines")]
    [InlineData("tillBusinessDate")]
    [InlineData("tillCashierId")]
    public async Task TillReviewWithoutCompleteFrozenContext_DisableCannotTurnExplicitRetryIntoLegacyPosting(string missingField)
    {
        using var client = CreateClient();
        var (owner, payload, _) = await ArrangeAsync(client);
        if (missingField == "legacy") RemoveTillEvidence(payload);
        else payload.Remove(missingField);
        var held = await SyncAsync(client, owner.Token, payload);
        var caseId = held.GetProperty("reviewCaseId").GetGuid();
        var detail = await GetCaseAsync(client, owner.Token, caseId);
        Assert.Equal("TillReview", detail.GetProperty("issueType").GetString());
        _factory.Services.GetRequiredService<IOptions<TillSessionOptions>>().Value.Enabled = false;
        using var refused = await ResolveAsync(client, owner.Token, caseId, detail.GetProperty("version").GetUInt32(), "RetryHistoricalLease", "Forged ordinary retry must not fall through");
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("Reconciliation.ActionNotAllowed", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.DoesNotContain("RetryHistoricalLease", Strings(detail.GetProperty("availableActions")));
        Assert.Contains("ReconcileTillSale", Strings(detail.GetProperty("availableActions")));
        Assert.Equal(caseId, (await SyncAsync(client, owner.Token, payload)).GetProperty("reviewCaseId").GetGuid());
        await using var app = new TillTestContext(fixture.MigratorConnectionString, owner.ShopId);
        Assert.False(await app.Db.Sales.AnyAsync(x => x.ShopId == owner.ShopId));
        Assert.False(await app.Db.TillLatePostings.AnyAsync(x => x.ShopId == owner.ShopId));
        Assert.False(await app.Db.TillMovements.AnyAsync(x => x.ShopId == owner.ShopId));
    }

    [Fact]
    public async Task TillReviewOrdinaryRetry_CompleteZeroPaidContextAllowsEmptyTenderLines()
    {
        using var client = CreateClient();
        var (owner, payload, session) = await ArrangeAsync(client);
        await using var app = new TillTestContext(fixture.MigratorConnectionString, owner.ShopId);
        var customer = Customer.Create(owner.ShopId, "All due customer", "+919222222225", null, true);
        app.Db.Customers.Add(customer);
        await app.Db.SaveChangesAsync();
        payload["customerId"] = customer.Id;
        payload["paidAmount"] = 0m;
        payload["dueAmount"] = 118m;
        payload["tenderLines"] = new JsonArray();
        var options = _factory.Services.GetRequiredService<IOptions<TillSessionOptions>>().Value;
        options.Enabled = false;
        var held = await SyncAsync(client, owner.Token, payload);
        var caseId = held.GetProperty("reviewCaseId").GetGuid();
        var detail = await GetCaseAsync(client, owner.Token, caseId);
        Assert.Contains("RetryHistoricalLease", Strings(detail.GetProperty("availableActions")));
        options.Enabled = true;
        using var resolved = await ResolveAsync(client, owner.Token, caseId, detail.GetProperty("version").GetUInt32(), "RetryHistoricalLease", "Recover original all-due attribution");
        Assert.True(resolved.IsSuccessStatusCode, await resolved.Content.ReadAsStringAsync());
        var source = await app.Db.TillSourceRecords.SingleAsync(x => x.ShopId == owner.ShopId);
        Assert.Equal(session.Id, source.SessionId);
        Assert.Equal(TillMovementSource.OfflineSale, source.SourceType);
        Assert.Equal(118m, source.Settlement.ReceivableChange);
        Assert.False(await app.Db.TillMovements.AnyAsync(x => x.ShopId == owner.ShopId));
        Assert.False(await app.Db.TillLatePostings.AnyAsync(x => x.ShopId == owner.ShopId));
    }

    private static async Task<OwnerContext> AddManagerAsync(HttpClient client, OwnerContext owner, string role = "Manager")
    {
        var email = $"till-manager-{Guid.NewGuid():N}@test.com";
        const string password = "StaffPass1!";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/users");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", owner.Token);
        request.Content = JsonContent.Create(new { shopIds = new[] { owner.ShopId }, email, firstName = "Manager", lastName = "Two",
            phoneNumber = $"+91{Random.Shared.NextInt64(6_000_000_000, 9_999_999_999)}", password, confirmPassword = password, role });
        using var seeded = await client.SeedVerifiedStaffAsync(request);
        seeded.EnsureSuccessStatusCode();
        using var login = await client.PostAsJsonAsync("/api/auth/login/email", new { email, password });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
        return new(token, owner.ShopId, Guid.Parse(new JwtSecurityTokenHandler().ReadJwtToken(token).Subject));
    }

    private static Task<HttpResponseMessage> ResolveTillAsync(HttpClient client, OwnerContext owner, Guid caseId, uint version, JsonObject payload,
        string status = "Recorded", string? operation = null) =>
        SendAsync(client, HttpMethod.Post, $"{Route}/{caseId}/resolve", owner.Token, new
        {
            operationId = operation ?? Guid.NewGuid().ToString(), expectedVersion = version, action = "ReconcileTillSale", reason = "Reviewed historical invoice",
            tillReconciliationEvidence = new { evidenceStatus = status, tenderLines = status == "Unknown" ? new JsonArray() : payload["tenderLines"]?.DeepClone(), reason = "Verified payment evidence separately" }
        });

    private async Task<(OwnerContext Owner, JsonObject Payload, Intelibill.Application.Features.Tills.TillSessionDto Session)> ArrangeAsync(HttpClient client)
    {
        var owner = await CreateOwnerAsync(client);
        var stock = await AddInventoryAsync(client, owner.Token, 10m);
        var invoice = await FirstLeasedInvoiceNumberAsync(client, owner.Token);
        _factory.Services.GetRequiredService<IOptions<TillSessionOptions>>().Value.Enabled = true;
        _factory.Services.GetRequiredService<IOptions<TillSessionOptions>>().Value.AllowedShopIds = [owner.ShopId];
        await using var app = new TillTestContext(fixture.MigratorConnectionString, owner.ShopId, new() { Now = DateTimeOffset.UtcNow.AddSeconds(-2) });
        var till = await app.CreateAsync(owner.ShopId, owner.UserId);
        var session = await app.OpenAsync(owner.ShopId, owner.UserId, till.Id);
        var payload = JsonSerializer.SerializeToNode(Payload(NewClientSaleId(), invoice, stock, 1m, DateTimeOffset.UtcNow.AddSeconds(-1), owner.UserId))!.AsObject();
        payload["till"] = JsonSerializer.SerializeToNode(new { sessionId = session.Id });
        payload["tillBusinessDate"] = session.BusinessDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        payload["tillCashierId"] = owner.UserId;
        payload["tenderLines"] = JsonNode.Parse("""[{"method":2,"appliedAmount":118}]""");
        return (owner, payload, session);
    }

    private async Task CloseAsync(OwnerContext owner, Intelibill.Application.Features.Tills.TillSessionDto session)
    {
        await using var app = new TillTestContext(fixture.MigratorConnectionString, owner.ShopId, new() { Now = DateTimeOffset.UtcNow });
        var current = await app.Db.TillSessions.AsNoTracking().SingleAsync(x => x.Id == session.Id);
        Success(await app.Lifecycle.CloseAsync(new(owner.ShopId, owner.UserId, session.Id, 1000m, null, current.Version, Guid.NewGuid().ToString()), default));
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

    private static void AssertSameInstant(DateTimeOffset expected, DateTimeOffset actual) =>
        Assert.True(
            Math.Abs((expected - actual).TotalMilliseconds) < 1,
            $"Expected {expected:O} but got {actual:O}");

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
            gstNumber = TestTaxProfile.NewGstin(),
            taxProfile = TestTaxProfile.Regular,
        });
        shop.EnsureSuccessStatusCode();
        var shopToken = (await shop.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(shopToken);
        return new OwnerContext(
            shopToken,
            Guid.Parse(jwt.Claims.Single(c => c.Type == "active_shop_id").Value),
            Guid.Parse(jwt.Subject));
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

    private async Task<JsonElement> ReserveInvoiceLeaseAsync(HttpClient client, string token, string deviceId = Device)
    {
        await LegacyOfflineExpiryHistory.SeedAsync(_factory, token);
        using var response = await SendAsync(client, HttpMethod.Post, "/api/sales/invoice-leases/reserve", token, new { deviceId, blockSize = 25 });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static string InvoiceAt(JsonElement lease, int offset) =>
        $"{lease.GetProperty("prefix").GetString()}{(lease.GetProperty("rangeStart").GetInt32() + offset).ToString($"D{lease.GetProperty("numberPadding").GetInt32()}", System.Globalization.CultureInfo.InvariantCulture)}";

    private async Task<string> FirstLeasedInvoiceNumberAsync(HttpClient client, string token) =>
        InvoiceAt(await ReserveInvoiceLeaseAsync(client, token), 0);

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

    private sealed record OwnerContext(string Token, Guid ShopId, Guid UserId);
    private sealed record StockContext(string Barcode, Guid ItemId, Guid BatchId);
}
