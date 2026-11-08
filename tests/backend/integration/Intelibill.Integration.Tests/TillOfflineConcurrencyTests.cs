using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using static Intelibill.Integration.Tests.TillTestContext;

namespace Intelibill.Integration.Tests;

public sealed partial class TillOfflineReconciliationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CloseVersusOfflineSync_ForcedDatabaseOrdering_NoLostOrDoubledCash(bool closeWins)
    {
        using var client = CreateClient();
        var (owner, payload, session) = await ArrangeAsync(client);
        await using var app = new TillTestContext(fixture.MigratorConnectionString, owner.ShopId, new() { Now = DateTimeOffset.UtcNow });
        await using var barrier = await TillPostgresBarrier.CreateAsync(fixture.MigratorConnectionString, closeWins ? "till_closes" : "till_movements", owner.ShopId);
        Task<HttpResponseMessage> CloseDrawerAsync() => SendAsync(client, HttpMethod.Post, $"/api/tills/sessions/{session.Id}/close", owner.Token,
            new { countedCash = 1000m, expectedVersion = session.Version, operationKey = "barrier-close" });
        Task<JsonElement> sync;
        Task<HttpResponseMessage> close;
        if (closeWins)
        {
            close = CloseDrawerAsync();
            var firstPid = await barrier.WaitForArrivalAsync();
            sync = SyncAsync(client, owner.Token, payload);
            await barrier.WaitForBlockedByAsync(firstPid);
        }
        else
        {
            sync = SyncAsync(client, owner.Token, payload);
            var firstPid = await barrier.WaitForArrivalAsync();
            close = CloseDrawerAsync();
            await barrier.WaitForBlockedByAsync(firstPid);
        }
        Assert.False(sync.IsCompleted); Assert.False(close.IsCompleted);
        await barrier.ReleaseAsync();
        var synced = await sync.WaitAsync(TimeSpan.FromSeconds(15));
        using var closed = await close.WaitAsync(TimeSpan.FromSeconds(15));
        if (closeWins)
        {
            Assert.True(closed.IsSuccessStatusCode, await closed.Content.ReadAsStringAsync());
            Assert.Equal("NeedsReview", synced.GetProperty("status").GetString());
            var originalClose = await app.Db.TillCloses.AsNoTracking().SingleAsync(x => x.SessionId == session.Id);
            var frozen = JsonSerializer.Serialize(originalClose);
            var envelope = await app.Db.OfflineSubmissions.AsNoTracking().SingleAsync(x => x.ShopId == owner.ShopId);
            var payloadBytes = envelope.PayloadJson;
            var detail = await GetCaseAsync(client, owner.Token, synced.GetProperty("reviewCaseId").GetGuid());
            using var reviewed = await ResolveTillAsync(client, owner, detail.GetProperty("caseId").GetGuid(), detail.GetProperty("version").GetUInt32(), payload);
            Assert.True(reviewed.IsSuccessStatusCode, await reviewed.Content.ReadAsStringAsync());
            var late = await app.Db.TillLatePostings.SingleAsync(x => x.ShopId == owner.ShopId);
            Assert.Equal(originalClose.Id, late.CloseId); Assert.False(late.IncludedInCount);
            Assert.Equal(118m, Assert.Single(late.TenderLines).AppliedAmount);
            Assert.False(await app.Db.TillMovements.AnyAsync(x => x.ShopId == owner.ShopId));
            Assert.Equal(frozen, JsonSerializer.Serialize(await app.Db.TillCloses.AsNoTracking().SingleAsync(x => x.SessionId == session.Id)));
            Assert.Equal(payloadBytes, (await app.Db.OfflineSubmissions.AsNoTracking().SingleAsync(x => x.Id == envelope.Id)).PayloadJson);
        }
        else
        {
            Assert.True(synced.GetProperty("status").GetString() is "created" or "SyncedWithWarnings");
            Assert.Equal(HttpStatusCode.Conflict, closed.StatusCode);
            Assert.Contains("Till.VersionConflict", await closed.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            var movement = await app.Db.TillMovements.SingleAsync(x => x.SessionId == session.Id);
            Assert.Equal(118m, movement.SignedAmount);
            Assert.False(await app.Db.TillLatePostings.AnyAsync(x => x.ShopId == owner.ShopId));
            app.Db.ChangeTracker.Clear();
            var current = Success(await app.Reads.SessionAsync(owner.ShopId, owner.UserId, session.Id, 1, 20, default)).Session;
            var saved = Success(await app.Lifecycle.CloseAsync(new(owner.ShopId, owner.UserId, session.Id, 1118m, null, current.Version, "fresh-close"), default));
            Assert.Equal(1118m, saved.Close!.ExpectedCash); Assert.Equal(1, saved.Close.Totals.MovementCount);
        }
        var sale = await app.Db.Sales.AsNoTracking().SingleAsync(x => x.ShopId == owner.ShopId);
        Assert.Equal(payload["invoiceNumber"]!.GetValue<string>(), sale.InvoiceNumber);
        AssertSameInstant(payload["soldAt"]!.GetValue<DateTimeOffset>(), sale.SoldAt);
        Assert.Equal(session.BusinessDate, sale.BusinessDate);
        Assert.Equal(9m, (await app.Db.InventoryBatches.SingleAsync(x => x.ShopId == owner.ShopId)).Quantity);
    }

    [Fact]
    public async Task NormalSyncVersusManagerReview_ReadyOnSameSessionLock_ExactlyOneNormalSource()
    {
        using var client = CreateClient();
        var (owner, payload, session) = await ArrangeAsync(client);
        await using var app = new TillTestContext(fixture.MigratorConnectionString, owner.ShopId);
        // The normal writer has passed its hold-case preflight and owns the till before INSERT.
        await using var barrier = await TillPostgresBarrier.CreateAsync(fixture.MigratorConnectionString, "till_movements", owner.ShopId);
        var sync = SyncAsync(client, owner.Token, payload);
        var syncPid = await barrier.WaitForArrivalAsync();
        var envelope = await app.Db.OfflineSubmissions.SingleAsync(x => x.ShopId == owner.ShopId);
        var originalBytes = envelope.PayloadJson;
        var issue = ReconciliationIssue.Create(owner.ShopId, null, envelope.ClientSaleId, Device,
            ReconciliationIssueType.TillReview, "Till.ReviewRequired", "Independent earlier review", owner.UserId);
        issue.AttachEvidence(envelope.InvoiceNumber, envelope.Id);
        app.Db.ReconciliationIssues.Add(issue); await app.Db.SaveChangesAsync();
        var review = ResolveTillAsync(client, owner, issue.Id, issue.Version, payload);
        await barrier.WaitForBlockedByAsync(syncPid);
        Assert.False(sync.IsCompleted); Assert.False(review.IsCompleted);
        await barrier.ReleaseAsync();
        var result = await sync.WaitAsync(TimeSpan.FromSeconds(15));
        using var reviewed = await review.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.True(result.GetProperty("status").GetString() is "created" or "SyncedWithWarnings");
        Assert.True(reviewed.IsSuccessStatusCode || reviewed.StatusCode == HttpStatusCode.Conflict, await reviewed.Content.ReadAsStringAsync());
        Assert.Equal(1, await app.Db.Sales.CountAsync(x => x.ShopId == owner.ShopId));
        var movement = await app.Db.TillMovements.SingleAsync(x => x.SessionId == session.Id);
        Assert.Equal(118m, movement.SignedAmount);
        Assert.Equal(1, await app.Db.TillSourceRecords.CountAsync(x => x.SessionId == session.Id));
        Assert.False(await app.Db.TillLatePostings.AnyAsync(x => x.ShopId == owner.ShopId));
        Assert.Equal(originalBytes, (await app.Db.OfflineSubmissions.AsNoTracking().SingleAsync(x => x.Id == envelope.Id)).PayloadJson);
        var sale = await app.Db.Sales.SingleAsync(x => x.ShopId == owner.ShopId);
        Assert.Equal(envelope.InvoiceNumber, sale.InvoiceNumber); AssertSameInstant(envelope.ClaimedSoldAt, sale.SoldAt);
        Assert.Equal(session.BusinessDate, sale.BusinessDate);
        Assert.Equal(9m, (await app.Db.InventoryBatches.SingleAsync(x => x.ShopId == owner.ShopId)).Quantity);
    }
    [Fact]
    public async Task ManagerReviewVersusSync_ReviewInFlight_ExactlyOneExcludedSupplement()
    {
        using var client = CreateClient();
        var (owner, payload, session) = await ArrangeAsync(client);
        await CloseAsync(owner, session);
        var held = await SyncAsync(client, owner.Token, payload);
        var caseId = held.GetProperty("reviewCaseId").GetGuid();
        var detail = await GetCaseAsync(client, owner.Token, caseId);
        await using var app = new TillTestContext(fixture.MigratorConnectionString, owner.ShopId);
        var frozenClose = JsonSerializer.Serialize(await app.Db.TillCloses.AsNoTracking().SingleAsync(x => x.SessionId == session.Id));
        var envelope = await app.Db.OfflineSubmissions.AsNoTracking().SingleAsync(x => x.ShopId == owner.ShopId);
        await using var barrier = await TillPostgresBarrier.CreateAsync(fixture.MigratorConnectionString, "till_late_postings", owner.ShopId);
        var review = ResolveTillAsync(client, owner, caseId, detail.GetProperty("version").GetUInt32(), payload);
        var reviewPid = await barrier.WaitForArrivalAsync();
        // The review owns the envelope/case/session and has staged its real sale + late fact.
        var resending = SyncAsync(client, owner.Token, payload);
        await barrier.WaitForBlockedByAsync(reviewPid);
        Assert.False(resending.IsCompleted); Assert.False(review.IsCompleted);
        await barrier.ReleaseAsync();
        var resend = await resending.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal("duplicate", resend.GetProperty("status").GetString());
        using var reviewed = await review.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.True(reviewed.IsSuccessStatusCode, await reviewed.Content.ReadAsStringAsync());
        Assert.Equal("duplicate", (await SyncAsync(client, owner.Token, payload)).GetProperty("status").GetString());
        var sale = await app.Db.Sales.AsNoTracking().SingleAsync(x => x.ShopId == owner.ShopId);
        var late = await app.Db.TillLatePostings.SingleAsync(x => x.ShopId == owner.ShopId);
        Assert.Equal(sale.Id, late.SaleId); Assert.Equal(caseId, late.CaseId); Assert.False(late.IncludedInCount);
        Assert.False(await app.Db.TillSourceRecords.AnyAsync(x => x.ShopId == owner.ShopId));
        Assert.False(await app.Db.TillMovements.AnyAsync(x => x.ShopId == owner.ShopId));
        Assert.Equal(frozenClose, JsonSerializer.Serialize(await app.Db.TillCloses.AsNoTracking().SingleAsync(x => x.SessionId == session.Id)));
        Assert.Equal(envelope.PayloadJson, (await app.Db.OfflineSubmissions.AsNoTracking().SingleAsync(x => x.Id == envelope.Id)).PayloadJson);
        Assert.Equal(envelope.InvoiceNumber, sale.InvoiceNumber); AssertSameInstant(envelope.ClaimedSoldAt, sale.SoldAt);
        Assert.Equal(session.BusinessDate, sale.BusinessDate);
        Assert.Equal(9m, (await app.Db.InventoryBatches.SingleAsync(x => x.ShopId == owner.ShopId)).Quantity);
    }
}
