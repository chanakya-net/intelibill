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
}
