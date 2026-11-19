using System.Net;
using System.Net.Http.Json;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

public sealed partial class P0408cLicensingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restricted_edit_can_keep_an_active_supplier_active(bool pendingActivation)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var binding = await OpenTradingAsync(client);
        var supplierId = await AddSupplierIdAsync(client, "Active supplier");
        _clock.Now = _clock.Now.AddDays(1);
        if (pendingActivation)
            await ApplyAsync(binding, _clock.Now.AddDays(1), 2, state: "PendingActivation");

        using var response = await RenameActiveSupplierAsync(client, supplierId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(("Renamed", true), await ReadSupplierEditStateAsync(binding.Shop, supplierId));
    }

    [Fact]
    public async Task Restricted_edit_revalidates_session_after_waiting_for_supplier_row()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var binding = await OpenTradingAsync(client);
        var supplierId = await AddSupplierIdAsync(client, "Active supplier");
        _clock.Now = _clock.Now.AddDays(1);

        await using var holder = Open(Migrator);
        await using var tx = await holder.Database.BeginTransactionAsync(Ct);
        await LockSupplierRowAsync(holder, binding.Shop, supplierId);
        var writing = RenameActiveSupplierAsync(client, supplierId);
        try
        {
            await AssertBlockedAsync(writing, Pid(holder));
            await using var db = Open(Migrator);
            var sessionId = ReadSession(client).SessionId;
            Assert.Equal(1, await db.Database.ExecuteSqlAsync($"""
                UPDATE auth_sessions SET revoked_at = {_clock.Now} WHERE id = {sessionId}
                """, Ct));
        }
        finally
        {
            await tx.CommitAsync(Ct);
        }

        await AssertProblemAsync(await writing, HttpStatusCode.Unauthorized, "Auth.SessionInvalidated");
        Assert.Equal(("Active supplier", true), await ReadSupplierEditStateAsync(binding.Shop, supplierId));
    }

    [Fact]
    public async Task Restricted_edit_times_out_on_supplier_row_and_can_retry()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var binding = await OpenTradingAsync(client);
        var supplierId = await AddSupplierIdAsync(client, "Active supplier");
        _clock.Now = _clock.Now.AddDays(1);

        await using var holder = Open(Migrator);
        await using var tx = await holder.Database.BeginTransactionAsync(Ct);
        await LockSupplierRowAsync(holder, binding.Shop, supplierId);
        var writing = RenameActiveSupplierAsync(client, supplierId);
        await AssertBlockedAsync(writing, Pid(holder));
        await AssertProblemAsync(await writing, HttpStatusCode.Conflict, "General.Conflict");
        Assert.Equal(("Active supplier", true), await ReadSupplierEditStateAsync(binding.Shop, supplierId));
        await tx.RollbackAsync(Ct);

        using var retry = await RenameActiveSupplierAsync(client, supplierId);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(("Renamed", true), await ReadSupplierEditStateAsync(binding.Shop, supplierId));
    }

    private static Task<HttpResponseMessage> RenameActiveSupplierAsync(HttpClient client, Guid supplierId) =>
        client.PutAsJsonAsync($"/api/suppliers/{supplierId}", SupplierInput("Renamed", true), Ct);

    private static Task<int> LockSupplierRowAsync(ApplicationDbContext db, Guid shop, Guid supplierId) =>
        db.Database.ExecuteSqlAsync($"""
            SELECT id FROM suppliers WHERE shop_id = {shop} AND id = {supplierId} FOR NO KEY UPDATE
            """, Ct);

    private async Task<(string Name, bool IsActive)> ReadSupplierEditStateAsync(Guid shop, Guid supplierId)
    {
        await using var db = Open(Migrator);
        var supplier = await db.Suppliers.SingleAsync(x => x.ShopId == shop && x.Id == supplierId, Ct);
        return (supplier.Name, supplier.IsActive);
    }
}
