using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Intelibill.Integration.Tests;

public sealed partial class P0413cLicensingTests
{
    [Fact]
    public async Task Expired_paid_receipt_supports_only_genuine_shipment_and_credit()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var owner = CreateClient(host);
        using var manager = CreateClient(host);
        using var staff = CreateClient(host);
        var source = await SeedAsync(owner);
        await AddMemberAsync(host, owner, manager, source.Shop, "Manager");
        await AddMemberAsync(host, owner, staff, source.Shop, "Staff");
        await PostAsync(owner, $"/api/suppliers/{source.Supplier}/payments", new
        {
            amount = 1000, paymentDate = BusinessCalendar.DateOf(_clock.Now), operationKey = NewKey(), method = "Cash",
            allocations = new[] { new { targetId = source.Receipt, amount = 1000 } },
        });
        var end = _clock.Now.AddMinutes(1);
        await ApplyAsync(source.Binding, end);
        await EnforceAsync(source.Shop);
        _clock.Now = end;
        await AssertExpiredAsync(owner, source.Shop);

        var detail = await DetailAsync(owner, source.Receipt);
        var before = await FactsAsync(source.Shop);
        await AssertProblemAsync(await owner.PostAsJsonAsync("/api/supplier-returns",
            Shipment(source, detail, NewKey(), quantity: 11), Ct),
            HttpStatusCode.BadRequest, "SupplierReturn.QuantityInvalid");
        var pending = await ShipAsManagerAsync(manager, staff, source, detail);
        Assert.Equal(before with { Operations = before.Operations + 1, Shipments = 1, Movements = before.Movements + 1, Stock = 7 },
            await FactsAsync(source.Shop));
        await AssertPaidCreditAsync(owner, manager, source, pending);
    }

    private static async Task<JsonElement> ShipAsManagerAsync(HttpClient manager, HttpClient staff, Source source, JsonElement detail)
    {
        const string route = "/api/supplier-returns";
        var request = Shipment(source, detail, NewKey());
        await AssertProblemAsync(await staff.PostAsJsonAsync(route, request, Ct),
            HttpStatusCode.Forbidden, "SupplierReturn.Forbidden");
        await AssertProblemAsync(await manager.PostAsJsonAsync(route, Shipment(source, detail, NewKey(), accepted: true), Ct),
            HttpStatusCode.Forbidden, "SupplierReturn.Forbidden");
        var pending = await PostAsync(manager, route, request);
        Assert.Equal("PendingSupplierCredit", pending.GetProperty("status").GetString());
        return pending;
    }

    private async Task AssertPaidCreditAsync(HttpClient owner, HttpClient manager, Source source, JsonElement pending)
    {
        var before = await FactsAsync(source.Shop);
        var request = Credit(pending, NewKey());
        var route = $"/api/supplier-returns/{pending.GetProperty("returnId").GetGuid()}/credit";
        await AssertProblemAsync(await manager.PostAsJsonAsync(route, request, Ct),
            HttpStatusCode.Forbidden, "SupplierReturn.Forbidden");
        Assert.Equal(before, await FactsAsync(source.Shop));
        var accepted = await PostAsync(owner, route, request);
        Assert.Equal("CreditAccepted", accepted.GetProperty("status").GetString());
        var after = await DetailAsync(owner, source.Receipt);
        Assert.Equal(7, after.GetProperty("availableQuantity").GetDecimal());
        Assert.Equal(700, after.GetProperty("netPayableAmount").GetDecimal());
        Assert.Equal(1000, after.GetProperty("allocatedPaymentAmount").GetDecimal());
        Assert.Equal(0, after.GetProperty("outstandingAmount").GetDecimal());
        Assert.Equal(300, after.GetProperty("supplierCreditAmount").GetDecimal());
        Assert.Equal(before with { Operations = before.Operations + 1, Credits = 1, Ledger = before.Ledger + 1 },
            await FactsAsync(source.Shop));
        await using var db = Open(Migrator);
        Assert.Single(await db.PaymentReceipts.Where(x => x.ShopId == source.Shop).ToListAsync(Ct));
        Assert.Equal(-300, await db.SupplierLedgerEntries.Where(x => x.ShopId == source.Shop &&
            x.EntryType == SupplierLedgerEntryType.SupplierReturnCredit).Select(x => x.Amount).SingleAsync(Ct));
        _output.WriteLine("Expired paid receipt: 7 units, payable 700, allocated 1000, outstanding 0, supplier credit 300, one credit ledger entry.");
    }

    [Theory]
    [InlineData("shipment")]
    [InlineData("receipt")]
    public async Task Expiry_rejects_a_future_dated_source(string operation)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var source = await SeedAsync(client);
        var end = _clock.Now.AddMinutes(1);
        await ApplyAsync(source.Binding, end);
        await EnforceAsync(source.Shop);
        await using var db = Open(Migrator);
        Assert.Equal(1, await db.Database.ExecuteSqlAsync(
            $"UPDATE purchase_receipt_values SET received_at = {end.AddMinutes(1)} WHERE id = {source.Receipt}", Ct));
        var (route, body) = await PrepareAsync(client, source, operation);
        _clock.Now = end;
        var before = await FactsAsync(source.Shop);
        await AssertProblemAsync(await client.PostAsJsonAsync(route, body, Ct),
            HttpStatusCode.Conflict, "SupplierReturn.SourceIneligible");
        Assert.Equal(before, await FactsAsync(source.Shop));
    }

    [Fact]
    public async Task Trading_suspension_preserves_completion_and_refuses_new_receiving()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var source = await SeedAsync(client);
        var group = await ReceiveGroupAsync(client, source);
        var end = _clock.Now.AddMinutes(1);
        await ApplyAsync(source.Binding, end, restriction: "Trading");
        await EnforceAsync(source.Shop);

        var before = await FactsAsync(source.Shop);
        var order = await GetAsync(client, $"/api/purchase-orders/{group.Order}");
        await AssertProblemAsync(await client.PostAsJsonAsync("/api/inventory/inbound",
            Inbound(source.Supplier, $"NEW-{Guid.NewGuid():N}"), Ct), HttpStatusCode.Forbidden, "Licence.TradingRestricted");
        await AssertProblemAsync(await client.PostAsJsonAsync($"/api/purchase-orders/{group.Order}/receipts",
            Receiving(source, group.Line, 1), Ct), HttpStatusCode.Forbidden, "Licence.TradingRestricted");
        Assert.Equal(before, await FactsAsync(source.Shop));
        Assert.True(JsonElement.DeepEquals(order, await GetAsync(client, $"/api/purchase-orders/{group.Order}")));
        _output.WriteLine("Intake and receiving returned 403 Licence.TradingRestricted. Stock, receipts, ledger and PO were unchanged.");

        var (route, body) = await PrepareAsync(client, source, "shipment");
        var pending = await PostAsync(client, route, body);
        _clock.Now = end;
        await ApplyAsync(source.Binding, end, revision: 2, state: "Expired",
            verified: end.AddSeconds(-1), releasedRevision: 1);
        await AssertExpiredAsync(client, source.Shop);
        var accepted = await PostAsync(client, $"/api/supplier-returns/{pending.GetProperty("returnId").GetGuid()}/credit",
            Credit(pending, NewKey()));
        Assert.Equal("CreditAccepted", accepted.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Scheduled_security_is_rechecked_after_waiting_for_the_supplier()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var source = await SeedAsync(client);
        var start = _clock.Now.AddMinutes(1);
        await ApplyAsync(source.Binding, start.AddDays(1), securityEffectiveAt: start);
        await EnforceAsync(source.Shop);
        var (route, body) = await PrepareAsync(client, source, "receipt");
        await AssertBusinessWaitAsync(client, source, start, route, body);
    }

    private async Task AssertBusinessWaitAsync(HttpClient client, Source source, DateTimeOffset start, string route, object body)
    {
        var before = await FactsAsync(source.Shop);
        await using var held = Open(Migrator);
        await using var transaction = await held.Database.BeginTransactionAsync(Ct);
        await held.Database.ExecuteSqlAsync($"SELECT 1 FROM suppliers WHERE id = {source.Supplier} FOR UPDATE", Ct);
        var blocker = ((NpgsqlConnection)held.Database.GetDbConnection()).ProcessID;
        await using var observer = await TillPostgresBarrier.CreateAsync(Migrator, "entitlement_snapshot_inbox", source.Shop);
        var request = client.PostAsJsonAsync(route, body, Ct);
        try
        {
            var waiter = await observer.WaitForBlockedByAsync(blocker);
            _output.WriteLine("PostgreSQL business lock: request PID {0} waited on supplier blocker PID {1}.", waiter, blocker);
            _clock.Now = start;
        }
        finally
        {
            await transaction.RollbackAsync(Ct);
        }
        await AssertProblemAsync(await request, HttpStatusCode.Forbidden, "Licence.SecuritySuspended");
        Assert.Equal(before, await FactsAsync(source.Shop));
    }

    private static async Task AssertExpiredAsync(HttpClient client, Guid shop) =>
        await AssertProblemAsync(await client.GetAsync($"/api/licensing/shops/{shop}/trading-authority", Ct),
            HttpStatusCode.Forbidden, "Licence.Expired");
}
