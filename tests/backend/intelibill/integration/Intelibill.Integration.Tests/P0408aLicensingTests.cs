using ErrorOr;
using Intelibill.Application.Common.Interfaces;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

public sealed partial class P0408aLicensingTests
{
    [Fact]
    public async Task Item_creation_uses_current_capacity()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        var binding = await BindAsync(shop);
        var end = _clock.Now.AddMinutes(1);
        await ApplyAsync(binding, end);
        await EnforceAsync(shop);

        _clock.Now = end.AddMicroseconds(-1);
        using var allowed = await client.PostAsJsonAsync("/api/items", ItemRequest("existing"), Ct);
        await ReadSuccessAsync(allowed);

        _clock.Now = end;
        await AssertProblemAsync(await client.PostAsJsonAsync("/api/items", ItemRequest("expired"), Ct),
            HttpStatusCode.Forbidden, "Licence.Expired");

        await ApplyAsync(binding, end.AddDays(1), revision: 2, state: "PendingActivation");
        await AssertProblemAsync(await client.PostAsJsonAsync("/api/items", ItemRequest("pending"), Ct),
            HttpStatusCode.Forbidden, "Licence.PendingActivation");
        using var details = await client.GetAsync("/api/items/details?barcode=existing", Ct);
        await ReadSuccessAsync(details);

        await using var db = Open(Migrator);
        var items = await db.Items.Where(x => x.ShopId == shop).Select(x => x.Barcode).ToArrayAsync(Ct);
        Assert.Equal("existing", Assert.Single(items));
        _output.WriteLine("PostgreSQL item rows after expiry and pending refusals: {0}", items.Length);
    }

    [Fact]
    public async Task Service_creation_and_code_allocation_are_atomic()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        var binding = await BindAsync(shop);
        var end = _clock.Now.AddMinutes(1);
        await ApplyAsync(binding, end);
        await EnforceAsync(shop);

        var first = await Task.WhenAll(PostServiceAsync(client, "Repair"), PostServiceAsync(client, "Consulting"));
        Assert.Equal("SRV-000001", first.Min(StringComparer.Ordinal));
        Assert.Equal("SRV-000002", first.Max(StringComparer.Ordinal));
        await AssertProblemAsync(await client.PostAsJsonAsync("/api/services", ServiceRequest("Repair"), Ct),
            HttpStatusCode.Conflict, "Service.NameAlreadyExists");
        _clock.Now = end;
        await AssertProblemAsync(await client.PostAsJsonAsync("/api/services", ServiceRequest("Expired"), Ct),
            HttpStatusCode.Forbidden, "Licence.Expired");
        await AssertExistingServiceOperationsAsync(client);

        await using var db = Open(Migrator);
        var count = await db.Services.CountAsync(x => x.ShopId == shop, Ct);
        var next = await db.ServiceCodeSequences.Where(x => x.ShopId == shop).Select(x => x.NextNumber).SingleAsync(Ct);
        Assert.Equal(2, count);
        Assert.Equal(3, next);
        _output.WriteLine("PostgreSQL services: {0}; next code number: {1}", count, next);
    }

    [Fact]
    public async Task Barcode_lookup_gates_only_a_new_definition()
    {
        using var host = CreateHost(new Catalogue());
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        var binding = await BindAsync(shop);
        var end = _clock.Now.AddMinutes(1);
        await ApplyAsync(binding, end);
        await EnforceAsync(shop);

        using var imported = await client.GetAsync(Details("imported"), Ct);
        Assert.Equal("Fixture catalogue product", (await ReadSuccessAsync(imported)).GetProperty("name").GetString());
        _clock.Now = end;

        using var history = await client.GetAsync(Details("imported"), Ct);
        await ReadSuccessAsync(history);
        using var missing = await client.GetAsync(Details("unknown"), Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        await AssertProblemAsync(await client.GetAsync(Details("blocked-import"), Ct),
            HttpStatusCode.Forbidden, "Licence.Expired");

        await using var db = Open(Migrator);
        var barcodes = await db.Items.Where(x => x.ShopId == shop).Select(x => x.Barcode).ToArrayAsync(Ct);
        Assert.Equal("imported", Assert.Single(barcodes));
        Assert.False(await db.InventoryBatches.AnyAsync(x => x.ShopId == shop, Ct));
        Assert.False(await db.StockTransactions.AnyAsync(x => x.ShopId == shop, Ct));
        _output.WriteLine("PostgreSQL imported definitions: {0}; inventory batches: 0; stock transactions: 0", barcodes.Length);
    }

    private static string Details(string barcode) => $"/api/items/details?barcode={barcode}";

    private sealed class Catalogue(Func<string, Task>? beforeLookup = null) : IExternalProductLookupService
    {
        public async Task<ErrorOr<ExternalProductLookupResult?>> LookupByBarcodeAsync(
            string barcode, string? authorizationHeader, CancellationToken cancellationToken)
        {
            if (beforeLookup is not null)
                await beforeLookup(barcode).WaitAsync(cancellationToken);
            return barcode == "unknown"
                ? (ExternalProductLookupResult?)null
                : new ExternalProductLookupResult("Fixture catalogue product", "Fixture description", "pcs");
        }
    }

    private static async Task AssertExistingServiceOperationsAsync(HttpClient client)
    {
        using var history = await client.GetAsync("/api/services", Ct);
        var existing = (await ReadSuccessAsync(history)).EnumerateArray().Single(x => x.GetProperty("name").GetString() == "Repair");
        var id = existing.GetProperty("serviceId").GetGuid();
        using var updated = await client.PatchAsJsonAsync($"/api/services/{id}", ServiceRequest("Repair updated"), Ct);
        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
        using var activated = await client.PostAsync($"/api/services/{id}/activate", null, Ct);
        Assert.Equal(HttpStatusCode.NoContent, activated.StatusCode);
        using var deactivated = await client.PostAsync($"/api/services/{id}/deactivate", null, Ct);
        Assert.Equal(HttpStatusCode.NoContent, deactivated.StatusCode);
    }

    private static async Task<string> PostServiceAsync(HttpClient client, string name)
    {
        using var response = await client.PostAsJsonAsync("/api/services", ServiceRequest(name), Ct);
        var result = await ReadSuccessAsync(response);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return result.GetProperty("code").GetString()!;
    }

    private static object ServiceRequest(string name) =>
        new { name, price = 10m, taxRatePercent = 0m, taxIncluded = false, isActive = false };

    private static object ItemRequest(string barcode) =>
        new { name = barcode, barcode, uom = "pcs", isActive = false, defaultTaxRatePercent = 0m };
}
