using ErrorOr;
using Intelibill.Application.Features.FinancialOperations.Queries.GetFinancialOperation;
using Intelibill.Domain.Common;
using System.Net;
using System.Net.Http.Json;
using Intelibill.Domain.Enums;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Intelibill.Integration.Tests;

public sealed partial class P0413aLicensingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Return_fences_licence_before_period(bool keyed)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        var binding = await BindAsync(shop);
        var end = _clock.Now.AddMinutes(5);
        await ApplyAsync(binding, end);
        await EnforceAsync(shop);
        var sale = await SellAsync(client);
        _clock.Now = end;

        await using var holder = Open(Migrator);
        await using var period = await holder.Database.BeginTransactionAsync(Ct);
        await holder.Database.ExecuteSqlAsync($"SELECT id FROM posting_periods WHERE shop_id = {shop} FOR UPDATE", Ct);
        var blocker = ((NpgsqlConnection)holder.Database.GetDbConnection()).ProcessID;
        var request = ReturnAsync(client, sale, ReturnInput(sale.Line, keyed ? Guid.NewGuid().ToString() : null));
        Task? projection = null;
        try
        {
            var returning = await WaitForLockAsync(request, blocker);
            projection = ApplyAsync(binding, end.AddHours(1), revision: 2, restriction: "Security");
            await WaitForLockAsync(projection, returning);
        }
        finally
        {
            await period.RollbackAsync(Ct);
            if (projection is not null) await projection;
        }

        using var saved = await request;
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        await AssertProblemAsync(await ReturnAsync(client, sale, ReturnInput(sale.Line)), HttpStatusCode.Forbidden, "Licence.SecuritySuspended");
    }

    [Fact]
    public async Task Prior_sale_returns_at_expiry_preserve_invoice_and_settlements()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        var binding = await BindAsync(shop);
        var end = _clock.Now.AddMinutes(5);
        await ApplyAsync(binding, end);
        await EnforceAsync(shop);
        var sale = await SellAsync(client);
        using var original = await client.GetAsync($"/api/sales/{sale.Id}", Ct);
        var before = await ReadSuccessAsync(original);
        await using var db = Open(Migrator);
        var document = await db.Database.SqlQuery<string>(
            $"SELECT to_jsonb(s)::text AS \"Value\" FROM sales s WHERE s.id = {sale.Id}").SingleAsync(Ct);
        var originalLine = await db.Database.SqlQuery<string>(
            $"SELECT to_jsonb(i)::text AS \"Value\" FROM sale_items i WHERE i.id = {sale.Line}").SingleAsync(Ct);
        var originalMovements = await db.StockTransactions.Where(x => x.InventoryBatchId == sale.Batch)
            .Select(x => x.Id).ToListAsync(Ct);

        _clock.Now = end;
        using var preview = await ReturnAsync(client, sale, ReturnInput(sale.Line), preview: true);
        Assert.Equal(100m, (await ReadSuccessAsync(preview)).GetProperty("financial").GetProperty("payoutAmount").GetDecimal());
        using var posted = await ReturnAsync(client, sale, ReturnInput(sale.Line, Guid.NewGuid().ToString()));
        var after = await ReadSuccessAsync(posted);
        foreach (var field in new[] { "invoiceNumber", "document", "soldAt", "totalAmount", "paidAmount", "dueAmount" })
            Assert.True(JsonElement.DeepEquals(before.GetProperty(field), after.GetProperty(field)), field);
        Assert.Equal(1m, after.GetProperty("items")[0].GetProperty("returnableQuantity").GetDecimal());
        Assert.Equal(document, await db.Database.SqlQuery<string>(
            $"SELECT to_jsonb(s)::text AS \"Value\" FROM sales s WHERE s.id = {sale.Id}").SingleAsync(Ct));
        Assert.Equal(originalLine, await db.Database.SqlQuery<string>(
            $"SELECT to_jsonb(i)::text AS \"Value\" FROM sale_items i WHERE i.id = {sale.Line}").SingleAsync(Ct));
        Assert.Equal(9m, await db.InventoryBatches.Where(x => x.Id == sale.Batch).Select(x => x.Quantity).SingleAsync(Ct));

        var saved = await db.SaleReturns.Include(x => x.Items).Include(x => x.Settlements)
            .SingleAsync(x => x.SaleId == sale.Id, Ct);
        Assert.Equal(end, saved.ProcessedAt);
        Assert.Equal(100m, saved.TotalRefundAmount);
        Assert.Equal(0m, saved.TotalTaxAmount);
        var leg = Assert.Single(saved.Settlements);
        Assert.Equal(ReturnSettlementKind.ExternalRefund, leg.Kind);
        Assert.Equal(100m, leg.Amount);
        Assert.Equal(sale.Line, Assert.Single(saved.Items).SaleItemId);
        var moves = await db.StockTransactions.Where(x => x.InventoryBatchId == sale.Batch).ToListAsync(Ct);
        Assert.All(originalMovements, id => Assert.Contains(moves, x => x.Id == id));
        Assert.Equal(1m, Assert.Single(moves, x => !originalMovements.Contains(x.Id)).Quantity);
    }

    [Fact]
    public async Task Restricted_returns_reject_sales_at_or_after_known_cutoff()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        var binding = await BindAsync(shop);
        var end = _clock.Now.AddMinutes(5);
        await ApplyAsync(binding, end);
        _clock.Now = end;
        var cutOffSale = await SellAsync(client);
        var key = Guid.NewGuid().ToString();
        await EnforceAsync(shop);

        await AssertProblemAsync(await ReturnAsync(client, cutOffSale, ReturnInput(cutOffSale.Line, key), preview: true),
            HttpStatusCode.Conflict, "SaleReturn.SourceIneligible");
        await AssertProblemAsync(await ReturnAsync(client, cutOffSale, ReturnInput(cutOffSale.Line, key)),
            HttpStatusCode.Conflict, "SaleReturn.SourceIneligible");
        await using var db = Open(Migrator);
        Assert.False(await db.SaleReturns.AnyAsync(x => x.ShopId == shop, Ct));
        Assert.False(await db.OperationReceipts.AnyAsync(x => x.ShopId == shop && x.OperationKey == key, Ct));
        Assert.Equal(8m, await db.InventoryBatches.Where(x => x.Id == cutOffSale.Batch).Select(x => x.Quantity).SingleAsync(Ct));
    }

    [Fact]
    public async Task Restricted_returns_use_only_the_original_remaining_line()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var owner = CreateClient(host);
        using var manager = CreateClient(host);
        using var foreign = CreateClient(host);
        var shop = await CreateShopAsync(owner);
        await AddMemberAsync(host, owner, manager, shop, "Manager");
        await CreateShopAsync(foreign);
        var sale = await SellAsync(owner);
        var unrelated = await SellAsync(owner);
        var external = await SellAsync(foreign);
        var binding = await BindAsync(shop);
        _clock.Now = _clock.Now.AddMinutes(2);
        await ApplyAsync(binding, _clock.Now.AddMinutes(10), restriction: "Trading", takesEffectAt: _clock.Now);
        await EnforceAsync(shop);

        foreach (var missing in new[] { external, sale with { Id = Guid.NewGuid() } })
            await AssertProblemAsync(await ReturnAsync(owner, missing, ReturnInput(missing.Line, Guid.NewGuid().ToString())),
                HttpStatusCode.NotFound, "Sale.NotFound");
        await AssertProblemAsync(await ReturnAsync(owner, sale, ReturnInput(unrelated.Line)), HttpStatusCode.BadRequest, "SaleReturn.SaleItemNotFound");
        await AssertProblemAsync(await ReturnAsync(owner, sale, new
        {
            payoutDestination = (int)ReturnPayoutDestination.Refund,
            items = new[]
            {
                new { saleItemId = sale.Line, quantity = 1m, lineType = "Goods", condition = "Restockable" },
                new { saleItemId = sale.Line, quantity = 1m, lineType = "Goods", condition = "Restockable" },
            },
        }), HttpStatusCode.BadRequest, "SaleReturn.DuplicateSaleItem");

        using var first = await ReturnAsync(owner, sale, ReturnInput(sale.Line));
        await ReadSuccessAsync(first);
        await using var db = Open(Migrator);
        var originalReturn = await db.SaleReturns.Include(x => x.Items).SingleAsync(x => x.SaleId == sale.Id, Ct);
        await AssertProblemAsync(await ReturnAsync(owner, sale with { Id = originalReturn.Id }, ReturnInput(sale.Line)),
            HttpStatusCode.NotFound, "Sale.NotFound");
        await AssertProblemAsync(await ReturnAsync(owner, sale, ReturnInput(Assert.Single(originalReturn.Items).Id)),
            HttpStatusCode.BadRequest, "SaleReturn.SaleItemNotFound");

        var responses = await Task.WhenAll(
            ReturnAsync(owner, sale, ReturnInput(sale.Line, Guid.NewGuid().ToString())),
            ReturnAsync(manager, sale, ReturnInput(sale.Line, Guid.NewGuid().ToString())));
        using var winner = Assert.Single(responses, x => x.IsSuccessStatusCode);
        await AssertProblemAsync(Assert.Single(responses, x => !x.IsSuccessStatusCode), HttpStatusCode.BadRequest, "SaleReturn.QuantityExceedsRemaining");
        Assert.Equal(2, await db.SaleReturns.CountAsync(x => x.ShopId == shop, Ct));
        Assert.Equal(1, await db.OperationReceipts.CountAsync(x => x.ShopId == shop && x.OperationType == FinancialOperationType.SaleReturn, Ct));
        Assert.Equal(10m, await db.InventoryBatches.Where(x => x.Id == sale.Batch).Select(x => x.Quantity).SingleAsync(Ct));
        Assert.Equal(2, await db.StockTransactions.CountAsync(x => x.InventoryBatchId == sale.Batch && x.TransactionType == StockTransactionType.Ret, Ct));
    }

    [Fact]
    public async Task Linked_void_and_keyed_recovery_obey_current_authority()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var owner = CreateClient(host);
        using var staff = CreateClient(host);
        var shop = await CreateShopAsync(owner);
        await AddMemberAsync(host, owner, staff, shop, "Staff");
        var sale = await SellAsync(owner);
        var binding = await BindAsync(shop);
        var end = _clock.Now.AddMinutes(5);
        await ApplyAsync(binding, end);
        await EnforceAsync(shop);
        _clock.Now = end;
        var recordKey = Guid.NewGuid().ToString();
        var record = ReturnInput(sale.Line, recordKey, 2m);
        using var preview = await ReturnAsync(staff, sale, ReturnInput(sale.Line), preview: true);
        var redacted = await ReadSuccessAsync(preview);
        Assert.False(redacted.GetProperty("hasFinancialAccess").GetBoolean());
        Assert.Equal(JsonValueKind.Null, redacted.GetProperty("financial").ValueKind);
        Assert.Equal(JsonValueKind.Null, redacted.GetProperty("lines")[0].GetProperty("financial").ValueKind);

        using var posted = await ReturnAsync(owner, sale, record);
        var postedReturn = (await ReadSuccessAsync(posted)).GetProperty("returns")[0].GetProperty("saleReturnId").GetGuid();
        using var replay = await ReturnAsync(owner, sale, record);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        await AssertProblemAsync(await ReturnAsync(owner, sale, ReturnInput(sale.Line, recordKey)),
            HttpStatusCode.Conflict, "Operation.PayloadMismatch");
        Assert.Equal(HttpStatusCode.Forbidden, (await ReturnAsync(staff, sale, record)).StatusCode);
        await AssertProblemAsync(await owner.GetAsync($"/api/financial-operations/sale-return/{Guid.NewGuid():N}", Ct),
            HttpStatusCode.NotFound, "Operation.NotFound");

        var voidKey = Guid.NewGuid().ToString();
        var voidRoute = $"/api/sales/returns/{postedReturn}/void";
        var reversal = new { operationKey = voidKey, reason = "Recorded twice" };
        using var voided = await owner.PostAsJsonAsync(voidRoute, reversal, Ct);
        Assert.Equal(HttpStatusCode.NoContent, voided.StatusCode);
        using var voidReplay = await owner.PostAsJsonAsync(voidRoute, reversal, Ct);
        Assert.Equal(HttpStatusCode.NoContent, voidReplay.StatusCode);
        await AssertProblemAsync(await owner.PostAsJsonAsync(voidRoute, new { operationKey = voidKey, reason = "Different reason" }, Ct),
            HttpStatusCode.Conflict, "Operation.PayloadMismatch");

        await using var db = Open(Migrator);
        var actor = await db.ShopMemberships.Where(x => x.ShopId == shop && x.Role == ShopRole.Owner).Select(x => x.UserId).SingleAsync(Ct);
        var persisted = await db.SaleReturns.SingleAsync(x => x.SaleId == sale.Id, Ct);
        Assert.True(persisted.IsVoided);
        Assert.Equal(BusinessCalendar.DateOf(end), persisted.VoidedBusinessDate);
        Assert.Equal(8m, await db.InventoryBatches.Where(x => x.Id == sale.Batch).Select(x => x.Quantity).SingleAsync(Ct));
        await AssertOutcomesAsync(owner, recordKey, voidKey, postedReturn);
        await ApplyAsync(binding, end.AddHours(1), revision: 2, restriction: "Security");
        await AssertProblemAsync(await ReturnAsync(owner, sale, record), HttpStatusCode.Forbidden, "Licence.SecuritySuspended");
        await AssertProblemAsync(await owner.PostAsJsonAsync(voidRoute, reversal, Ct), HttpStatusCode.Forbidden, "Licence.SecuritySuspended");
        var protectedLookup = await DispatchAsync<ErrorOr<FinancialOperationDto>>(host, owner,
            new GetFinancialOperationQuery(actor, shop, "sale-return", recordKey));
        Assert.True(protectedLookup.IsError, "A committed return must not be disclosed during security suspension.");
        Assert.Equal("Licence.SecuritySuspended", protectedLookup.FirstError.Code);
    }

    private static async Task AssertOutcomesAsync(HttpClient client, string recordKey, string voidKey, Guid returnId)
    {
        foreach (var (slug, key) in new[] { ("sale-return", recordKey), ("sale-return-void", voidKey) })
        {
            using var lookup = await client.GetAsync($"/api/financial-operations/{slug}/{key}", Ct);
            var outcome = await ReadSuccessAsync(lookup);
            Assert.Equal(returnId, outcome.GetProperty("response").GetProperty("saleReturnId").GetGuid());
        }
    }

    [Fact]
    public async Task Projected_security_takes_effect_while_waiting_on_the_original_sale()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        var sale = await SellAsync(client);
        var binding = await BindAsync(shop);
        var effective = _clock.Now.AddMinutes(5);
        await ApplyAsync(binding, effective.AddHours(1), restriction: "Security", takesEffectAt: effective);
        await EnforceAsync(shop);

        await using var holder = Open(Migrator);
        await using var row = await holder.Database.BeginTransactionAsync(Ct);
        await holder.Database.ExecuteSqlAsync($"SELECT id FROM sales WHERE id = {sale.Id} FOR UPDATE", Ct);
        var blocker = ((NpgsqlConnection)holder.Database.GetDbConnection()).ProcessID;
        var key = Guid.NewGuid().ToString();
        var request = ReturnAsync(client, sale, ReturnInput(sale.Line, key));
        try
        {
            await WaitForLockAsync(request, blocker);
            _clock.Now = effective;
        }
        finally
        {
            await row.RollbackAsync(Ct);
        }

        await AssertProblemAsync(await request, HttpStatusCode.Forbidden, "Licence.SecuritySuspended");
        Assert.False(await holder.SaleReturns.AnyAsync(x => x.SaleId == sale.Id, Ct));
        Assert.False(await holder.OperationReceipts.AnyAsync(x => x.ShopId == shop && x.OperationKey == key, Ct));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Legacy_service_returns_respect_existing_period_and_tender_rules(bool enrolled)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        var source = await SellServiceAsync(client);
        var binding = await BindAsync(shop);
        var end = _clock.Now.AddMinutes(5);
        await ApplyAsync(binding, end);
        if (enrolled) await EnforceAsync(shop);
        _clock.Now = end;

        await using var db = Open(Migrator);
        // Model an issued sale from before invoice snapshots, while keeping its actual service line.
        await db.Database.ExecuteSqlAsync($"UPDATE sales SET invoice_document = NULL WHERE id = {source.Id}", Ct);
        var line = new { saleItemId = source.Line, quantity = 1m, lineType = "Service", approvedRefundAmount = 100m };
        var input = new { payoutDestination = "Refund", items = new[] { line } };
        await AssertProblemAsync(await ReturnAsync(client, source, new
        {
            payoutDestination = "Refund", refundConfirmed = true, items = new[] { line },
        }), HttpStatusCode.Conflict, "Till.Disabled");

        await db.Database.ExecuteSqlAsync($"UPDATE posting_periods SET state = {(int)PostingPeriodState.Closed} WHERE shop_id = {shop}", Ct);
        using var preview = await ReturnAsync(client, source, input, preview: true);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        await AssertProblemAsync(await ReturnAsync(client, source, input), HttpStatusCode.Conflict, "PostingPeriod.Closed");
        Assert.False(await db.SaleReturns.AnyAsync(x => x.SaleId == source.Id, Ct));

        await db.Database.ExecuteSqlAsync($"UPDATE posting_periods SET state = {(int)PostingPeriodState.Open} WHERE shop_id = {shop}", Ct);
        using var posted = await ReturnAsync(client, source, input);
        Assert.True((await ReadSuccessAsync(posted)).GetProperty("isLegacyDocument").GetBoolean());
        Assert.False(await db.StockTransactions.AnyAsync(x => x.ShopId == shop, Ct));
        Assert.Equal(1, await db.SaleReturns.CountAsync(x => x.SaleId == source.Id, Ct));
    }

    private async Task<SoldSale> SellServiceAsync(HttpClient client)
    {
        using var created = await client.PostAsJsonAsync("/api/services", new
            { name = "Repair", price = 100m, taxRatePercent = 0m, taxIncluded = false, isActive = true }, Ct);
        var service = (await ReadSuccessAsync(created)).GetProperty("serviceId").GetGuid();
        using var sold = await client.PostAsJsonAsync("/api/sales", new
        {
            idempotencyKey = $"sale-{Guid.NewGuid():N}", customerName = "Walk in",
            paymentMethod = (int)PaymentMethod.Cash, paidAmount = 100m, dueAmount = 0m,
            items = new[] { new { barcode = "REPAIR", batchNumber = "", itemName = "Repair", quantity = 1m,
                costPrice = 0m, salesPrice = 100m, mrp = 0m, taxRatePercent = 0m, isPriceIncludingTax = false,
                inventoryBatchId = Guid.Empty, lineType = "Service", serviceId = service } },
        }, Ct);
        var id = (await ReadSuccessAsync(sold)).GetProperty("saleId").GetGuid();
        await using var db = Open(Migrator);
        var source = await db.Sales.Include(x => x.Items).SingleAsync(x => x.Id == id, Ct);
        return new(id, Assert.Single(source.Items).Id, Guid.Empty);
    }

    private async Task<int> WaitForLockAsync(Task operation, int blocker)
    {
        await using var observer = Open(Migrator);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (true)
        {
            var waiter = await observer.Database.SqlQuery<int>($"""
                SELECT pid AS "Value" FROM pg_stat_activity
                WHERE wait_event_type = 'Lock' AND {blocker} = ANY(pg_blocking_pids(pid)) LIMIT 1
                """).SingleOrDefaultAsync(deadline.Token);
            if (waiter != 0)
            {
                _output.WriteLine($"PostgreSQL backend {waiter} waits for {blocker}");
                return waiter;
            }

            Assert.False(operation.IsCompleted, $"Operation completed before waiting for PostgreSQL backend {blocker}.");
            await Task.Delay(10, deadline.Token);
        }
    }

    private async Task<SoldSale> SellAsync(HttpClient client)
    {
        var barcode = $"RETURN-{Guid.NewGuid():N}";
        var itemName = $"Item {barcode}";
        using var inbound = await client.PostAsJsonAsync("/api/inventory/inbound", new
        {
            itemName, barcode, uom = "pcs", batchNumber = "P0413A", quantity = 10m,
            totalPurchaseCost = 400m, mrp = 120m, salesPrice = 100m, taxRatePercent = 0m,
        }, Ct);
        var batch = (await ReadSuccessAsync(inbound)).GetProperty("inventoryBatchId").GetGuid();
        using var response = await client.PostAsJsonAsync("/api/sales", new
        {
            idempotencyKey = $"sale-{Guid.NewGuid():N}", customerName = "Walk in",
            paymentMethod = (int)PaymentMethod.Cash, paidAmount = 200m, dueAmount = 0m,
            items = new[] { new { barcode, batchNumber = "P0413A", itemName, quantity = 2m,
                costPrice = 40m, salesPrice = 100m, mrp = 120m, taxRatePercent = 0m, isPriceIncludingTax = false,
                inventoryBatchId = batch, lineType = "Goods" } },
        }, Ct);
        var sale = (await ReadSuccessAsync(response)).GetProperty("saleId").GetGuid();
        await using var db = Open(Migrator);
        var source = await db.Sales.Include(x => x.Items).SingleAsync(x => x.Id == sale, Ct);
        return new(sale, Assert.Single(source.Items).Id, batch);
    }

    private static object ReturnInput(Guid line, string? operationKey = null, decimal quantity = 1m) => new
    {
        operationKey,
        payoutDestination = (int)ReturnPayoutDestination.Refund,
        items = new[] { new { saleItemId = line, quantity, lineType = (int)SaleLineType.Goods,
            condition = (int)SaleReturnCondition.Restockable, approvedRefundAmount = quantity * 100m } },
    };

    private static Task<HttpResponseMessage> ReturnAsync(HttpClient client, SoldSale sale, object input, bool preview = false) =>
        client.PostAsJsonAsync($"/api/sales/{sale.Id}/returns{(preview ? "/preview" : "")}", input, Ct);

    private sealed record SoldSale(Guid Id, Guid Line, Guid Batch);
}
