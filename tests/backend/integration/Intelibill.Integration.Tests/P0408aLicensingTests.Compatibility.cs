using System.Net;
using System.Net.Http.Json;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

public sealed partial class P0408aLicensingTests
{
    [Fact]
    public async Task Pending_shop_keeps_existing_stock_history_and_quantity_validation()
    {
        using var host = CreateHost(new Catalogue());
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        using var received = await client.PostAsJsonAsync("/api/inventory/inbound", new
        {
            itemName = "Held rice", barcode = "held", uom = "kg", batchNumber = "LOT", quantity = 3.125m,
            totalPurchaseCost = 6m, mrp = 2m, salesPrice = 2m, taxRatePercent = 0m,
            taxIncluded = false, purchaseTaxIncluded = false,
        }, Ct);
        await ReadSuccessAsync(received);
        var before = await ReadStockAsync(shop);
        var binding = await BindAsync(shop);
        await ApplyAsync(binding, _clock.Now.AddDays(1), state: "PendingActivation");
        await EnforceAsync(shop);

        foreach (var writer in new[] { "item", "service", "import" })
            await AssertProblemAsync(await WriteAsync(client, writer, "pending"), HttpStatusCode.Forbidden,
                "Licence.PendingActivation");

        using var list = await client.GetAsync("/api/items", Ct);
        await ReadSuccessAsync(list);
        using var stream = await client.GetAsync("/api/items/stream", Ct);
        Assert.Equal(HttpStatusCode.OK, stream.StatusCode);
        Assert.Contains("held", await stream.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        using var details = await client.GetAsync("/api/items/details?name=Held%20rice&barcode=unused", Ct);
        Assert.Equal("Held rice", (await ReadSuccessAsync(details)).GetProperty("name").GetString());

        await AssertQuantityUnitCannotChangeAsync(client, before.Id);
        Assert.Equal(before, await ReadStockAsync(shop));
        await AssertDefinitionCountAsync(shop, "service", 0);
    }

    [Theory]
    [InlineData("Active", "Trial")]
    [InlineData("Grace", "Paid")]
    public async Task Current_trial_and_grace_keep_definition_creation_available(string state, string basis)
    {
        using var host = CreateHost(new Catalogue());
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        await ApplyAsync(await BindAsync(shop), _clock.Now.AddHours(1), state: state, basis: basis);
        await EnforceAsync(shop);

        foreach (var writer in new[] { "item", "service", "import" })
        {
            using var response = await WriteAsync(client, writer, writer);
            await ReadSuccessAsync(response);
        }
        await AssertDefinitionCountAsync(shop, "item", 2);
        await AssertDefinitionCountAsync(shop, "service", 1);
    }

    [Fact]
    public async Task Verification_outage_blocks_only_the_enrolled_shop()
    {
        using var host = CreateHost(new Catalogue());
        await host.InitializeAsync();
        using var enrolled = CreateClient(host);
        using var observeOnly = CreateClient(host);
        using var noProjection = CreateClient(host);
        var enrolledShop = await CreateShopAsync(enrolled);
        var observedShop = await CreateShopAsync(observeOnly);
        var thirdShop = await CreateShopAsync(noProjection);
        var verified = _clock.Now.AddHours(-48).AddMinutes(1);
        var paidEnd = _clock.Now.AddDays(1);
        await ApplyAsync(await BindAsync(enrolledShop), paidEnd, verified: verified);
        await ApplyAsync(await BindAsync(observedShop), paidEnd, verified: verified);
        await EnforceAsync(enrolledShop);
        _clock.Now = verified.AddHours(48);

        foreach (var writer in new[] { "item", "service", "import" })
        {
            await AssertProblemAsync(await WriteAsync(enrolled, writer, writer), HttpStatusCode.ServiceUnavailable,
                "Licence.VerificationUnavailable");
            using var observed = await WriteAsync(observeOnly, writer, writer);
            await ReadSuccessAsync(observed);
            using var unprojected = await WriteAsync(noProjection, writer, writer);
            await ReadSuccessAsync(unprojected);
        }

        await AssertDefinitionCountAsync(enrolledShop, "item", 0);
        await AssertDefinitionCountAsync(enrolledShop, "service", 0);
        await AssertDefinitionCountAsync(observedShop, "item", 2);
        await AssertDefinitionCountAsync(observedShop, "service", 1);
        await AssertDefinitionCountAsync(thirdShop, "item", 2);
        await AssertDefinitionCountAsync(thirdShop, "service", 1);
    }

    [Fact]
    public async Task Staff_can_only_create_via_an_operating_barcode_import()
    {
        using var host = CreateHost(new Catalogue());
        await host.InitializeAsync();
        using var owner = CreateClient(host);
        using var staff = CreateClient(host);
        var binding = await OpenTradingAsync(owner);
        await RegisterAsync(staff);
        await using (var db = Open(Migrator))
        {
            db.ShopMemberships.Add(ShopMembership.Create(binding.Shop, ReadSession(staff).UserId, ShopRole.Staff, false));
            await db.SaveChangesAsync(Ct);
        }
        using var switched = await staff.PostAsJsonAsync("/api/shops/switch", new { shopId = binding.Shop }, Ct);
        staff.DefaultRequestHeaders.Authorization = new("Bearer", (await ReadSuccessAsync(switched)).GetProperty("accessToken").GetString());

        using var itemPost = await WriteAsync(staff, "item", "staff-post");
        using var servicePost = await WriteAsync(staff, "service", "staff-post");
        using var serviceList = await staff.GetAsync("/api/services", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, itemPost.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, servicePost.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, serviceList.StatusCode);
        using var imported = await WriteAsync(staff, "import", "staff-import");
        await ReadSuccessAsync(imported);

        await ApplyAsync(binding, _clock.Now.AddDays(1), 2, restriction: "Trading");
        using var local = await staff.GetAsync(Details("staff-import"), Ct);
        await ReadSuccessAsync(local);
        await AssertProblemAsync(await WriteAsync(staff, "import", "restricted-import"), HttpStatusCode.Forbidden,
            "Licence.TradingRestricted");
        await AssertDefinitionCountAsync(binding.Shop, "item", 1);
        await AssertDefinitionCountAsync(binding.Shop, "service", 0);
    }

    [Theory]
    [InlineData("item")]
    [InlineData("import")]
    public async Task Product_created_notification_observes_the_committed_item(string writer)
    {
        var witness = new ProductNotificationWitness(Migrator);
        using var host = CreateHost(new Catalogue(), witness);
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var binding = await OpenTradingAsync(client);
        await ApplyAsync(binding, _clock.Now.AddDays(1), 2, restriction: "Trading");
        await AssertProblemAsync(await WriteAsync(client, writer, "rejected-event"), HttpStatusCode.Forbidden,
            "Licence.TradingRestricted");
        Assert.False(witness.Observed.Task.IsCompleted);

        await ApplyAsync(binding, _clock.Now.AddDays(1), 3, releasedRevision: 2);
        using var created = await WriteAsync(client, writer, "committed-event");
        await ReadSuccessAsync(created);
        var observed = await witness.Observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(("committed-event", true), observed);
        _output.WriteLine("Separate PostgreSQL connection found the committed {0} during product notification", writer);
    }

    private async Task<StockSnapshot> ReadStockAsync(Guid shop)
    {
        await using var db = Open(Migrator);
        var item = await db.Items.SingleAsync(x => x.ShopId == shop, Ct);
        var quantity = await db.InventoryBatches.Where(x => x.ShopId == shop).Select(x => x.Quantity).SingleAsync(Ct);
        var movementCount = await db.StockTransactions.CountAsync(x => x.ShopId == shop, Ct);
        _output.WriteLine("PostgreSQL items: 1; unit: {0}; scale: {1}; batches: 1; quantity: {2}; stock movements: {3}",
            item.UnitCode, item.QuantityScale, quantity, movementCount);
        return new(item.Id, item.UnitCode, item.QuantityScale, quantity, movementCount);
    }

    private static async Task AssertQuantityUnitCannotChangeAsync(HttpClient client, Guid itemId)
    {
        await AssertProblemAsync(await client.PatchAsJsonAsync($"/api/items/{itemId}",
            new { name = "Held rice", barcode = "held", uom = "pcs", defaultTaxRatePercent = 0m }, Ct),
            HttpStatusCode.Conflict, "Item.UnitChangeWithHistory");
    }

    private sealed record StockSnapshot(Guid Id, string? UnitCode, int QuantityScale, decimal Quantity, int MovementCount);

    private sealed class ProductNotificationWitness(string connection) : IProductHubNotifier
    {
        public TaskCompletionSource<(string Barcode, bool Exists)> Observed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task NotifyProductCreatedAsync(Guid itemId, string barcode, string name, Guid shopId,
            CancellationToken cancellationToken = default)
        {
            await using var db = Open(connection);
            var exists = await db.Items.AnyAsync(x => x.ShopId == shopId && x.Id == itemId, cancellationToken);
            Observed.TrySetResult((barcode, exists));
        }
    }
}
