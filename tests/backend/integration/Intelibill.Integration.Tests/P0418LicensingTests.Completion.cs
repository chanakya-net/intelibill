using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

public sealed partial class P0418LicensingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Supplier_deletion_preserves_baseline_authority_and_allows_an_existing_unused_supplier(bool expired)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var end = _clock.Now.AddMinutes(1);
        var binding = await OpenEnrolledShopAsync(client, end);
        var remove = await PrepareCapacityWriteAsync(client, "supplier-delete");
        await using var db = Open(Migrator);
        var system = await db.Suppliers.Where(x => x.ShopId == binding.Shop && x.IsSystem).Select(x => x.Id).SingleAsync(Ct);
        if (expired) _clock.Now = end;

        var before = await CountSourceFactsAsync(binding.Shop);
        var unowned = await ObserveAsync(await client.DeleteAsync($"/api/suppliers/{Guid.NewGuid():D}", Ct));
        Assert.Equal(HttpStatusCode.NotFound, unowned.Status);
        var denied = await ObserveAsync(await client.DeleteAsync($"/api/suppliers/{system:D}", Ct));
        Assert.Equal(new Observed(HttpStatusCode.Conflict, "Supplier.CannotModifySystemSupplier"), denied);
        Assert.Equal(before, await CountSourceFactsAsync(binding.Shop));

        var accepted = await ObserveAsync(await remove());
        Assert.Equal(HttpStatusCode.NoContent, accepted.Status);
        Assert.False(await db.Suppliers.AnyAsync(x => x.ShopId == binding.Shop && !x.IsSystem, Ct));
        Assert.True(await db.Suppliers.AnyAsync(x => x.Id == system, Ct));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Legacy_batch_route_keeps_the_quantity_protocol_denial_at_expiry(bool expired)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var end = _clock.Now.AddMinutes(1);
        var binding = await OpenEnrolledShopAsync(client, end);
        var batch = await SeedGoodsAsync(client);
        var before = await CountSourceFactsAsync(binding.Shop);
        if (expired) _clock.Now = end;

        var result = await ObserveAsync(await client.PutAsJsonAsync($"/api/inventory/batches/{batch:D}", new
        {
            quantity = 11m, costPrice = 1m, mrp = 2m, salesPrice = 2m, taxRatePercent = 0m, taxIncluded = false,
        }, Ct));
        Assert.Equal(new Observed(HttpStatusCode.BadRequest, "Inventory.BatchQuantityEditMoved"), result);
        Assert.Equal(before, await CountSourceFactsAsync(binding.Shop));
        await using var db = Open(Migrator);
        Assert.Equal(10m, await db.InventoryBatches.Where(x => x.Id == batch).Select(x => x.Quantity).SingleAsync(Ct));
    }
}
