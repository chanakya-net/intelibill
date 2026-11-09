using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Intelibill.Application.Features.Tills;
using static Intelibill.Integration.Tests.TillTestContext;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Options;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class TillPaymentExpenseTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public async Task RealSourceWins_CloseWaitsAndRejectsStaleBoundary()
    {
        await using var setup = await ArrangeAsync();
        await using var barrier = await TillPostgresBarrier.CreateAsync(setup.Factory.MigratorConnectionString, "till_movements", setup.Shop);
        var posting = setup.Client.PostAsJsonAsync($"/api/customers/{setup.Customer}/payments", setup.Payment());
        var sourcePid = await barrier.WaitForArrivalAsync();
        var closing = setup.Client.PostAsJsonAsync($"/api/tills/sessions/{setup.Session.Id}/close", new { countedCash = 1000m, expectedVersion = setup.Session.Version, operationKey = "source-before-close" });
        await barrier.WaitForBlockedByAsync(sourcePid);
        Assert.False(posting.IsCompleted); Assert.False(closing.IsCompleted);
        await barrier.ReleaseAsync();
        using var source = await posting.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.True(source.IsSuccessStatusCode, await source.Content.ReadAsStringAsync());
        using var closed = await closing.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(HttpStatusCode.Conflict, closed.StatusCode);
        Assert.Contains("Till.VersionConflict", await closed.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Single(await setup.App.Db.TillMovements.Where(x => x.SessionId == setup.Session.Id).ToArrayAsync());
        Assert.False(await setup.App.Db.TillCloses.AnyAsync(x => x.SessionId == setup.Session.Id));
    }

    [Fact]
    public async Task UnknownPaymentTender_EnabledRejected()
    {
        await using var setup = await ArrangeAsync();
        var request = setup.Payment();
        request.Remove("method");
        using var result = await setup.Client.PostAsJsonAsync($"/api/customers/{setup.Customer}/payments", request);
        Assert.Contains("Till.TenderRequired", await result.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.False(await setup.App.Db.PaymentReceipts.AnyAsync(x => x.ShopId == setup.Shop));
    }

    [Fact]
    public async Task CustomerCollection_AddsCashOnce()
    {
        await using var setup = await ArrangeAsync();
        var request = setup.Payment();
        using var posted = await setup.Client.PostAsJsonAsync($"/api/customers/{setup.Customer}/payments", request);
        posted.EnsureSuccessStatusCode();
        using var replay = await setup.Client.PostAsJsonAsync($"/api/customers/{setup.Customer}/payments", request);
        replay.EnsureSuccessStatusCode();
        var movement = Assert.Single(await setup.App.Db.TillMovements.Where(x => x.ShopId == setup.Shop).ToArrayAsync());
        Assert.Equal(200m, movement.SignedAmount);
        Assert.Equal(TillMovementSource.CustomerPayment, movement.SourceType);
    }

    [Fact]
    public async Task PaymentReversal_NoPhysicalMovementDoesNotTouchCash()
    {
        await using var setup = await ArrangeAsync();
        using var posted = await setup.Client.PostAsJsonAsync($"/api/customers/{setup.Customer}/payments", setup.Payment());
        posted.EnsureSuccessStatusCode();
        var id = (await posted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("receiptId").GetGuid();
        using var reversed = await setup.Client.PostAsJsonAsync($"/api/payments/{id}/reversal", new { reason = "Bookkeeping duplicate", operationKey = "reverse", settlementEvidence = new { disposition = "NoPhysicalMovement", reason = "Money retained as customer credit" } });
        reversed.EnsureSuccessStatusCode();
        var stored = await setup.App.Db.PaymentReversals.SingleAsync(x => x.ShopId == setup.Shop);
        Assert.Equal(TillSettlementDisposition.NoPhysicalMovement, stored.SettlementEvidence!.Disposition);
        Assert.Equal("Money retained as customer credit", stored.SettlementEvidence.Reason);
        Assert.Single(await setup.App.Db.TillMovements.Where(x => x.ShopId == setup.Shop).ToArrayAsync());
    }

    [Fact]
    public async Task CustomerRefund_UsesActualTender()
    {
        await using var setup = await ArrangeAsync();
        using var posted = await setup.Client.PostAsJsonAsync($"/api/customers/{setup.Customer}/payments", setup.Payment());
        var id = (await posted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("receiptId").GetGuid();
        using var reversed = await setup.Client.PostAsJsonAsync($"/api/payments/{id}/reversal", new { reason = "Refund received", operationKey = "reverse", till = new { sessionId = setup.Session.Id }, settlementEvidence = new { disposition = "MoneyPaid", reason = "Refund via UPI", tender = new { method = "Upi", appliedAmount = 200m } } });
        reversed.EnsureSuccessStatusCode();
        var movement = await setup.App.Db.TillMovements.SingleAsync(x => x.ShopId == setup.Shop && x.SourceType == TillMovementSource.CustomerPaymentReversal);
        Assert.Equal(-200m, movement.SignedAmount); Assert.Equal(ReceiptTenderMethod.Upi, movement.Method);
    }

    [Fact]
    public async Task ExpenseCorrection_DoesNotAssumeRecovery()
    {
        await using var setup = await ArrangeAsync();
        var request = setup.Expense();
        using var posted = await setup.Client.PostAsJsonAsync("/api/expenses", request);
        posted.EnsureSuccessStatusCode();
        var id = (await posted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        request.Remove("tenderLine"); request.Remove("till"); request["amount"] = 50m; request["operationKey"] = "correct";
        using var corrected = await setup.Client.PostAsJsonAsync($"/api/expenses/{id}/correct", request);
        Assert.Contains("Till.SettlementEvidenceRequired", await corrected.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.False((await setup.App.Db.Expenses.SingleAsync(x => x.Id == id)).IsVoided);
    }

    [Theory]
    [InlineData("MoneyPaid", 150, -50)]
    [InlineData("MoneyReceived", 50, 50)]
    public async Task ExpenseCorrectionExplicitDelta_DirectionAndAmountValidated(string disposition, decimal amount, decimal expectedDelta)
    {
        await using var setup = await ArrangeAsync();
        using var posted = await setup.Client.PostAsJsonAsync("/api/expenses", setup.Expense());
        posted.EnsureSuccessStatusCode();
        var id = (await posted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var request = setup.Expense(); request.Remove("tenderLine"); request["amount"] = amount;
        request["settlementEvidence"] = new JsonObject { ["disposition"] = disposition, ["reason"] = "Actual delta settlement", ["tender"] = new JsonObject { ["method"] = "Cash", ["appliedAmount"] = 49m } };
        using var invalid = await setup.Client.PostAsJsonAsync($"/api/expenses/{id}/correct", request);
        Assert.Contains("Till.TenderAmountMismatch", await invalid.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        request["settlementEvidence"]!["tender"]!["appliedAmount"] = 50m;
        using var corrected = await setup.Client.PostAsJsonAsync($"/api/expenses/{id}/correct", request);
        corrected.EnsureSuccessStatusCode();
        var movement = await setup.App.Db.TillMovements.SingleAsync(x => x.ShopId == setup.Shop && x.SourceType == TillMovementSource.ExpenseCorrection);
        Assert.Equal(expectedDelta, movement.SignedAmount);
    }

    [Fact]
    public async Task SupplierPaymentAndExpense_OnlyOneOutflow_AndActualRecoveryUsesNewTender()
    {
        await using var setup = await ArrangeAsync();
        var supplier = Supplier.Create(setup.Shop, "Supplier", null, null, null, null, null, null, true, false);
        setup.App.Db.Suppliers.Add(supplier); await setup.App.Db.SaveChangesAsync();
        using var posted = await setup.Client.PostAsJsonAsync($"/api/suppliers/{supplier.Id}/payments", setup.Payment());
        posted.EnsureSuccessStatusCode();
        var id = (await posted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("receiptId").GetGuid();
        var receipt = await setup.App.Db.PaymentReceipts.SingleAsync(x => x.Id == id);
        var expense = await setup.App.Db.Expenses.SingleAsync(x => x.Id == receipt.ExpenseId);
        Assert.Equal(ExpenseTreatment.SupplierSettlement, expense.Treatment);
        Assert.Null(expense.SettlementEvidence);
        var movement = Assert.Single(await setup.App.Db.TillMovements.Where(x => x.ShopId == setup.Shop).ToArrayAsync());
        Assert.Equal(-200m, movement.SignedAmount); Assert.Equal(id, movement.SourceId);
        using var correction = await setup.Client.PostAsJsonAsync($"/api/expenses/{expense.Id}/correct", setup.Expense());
        Assert.Contains("Expense.SupplierSettlementCorrectionRefused", await correction.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        using var reversed = await setup.Client.PostAsJsonAsync($"/api/payments/{id}/reversal", new { reason = "Supplier returned funds", operationKey = "recover", till = new { sessionId = setup.Session.Id }, settlementEvidence = new { disposition = "MoneyReceived", reason = "Recovered via UPI", tender = new { method = "Upi", appliedAmount = 200m } } });
        reversed.EnsureSuccessStatusCode();
        var recovery = await setup.App.Db.TillMovements.SingleAsync(x => x.ShopId == setup.Shop && x.SourceType == TillMovementSource.SupplierPaymentReversal);
        Assert.Equal(200m, recovery.SignedAmount); Assert.Equal(ReceiptTenderMethod.Upi, recovery.Method);
        Assert.Equal(2, await setup.App.Db.TillMovements.CountAsync(x => x.ShopId == setup.Shop));
        setup.App.Db.ChangeTracker.Clear();
        Assert.True((await setup.App.Db.Expenses.SingleAsync(x => x.Id == expense.Id)).IsVoided);
    }

    [Theory]
    [InlineData("MoneyReceived", 200, "Till.SettlementDirectionInvalid")]
    [InlineData("MoneyPaid", 199, "Till.TenderAmountMismatch")]
    [InlineData(null, 200, "Till.SettlementEvidenceRequired")]
    public async Task Reversal_InvalidOrAbsentEvidence_LeavesSourceUnchanged(string? disposition, decimal amount, string code)
    {
        await using var setup = await ArrangeAsync();
        using var posted = await setup.Client.PostAsJsonAsync($"/api/customers/{setup.Customer}/payments", setup.Payment());
        var id = (await posted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("receiptId").GetGuid();
        var request = new JsonObject { ["reason"] = "Correction", ["operationKey"] = "reverse", ["till"] = new JsonObject { ["sessionId"] = setup.Session.Id } };
        if (disposition is not null) request["settlementEvidence"] = new JsonObject { ["disposition"] = disposition, ["reason"] = "Physical refund", ["tender"] = new JsonObject { ["method"] = "Cash", ["appliedAmount"] = amount } };
        using var reversed = await setup.Client.PostAsJsonAsync($"/api/payments/{id}/reversal", request);
        Assert.Contains(code, await reversed.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.False(await setup.App.Db.PaymentReversals.AnyAsync(x => x.ShopId == setup.Shop));
        Assert.Single(await setup.App.Db.CustomerLedgerEntries.Where(x => x.ShopId == setup.Shop).ToArrayAsync());
        Assert.Single(await setup.App.Db.TillMovements.Where(x => x.ShopId == setup.Shop).ToArrayAsync());
    }

    [Fact]
    public async Task Expense_NoMovementPersistsHistoricalEvidence_AndMetadataCorrectionAddsNoCash()
    {
        await using var setup = await ArrangeAsync();
        var request = setup.Expense(); request.Remove("tenderLine"); request.Remove("till");
        request["expenseDate"] = "2026-10-15";
        request["settlementEvidence"] = new JsonObject { ["disposition"] = "NoPhysicalMovement", ["reason"] = "Historical bookkeeping only" };
        using var posted = await setup.Client.PostAsJsonAsync("/api/expenses", request);
        posted.EnsureSuccessStatusCode();
        var id = (await posted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var expense = await setup.App.Db.Expenses.SingleAsync(x => x.Id == id);
        Assert.Equal("Historical bookkeeping only", expense.SettlementEvidence!.Reason);
        request["operationKey"] = "metadata"; request["paidTo"] = "Corrected landlord name";
        using var corrected = await setup.Client.PostAsJsonAsync($"/api/expenses/{id}/correct", request);
        corrected.EnsureSuccessStatusCode();
        using var replay = await setup.Client.PostAsJsonAsync($"/api/expenses/{id}/correct", request);
        replay.EnsureSuccessStatusCode();
        Assert.Equal(await corrected.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        Assert.False(await setup.App.Db.TillMovements.AnyAsync(x => x.ShopId == setup.Shop));
        Assert.Equal(2, await setup.App.Db.Expenses.CountAsync(x => x.ShopId == setup.Shop));
        var immutable = await Assert.ThrowsAsync<PostgresException>(() => setup.App.Db.Database.ExecuteSqlAsync($"UPDATE expenses SET settlement_evidence=NULL WHERE id={id}"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, immutable.SqlState);
    }

    [Fact]
    public async Task PaymentAndExpenseReplay_AfterCloseAndDisable_ReturnsSavedOutcome_AndChangedPayloadConflicts()
    {
        await using var setup = await ArrangeAsync();
        var payment = setup.Payment(); var expense = setup.Expense();
        using var postedPayment = await setup.Client.PostAsJsonAsync($"/api/customers/{setup.Customer}/payments", payment);
        postedPayment.EnsureSuccessStatusCode();
        using var postedExpense = await setup.Client.PostAsJsonAsync("/api/expenses", expense);
        postedExpense.EnsureSuccessStatusCode();
        setup.App.Time.Now = DateTimeOffset.UtcNow;
        setup.App.Db.ChangeTracker.Clear();
        var current = Success(await setup.App.Reads.SessionAsync(setup.Shop, setup.Actor, setup.Session.Id, 1, 20, default)).Session;
        Success(await setup.App.Lifecycle.CloseAsync(new(setup.Shop, setup.Actor, current.Id, 1100m, null, current.Version, "close"), default));
        setup.Factory.Services.GetRequiredService<IOptions<TillSessionOptions>>().Value.Enabled = false;
        using var replayPayment = await setup.Client.PostAsJsonAsync($"/api/customers/{setup.Customer}/payments", payment);
        using var replayExpense = await setup.Client.PostAsJsonAsync("/api/expenses", expense);
        Assert.Equal(await postedPayment.Content.ReadAsStringAsync(), await replayPayment.Content.ReadAsStringAsync());
        Assert.Equal(await postedExpense.Content.ReadAsStringAsync(), await replayExpense.Content.ReadAsStringAsync());
        using var recovery = await setup.Client.GetAsync($"/api/financial-operations/expense-record/{expense["operationKey"]!.GetValue<string>()}");
        recovery.EnsureSuccessStatusCode();
        payment["till"]!["overrideReason"] = "changed"; expense["tenderLine"]!["method"] = "Upi";
        using var conflictPayment = await setup.Client.PostAsJsonAsync($"/api/customers/{setup.Customer}/payments", payment);
        using var conflictExpense = await setup.Client.PostAsJsonAsync("/api/expenses", expense);
        Assert.Contains("Operation.PayloadMismatch", await conflictPayment.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains("Operation.PayloadMismatch", await conflictExpense.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        expense["operationKey"] = "disabled-new";
        using var disabled = await setup.Client.PostAsJsonAsync("/api/expenses", expense);
        Assert.Contains("Till.Disabled", await disabled.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PaymentRollback_RollsBackReceiptLedgerTillAndOperation_ThenSameKeyRetries()
    {
        await using var setup = await ArrangeAsync();
        var request = setup.Payment(); request["operationKey"] = "till-payment-rollback-test";
        await setup.App.Db.Database.ExecuteSqlRawAsync("CREATE FUNCTION fail_payment_till_test() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.operation_key = 'till-payment-rollback-test' THEN RAISE EXCEPTION 'test rollback'; END IF; RETURN NEW; END $$; CREATE TRIGGER fail_payment_till_test AFTER INSERT ON till_movements FOR EACH ROW EXECUTE FUNCTION fail_payment_till_test();");
        try
        {
            using var failed = await setup.Client.PostAsJsonAsync($"/api/customers/{setup.Customer}/payments", request);
            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
            Assert.False(await setup.App.Db.PaymentReceipts.AnyAsync(x => x.ShopId == setup.Shop));
            Assert.False(await setup.App.Db.CustomerLedgerEntries.AnyAsync(x => x.ShopId == setup.Shop));
            Assert.False(await setup.App.Db.TillMovements.AnyAsync(x => x.ShopId == setup.Shop));
            Assert.False(await setup.App.Db.OperationReceipts.AnyAsync(x => x.ShopId == setup.Shop && x.OperationKey == "till-payment-rollback-test"));
        }
        finally { await setup.App.Db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_payment_till_test ON till_movements; DROP FUNCTION fail_payment_till_test();"); }
        using var retried = await setup.Client.PostAsJsonAsync($"/api/customers/{setup.Customer}/payments", request);
        retried.EnsureSuccessStatusCode();
        Assert.Single(await setup.App.Db.PaymentReceipts.Where(x => x.ShopId == setup.Shop).ToArrayAsync());
        Assert.Single(await setup.App.Db.TillMovements.Where(x => x.ShopId == setup.Shop).ToArrayAsync());
    }

    [Fact]
    public async Task CloseWins_PaymentWaitsThenRefusesWithoutLateMovement()
    {
        await using var setup = await ArrangeAsync();
        await using var blocker = new TillTestContext(setup.Factory.MigratorConnectionString, setup.Shop, new TillTestClock { Now = DateTimeOffset.UtcNow });
        await using var tx = await blocker.Db.Database.BeginTransactionAsync();
        Success(await blocker.Periods.AuthorizeForWriteAsync(setup.Shop, setup.Session.BusinessDate));
        var session = Success(await blocker.Posting.AcquireAsync(setup.Shop, setup.Actor, new(setup.Session.Id), setup.Session.BusinessDate))!;
        var close = Success(session.Close(Intelibill.Domain.ValueObjects.TillTotals.From([], []), 1000m, setup.Actor, DateTimeOffset.UtcNow, setup.Session.BusinessDate, null, false, false));
        blocker.Db.TillCloses.Add(close); await blocker.Db.SaveChangesAsync();
        var pending = setup.Client.PostAsJsonAsync($"/api/customers/{setup.Customer}/payments", setup.Payment());
        await WaitForLockAsync(setup, "%FROM till_sessions%");
        Assert.False(pending.IsCompleted); await tx.CommitAsync();
        using var response = await pending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains("Till.SessionClosed", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.False(await setup.App.Db.PaymentReceipts.AnyAsync(x => x.ShopId == setup.Shop));
        Assert.False(await setup.App.Db.TillMovements.AnyAsync(x => x.ShopId == setup.Shop));
    }

    [Fact]
    public async Task PeriodCloseWins_PaymentWaitsThenRefusesBeforeTill()
    {
        await using var setup = await ArrangeAsync();
        await using var blocker = new TillTestContext(setup.Factory.MigratorConnectionString, setup.Shop);
        await using var tx = await blocker.Db.Database.BeginTransactionAsync();
        var period = await blocker.Db.PostingPeriods.SingleAsync(x => x.ShopId == setup.Shop);
        await blocker.Db.Database.ExecuteSqlAsync($"UPDATE posting_periods SET state={(int)PostingPeriodState.Closed} WHERE id={period.Id}");
        var pending = setup.Client.PostAsJsonAsync($"/api/customers/{setup.Customer}/payments", setup.Payment());
        await WaitForLockAsync(setup, "%posting_periods%");
        Assert.False(pending.IsCompleted); await tx.CommitAsync();
        using var response = await pending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains("PostingPeriod.Closed", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.False(await setup.App.Db.PaymentReceipts.AnyAsync(x => x.ShopId == setup.Shop));
        Assert.False(await setup.App.Db.TillSourceRecords.AnyAsync(x => x.ShopId == setup.Shop));
    }

    [Theory]
    [InlineData("MoneyReceived", 150, 50)]
    [InlineData("MoneyPaid", 50, 50)]
    [InlineData("MoneyPaid", 100, 1)]
    public async Task ExpenseCorrection_WrongDirectionOrZeroDelta_RefusesPhysicalClaim(string disposition, decimal correctedAmount, decimal tenderAmount)
    {
        await using var setup = await ArrangeAsync();
        using var posted = await setup.Client.PostAsJsonAsync("/api/expenses", setup.Expense());
        posted.EnsureSuccessStatusCode();
        var id = (await posted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var request = setup.Expense(); request.Remove("tenderLine"); request["amount"] = correctedAmount;
        request["settlementEvidence"] = new JsonObject { ["disposition"] = disposition, ["reason"] = "Claimed settlement", ["tender"] = new JsonObject { ["method"] = "Cash", ["appliedAmount"] = tenderAmount } };
        using var corrected = await setup.Client.PostAsJsonAsync($"/api/expenses/{id}/correct", request);
        Assert.Contains("Till.SettlementDirectionInvalid", await corrected.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.False((await setup.App.Db.Expenses.SingleAsync(x => x.Id == id)).IsVoided);
        Assert.Single(await setup.App.Db.TillMovements.Where(x => x.ShopId == setup.Shop).ToArrayAsync());
    }

    [Fact]
    public async Task HistoricalCorrection_UsesTodaysDrawer_AndFencesAllThreeDates()
    {
        await using var setup = await ArrangeAsync();
        var request = setup.Expense(); request.Remove("tenderLine"); request.Remove("till");
        request["expenseDate"] = "2026-10-15";
        request["settlementEvidence"] = new JsonObject { ["disposition"] = "NoPhysicalMovement", ["reason"] = "Historical original" };
        using var posted = await setup.Client.PostAsJsonAsync("/api/expenses", request);
        posted.EnsureSuccessStatusCode();
        var id = (await posted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        request["operationKey"] = "historical-correction"; request["expenseDate"] = "2026-09-15"; request["amount"] = 150m;
        request["till"] = new JsonObject { ["sessionId"] = setup.Session.Id };
        request["settlementEvidence"] = new JsonObject { ["disposition"] = "MoneyPaid", ["reason"] = "Actual additional payment today", ["tender"] = new JsonObject { ["method"] = "Cash", ["appliedAmount"] = 50m } };
        using var corrected = await setup.Client.PostAsJsonAsync($"/api/expenses/{id}/correct", request);
        corrected.EnsureSuccessStatusCode();
        var correctedId = (await corrected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var saved = await setup.App.Db.Expenses.SingleAsync(x => x.Id == correctedId);
        Assert.Equal(new DateOnly(2026, 9, 15), saved.ExpenseDate);
        var movement = Assert.Single(await setup.App.Db.TillMovements.Where(x => x.ShopId == setup.Shop).ToArrayAsync());
        Assert.Equal(setup.Session.Id, movement.SessionId); Assert.Equal(-50m, movement.SignedAmount);
        Assert.Equal(3, await setup.App.Db.PostingPeriods.CountAsync(x => x.ShopId == setup.Shop));
        setup.App.Db.ChangeTracker.Clear(); setup.App.Time.Now = DateTimeOffset.UtcNow;
        var current = Success(await setup.App.Reads.SessionAsync(setup.Shop, setup.Actor, setup.Session.Id, 1, 20, default)).Session;
        Success(await setup.App.Lifecycle.CloseAsync(new(setup.Shop, setup.Actor, current.Id, 950m, null, current.Version, "close-historical"), default));
        setup.Factory.Services.GetRequiredService<IOptions<TillSessionOptions>>().Value.Enabled = false;
        using var replay = await setup.Client.PostAsJsonAsync($"/api/expenses/{id}/correct", request);
        Assert.Equal(await corrected.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
    }

    private static async Task WaitForLockAsync(Setup setup, string queryPattern)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!await setup.App.Db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE wait_event_type='Lock' AND query LIKE {queryPattern} AND pid <> pg_backend_pid()) AS \"Value\"").SingleAsync(timeout.Token))
            await Task.Delay(10, timeout.Token);
    }

    private async Task<Setup> ArrangeAsync()
    {
        var factory = new ApiWebApplicationFactory(fixture);
        await factory.InitializeAsync();
        var client = await OpenShopAsync(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var shop = await db.Shops.SingleAsync(x => x.Name == "Till Payments");
        var actor = (await db.ShopMemberships.SingleAsync(x => x.ShopId == shop.Id)).UserId;
        var customer = Customer.Create(shop.Id, "Customer", "+919222222222", null);
        db.Customers.Add(customer); await db.SaveChangesAsync();
        var options = factory.Services.GetRequiredService<IOptions<TillSessionOptions>>().Value;
        options.Enabled = true; options.AllowedShopIds = [shop.Id];
        await OptInAsync(factory.MigratorConnectionString, shop.Id);
        var app = new TillTestContext(factory.MigratorConnectionString, shop.Id, new TillTestClock { Now = DateTimeOffset.UtcNow });
        var session = await app.OpenAsync(shop.Id, actor, (await app.CreateAsync(shop.Id, actor)).Id);
        return new(factory, client, app, shop.Id, actor, customer.Id, session);
    }

    private sealed record Setup(ApiWebApplicationFactory Factory, HttpClient Client, TillTestContext App, Guid Shop, Guid Actor, Guid Customer, TillSessionDto Session) : IAsyncDisposable
    {
        public JsonObject Expense() => new() { ["categoryName"] = "Rent", ["paidTo"] = "Landlord", ["amount"] = 100m, ["expenseDate"] = Session.BusinessDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), ["operationKey"] = Guid.NewGuid().ToString(), ["till"] = new JsonObject { ["sessionId"] = Session.Id }, ["tenderLine"] = new JsonObject { ["method"] = "Cash", ["appliedAmount"] = 100m } };
        public JsonObject Payment() => new() { ["amount"] = 200m, ["paymentDate"] = Session.BusinessDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), ["operationKey"] = Guid.NewGuid().ToString(), ["method"] = "Cash", ["till"] = new JsonObject { ["sessionId"] = Session.Id } };
        public async ValueTask DisposeAsync() { await App.DisposeAsync(); Client.Dispose(); await Factory.DisposeAsync(); }
    }
    private static async Task<HttpClient> OpenShopAsync(ApiWebApplicationFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var registered = await client.EnrollAsync(new { email = $"till-payment-{Guid.NewGuid():N}@example.test", password = "Pass123!Aa", firstName = "Till", lastName = "Owner" });
        registered.EnsureSuccessStatusCode();
        var auth = await registered.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());
        using var created = await client.PostAsJsonAsync("/api/shops", new { name = "Till Payments", address = "Address", city = "City", state = "State", pincode = "560001" });
        created.EnsureSuccessStatusCode();
        var scope = await created.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", scope.GetProperty("accessToken").GetString());
        return client;
    }

}
