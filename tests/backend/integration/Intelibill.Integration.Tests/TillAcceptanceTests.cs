using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Intelibill.Application.Features.Tills;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Intelibill.Integration.Tests;

public sealed partial class SaleCancellationTests
{
    private static readonly JsonSerializerOptions TillApiJson = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExplicitOpeningAndClosingCount_IsRequiredAndZeroIsValid(bool opening)
    {
        var s = await SeedAsync();
        var options = factory.Services.GetRequiredService<IOptions<TillSessionOptions>>().Value;
        options.Enabled = true; options.AllowedShopIds = [s.ShopId];
        await TillTestContext.OptInAsync(fixture.MigratorConnectionString, s.ShopId);
        using var created = await s.Client.PostAsJsonAsync("/api/tills", new { name = "Explicit count", operationKey = "create-count" });
        created.EnsureSuccessStatusCode();
        var till = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var route = $"/api/tills/{till}/sessions";
        var body = new JsonObject { ["cashierId"] = s.ActorId, ["operationKey"] = "explicit-count" };
        if (!opening)
        {
            using var opened = await s.Client.PostAsJsonAsync(route, new { cashierId = s.ActorId, operationKey = "open-zero", openingFloat = 0m });
            opened.EnsureSuccessStatusCode();
            var session = await opened.Content.ReadFromJsonAsync<JsonElement>();
            route = $"/api/tills/sessions/{session.GetProperty("id").GetGuid()}/close";
            body = new() { ["expectedVersion"] = session.GetProperty("version").GetUInt32(), ["operationKey"] = "explicit-count" };
        }
        using var missing = await s.Client.PostAsJsonAsync(route, body);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        body[opening ? "openingFloat" : "countedCash"] = 0m;
        using var zero = await s.Client.PostAsJsonAsync(route, body);
        Assert.True(zero.IsSuccessStatusCode, await zero.Content.ReadAsStringAsync());
    }
    [Fact]
    public async Task ExactDrawerAcceptance_RealSources_Frozen1250Count1240_Upi700Separate()
    {
        var s = await SeedAsync();
        var options = factory.Services.GetRequiredService<IOptions<TillSessionOptions>>().Value;
        options.Enabled = true; options.AllowedShopIds = [s.ShopId];
        await TillTestContext.OptInAsync(fixture.MigratorConnectionString, s.ShopId);
        await using var app = new TillTestContext(fixture.MigratorConnectionString, s.ShopId,
            new TillTestClock { Now = factory.Services.GetRequiredService<Intelibill.Application.Common.Time.IBusinessClock>().UtcNow });
        var shop = await app.Db.Shops.SingleAsync(x => x.Id == s.ShopId);
        TillTestContext.Success(shop.DeclareTaxProfile(null, "29", TaxRegime.Unregistered, null, "B", EInvoiceStatus.BelowThreshold, s.ActorId, app.Clock.UtcNow));
        await app.Db.SaveChangesAsync();
        using var tillResponse = await s.Client.PostAsJsonAsync("/api/tills", new { name = "Acceptance", operationKey = "accept-create" });
        tillResponse.EnsureSuccessStatusCode();
        var till = (await tillResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var openResponse = await s.Client.PostAsJsonAsync($"/api/tills/{till}/sessions", new { cashierId = s.ActorId, openingFloat = 1000m, operationKey = "accept-open" });
        openResponse.EnsureSuccessStatusCode();
        var session = (await openResponse.Content.ReadFromJsonAsync<TillSessionDto>(TillApiJson))!;
        var item = await app.Db.Items.SingleAsync(x => x.ShopId == s.ShopId);
        object SaleRequest(string method, decimal amount, decimal quantity, string key) => new
        {
            idempotencyKey = key, customerId = s.CustomerId, paymentMethod = method, paidAmount = amount, dueAmount = 0m,
            till = new { sessionId = session.Id }, tenderLines = new[] { new { method, appliedAmount = amount } },
            items = new[] { new { barcode = item.Barcode, batchNumber = "LOT", itemName = "Sugar", quantity,
                costPrice = 60m, salesPrice = 100m, mrp = 100m, taxRatePercent = 0m, isPriceIncludingTax = false, inventoryBatchId = s.BatchId } }
        };
        using var cashSale = await s.Client.PostAsJsonAsync("/api/sales", SaleRequest("Cash", 500m, 5m, "accept-sale"));
        Assert.True(cashSale.IsSuccessStatusCode, await cashSale.Content.ReadAsStringAsync());
        var saleId = (await cashSale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("saleId").GetGuid();
        using var collection = await s.Client.PostAsJsonAsync($"/api/customers/{s.CustomerId}/payments", new { amount = 200m, paymentDate = session.BusinessDate, method = "Cash", operationKey = "accept-collection", till = new { sessionId = session.Id } });
        Assert.True(collection.IsSuccessStatusCode, await collection.Content.ReadAsStringAsync());
        var receiptId = (await collection.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("receiptId").GetGuid();
        var sale = await app.Db.Sales.AsNoTracking().Include(x => x.Items).SingleAsync(x => x.Id == saleId);
        var saleBytes = JsonSerializer.Serialize(sale.InvoiceDocument);
        using var refund = await s.Client.PostAsJsonAsync($"/api/sales/{saleId}/returns", new { operationKey = "accept-refund", till = new { sessionId = session.Id }, payoutDestination = "Refund", refundConfirmed = true,
            refundTender = new { method = "Cash", appliedAmount = 100m }, items = new[] { new { saleItemId = sale.Items[0].Id, quantity = 1m, condition = "Restockable" } } });
        Assert.True(refund.IsSuccessStatusCode, await refund.Content.ReadAsStringAsync());
        using var expense = await s.Client.PostAsJsonAsync("/api/expenses", new { categoryName = "Cleaning", paidTo = "Cleaner", amount = 50m, expenseDate = session.BusinessDate, operationKey = "accept-expense", till = new { sessionId = session.Id }, tenderLine = new { method = "Cash", appliedAmount = 50m } });
        Assert.True(expense.IsSuccessStatusCode, await expense.Content.ReadAsStringAsync());
        var expenseId = (await expense.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var current = (await s.Client.GetFromJsonAsync<TillSessionDetailDto>($"/api/tills/sessions/{session.Id}", TillApiJson))!.Session;
        using var transfer = await s.Client.PostAsJsonAsync($"/api/tills/sessions/{session.Id}/cash-movements", new { direction = -1m, amount = 300m, reason = "Cash deposited in safe", expectedVersion = current.Version, operationKey = "accept-transfer" });
        Assert.True(transfer.IsSuccessStatusCode, await transfer.Content.ReadAsStringAsync());
        // Use seven units from another lot: this is a real second sale, not a staged till source.
        var batch = Intelibill.Domain.Entities.InventoryBatch.Create(s.ShopId, item.Id, "UPI-LOT", 7m, 60m, 100m, 100m, 0m, false, null, null, null, s.ActorId).Value;
        app.Db.InventoryBatches.Add(batch);
        var inventory = await app.Db.Inventory.SingleAsync(x => x.ItemId == item.Id);
        TillTestContext.Success(inventory.AddQuantity(7m, s.ActorId));
        await app.Db.SaveChangesAsync();
        var upiPayload = JsonSerializer.SerializeToNode(SaleRequest("Upi", 700m, 7m, "accept-upi"))!.AsObject();
        upiPayload["items"]![0]!["batchNumber"] = "UPI-LOT"; upiPayload["items"]![0]!["inventoryBatchId"] = batch.Id;
        using var upi = await s.Client.PostAsJsonAsync("/api/sales", upiPayload);
        Assert.True(upi.IsSuccessStatusCode, await upi.Content.ReadAsStringAsync());
        current = (await s.Client.GetFromJsonAsync<TillSessionDetailDto>($"/api/tills/sessions/{session.Id}", TillApiJson))!.Session;
        Assert.Equal(1250m, current.ExpectedCash);
        using var closeResponse = await s.Client.PostAsJsonAsync($"/api/tills/sessions/{session.Id}/close", new { countedCash = 1240m, reason = "Physical count short ten", expectedVersion = current.Version, operationKey = "accept-close" });
        Assert.True(closeResponse.IsSuccessStatusCode, await closeResponse.Content.ReadAsStringAsync());
        var closed = (await closeResponse.Content.ReadFromJsonAsync<TillSessionDto>(TillApiJson))!;
        Assert.Equal(1250m, closed.Close!.ExpectedCash); Assert.Equal(1240m, closed.Close.CountedCash); Assert.Equal(-10m, closed.Close.Variance);
        Assert.Equal(700m, closed.Close.Totals.RecordedTenderTotals[ReceiptTenderMethod.Upi]);
        Assert.Equal(250m, closed.Close.Totals.RecordedTenderTotals[ReceiptTenderMethod.Cash]);
        Assert.Equal(6, closed.Close.Totals.MovementCount);
        var sources = await app.Db.TillSourceRecords.AsNoTracking().Where(x => x.SessionId == session.Id).ToArrayAsync();
        Assert.Equal(6, sources.Length);
        Assert.Contains(sources, x => x.SourceId == saleId && x.SourceType == TillMovementSource.OnlineSale);
        Assert.Contains(sources, x => x.SourceId == receiptId && x.SourceType == TillMovementSource.CustomerPayment);
        Assert.Contains(sources, x => x.SourceId == expenseId && x.SourceType == TillMovementSource.Expense);
        var returnId = await app.Db.SaleReturns.Where(x => x.SaleId == saleId).Select(x => x.Id).SingleAsync();
        Assert.Contains(sources, x => x.SourceId == returnId && x.SourceType == TillMovementSource.SaleReturn);
        var frozen = JsonSerializer.Serialize(closed.Close);
        options.Enabled = false;
        using var replay = await s.Client.PostAsJsonAsync("/api/sales", SaleRequest("Cash", 500m, 5m, "accept-sale"));
        replay.EnsureSuccessStatusCode();
        var fetched = (await s.Client.GetFromJsonAsync<TillSessionDetailDto>($"/api/tills/sessions/{session.Id}", TillApiJson))!;
        Assert.Equal(frozen, JsonSerializer.Serialize(fetched.Session.Close));
        Assert.Equal(6, fetched.Sources.TotalCount);
        Assert.Equal(saleBytes, JsonSerializer.Serialize((await app.Db.Sales.AsNoTracking().SingleAsync(x => x.Id == saleId)).InvoiceDocument));
        var immutable = await Assert.ThrowsAsync<PostgresException>(() => app.Db.Database.ExecuteSqlAsync($"UPDATE till_source_records SET operation_key='changed' WHERE source_id={saleId}"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, immutable.SqlState);
    }
    [Fact]
    public async Task ReturnEvidence_DatabaseRefusesMutationDeletionAndInvalidLateAttachment()
    {
        var (s, app, session) = await TillReturnSetupAsync(); await using var owned = app;
        using var returned = await s.Client.PostAsJsonAsync($"/api/sales/{s.Sale.Id}/returns", ReturnRequest(s, session.Id));
        Assert.True(returned.IsSuccessStatusCode, await returned.Content.ReadAsStringAsync());
        var id = await app.Db.SaleReturns.Select(x => x.Id).SingleAsync();
        async Task RejectAsync(string sql)
        {
            await using var connection = new NpgsqlConnection(fixture.MigratorConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        }
        await RejectAsync($"UPDATE sale_returns SET refund_confirmed=false WHERE id='{id}'");
        await RejectAsync($"UPDATE sale_returns SET refund_tender='{{}}'::jsonb WHERE id='{id}'");
        await RejectAsync($"DELETE FROM sale_returns WHERE id='{id}'");
        await RejectAsync($"UPDATE sale_returns SET void_settlement_evidence='{{}}'::jsonb WHERE id='{id}'");
        using var voided = await s.Client.PostAsJsonAsync($"/api/sales/returns/{id}/void", new { reason = "Incorrect return", operationKey = "evidence-void", settlementEvidence = new { disposition = "NoPhysicalMovement", reason = "No cash recovered" } });
        Assert.True(voided.IsSuccessStatusCode, await voided.Content.ReadAsStringAsync());
        var saved = await app.Db.Database.SqlQuery<string>($"SELECT void_settlement_evidence::text AS \"Value\" FROM sale_returns WHERE id={id}").SingleAsync();
        await RejectAsync($"UPDATE sale_returns SET void_settlement_evidence=NULL WHERE id='{id}'");
        await RejectAsync($"UPDATE sale_returns SET void_settlement_evidence='{{}}'::jsonb WHERE id='{id}'");
        await RejectAsync($"DELETE FROM sale_returns WHERE id='{id}'");
        Assert.Equal(saved, await app.Db.Database.SqlQuery<string>($"SELECT void_settlement_evidence::text AS \"Value\" FROM sale_returns WHERE id={id}").SingleAsync());
    }

    [Fact]
    public async Task AlreadyVoidedLegacyReturn_CannotAttachEvidenceLater()
    {
        var s = await SeedAsync();
        using var returned = await s.Client.PostAsJsonAsync($"/api/sales/{s.Sale.Id}/returns", new { payoutDestination = "Refund", items = new[] { new { saleItemId = s.Sale.Items[0].Id, quantity = 2m, condition = "Restockable" } } });
        Assert.True(returned.IsSuccessStatusCode, await returned.Content.ReadAsStringAsync());
        await using var app = new TillTestContext(fixture.MigratorConnectionString, s.ShopId);
        var id = await app.Db.SaleReturns.Select(x => x.Id).SingleAsync();
        using var voided = await s.Client.PostAsJsonAsync($"/api/sales/returns/{id}/void", new { reason = "Legacy correction" });
        Assert.True(voided.IsSuccessStatusCode, await voided.Content.ReadAsStringAsync());
        var error = await Assert.ThrowsAsync<PostgresException>(() => app.Db.Database.ExecuteSqlAsync($"UPDATE sale_returns SET void_settlement_evidence='{{}}'::jsonb WHERE id={id}"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }
    [Fact]
    public async Task TillHttpBoundaries_RejectForeignCashierTenderAndForgedFinancialSource()
    {
        var (s, app, session) = await TillReturnSetupAsync(); await using var owned = app;
        var foreign = await TillTestContext.SeedAsync(fixture.MigratorConnectionString);
        await using var other = new TillTestContext(fixture.MigratorConnectionString, foreign.ShopId, new() { Now = app.Clock.UtcNow });
        var otherSession = await other.OpenAsync(foreign.ShopId, foreign.Owner, (await other.CreateAsync(foreign.ShopId, foreign.Owner)).Id);
        using var hidden = await s.Client.GetAsync($"/api/tills/sessions/{otherSession.Id}");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        using var foreignWrite = await s.Client.PostAsJsonAsync($"/api/tills/sessions/{otherSession.Id}/cash-movements", new { direction = 1m, amount = 1m, reason = "Forged tenant", expectedVersion = otherSession.Version, operationKey = "foreign-write" });
        Assert.Equal(HttpStatusCode.NotFound, foreignWrite.StatusCode);
        using var foreignCashier = await s.Client.PostAsJsonAsync($"/api/tills/{session.TillId}/sessions", new { cashierId = foreign.Owner, openingFloat = 0m, operationKey = "foreign-cashier", overrideReason = "Forged cashier" });
        Assert.Equal(HttpStatusCode.Forbidden, foreignCashier.StatusCode);
        var refund = ReturnRequest(s, session.Id); refund["refundTender"]!["method"] = 999;
        using var unknownTender = await s.Client.PostAsJsonAsync($"/api/sales/{s.Sale.Id}/returns", refund);
        Assert.Equal(HttpStatusCode.BadRequest, unknownTender.StatusCode);
        using var unknownDirection = await s.Client.PostAsJsonAsync($"/api/tills/sessions/{session.Id}/cash-movements", new { direction = 999m, amount = 1m, reason = "Forged enum", expectedVersion = session.Version, operationKey = "invalid-direction" });
        Assert.Equal(HttpStatusCode.BadRequest, unknownDirection.StatusCode);
        Assert.False(await app.Db.TillSourceRecords.AnyAsync(x => x.ShopId == s.ShopId));
        using var sourceClaim = await s.Client.PostAsJsonAsync($"/api/tills/sessions/{session.Id}/cash-movements", new { direction = 1m, amount = 1m, reason = "Actual manual cash", expectedVersion = session.Version, operationKey = "manual-owned-source", sourceType = "OnlineSale", sourceId = s.Sale.Id });
        Assert.True(sourceClaim.IsSuccessStatusCode, await sourceClaim.Content.ReadAsStringAsync());
        var source = await app.Db.TillSourceRecords.SingleAsync(x => x.ShopId == s.ShopId);
        Assert.Equal(TillMovementSource.ManualCash, source.SourceType);
        Assert.NotEqual(s.Sale.Id, source.SourceId);
        Assert.False(await app.Db.TillMovements.AnyAsync(x => x.ShopId == foreign.ShopId));
    }
}
