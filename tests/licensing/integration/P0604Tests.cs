using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0604Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    internal static string Slots(Package package, Guid term) =>
        $"/api/billing/accounts/{package.Account}/subscriptions/{term}/slot-assignments";

    internal static object Request(Guid shop, Guid key) => new
    {
        operationKey = key, shopId = shop, observedCapacity = 3, observedPaidThrough = February29,
        acknowledgedSharedEnd = true, acknowledgedNoNewFee = true,
    };

    internal static async Task<(Package Package, Guid Term)> PrepareAsync(P0308Fixture fixture)
    {
        var package = await fixture.PackageAsync(4, ["Central", "East", "North", "South"]);
        var purchase = package with { Shops = package.Shops[..2] };
        var (_, term) = await PurchaseAsync(purchase, "Month");
        await fixture.Checkout.Delivery.DrainAsync(true);
        await fixture.Checkout.Delivery.DrainAsync(false);
        fixture.At(February14);
        return (package, term);
    }

    [Fact]
    public void CoverageSlotContractPreservesTheAcknowledgementsAndSignedRoutes()
    {
        var path = Path.Combine(ProjectPaths.Root, "tests/contracts/licensing/coverage-slot-assignment-v1.json");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;
        var request = root.GetProperty("request").Deserialize<AssignUnusedCoverageSlotV1>(EntitlementSnapshotsV1.Json);
        Assert.NotNull(request);
        Assert.True(request.AcknowledgedSharedEnd && request.AcknowledgedNoNewFee);
        Assert.Equal(February29, request.ObservedPaidThrough);
        Assert.Equal(3, request.ObservedCapacity);
        Assert.Equal(CoverageSlotsV1.Kind, root.GetProperty("receiptKind").GetString());
        Assert.Equal(CoverageSlotsV1.ReceiptPath, root.GetProperty("receiptPath").GetString());
        Assert.Equal(CoverageSlotsV1.PageSize, root.GetProperty("pageSize").GetInt32());
        Assert.Equal([CoverageSlotsV1.Read, CoverageSlotsV1.Request, CoverageSlotsV1.Recover, CoverageSlotsV1.OwnerRead],
            root.GetProperty("actions").EnumerateArray().Select(x => x.GetString()!).ToArray());
    }

    [Fact]
    public async Task UnusedSlotRequestFreezesTheSharedEndForANamedAffiliatedShop()
    {
        await using var fixture = await StartAsync(database, January31);
        var (package, term) = await PrepareAsync(fixture);
        var path = Slots(package, term);
        var target = package.Shops[2];
        var empty = await package.Payer.Client.GetFromJsonAsync<CoverageSlotTermV1>(path + "?page=2");
        Assert.Empty(empty!.EligibleShops);
        var command = Request(target.ShopId, Guid.NewGuid());
        using var posted = await package.Payer.Client.PostAsJsonAsync(path, command);
        Assert.True(posted.IsSuccessStatusCode, $"Slot request returned {(int)posted.StatusCode}: {await posted.Content.ReadAsStringAsync()}");
        var assignment = await posted.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("AwaitingConsent", assignment.GetProperty("state").GetString());
        Assert.Equal(target.ShopId, assignment.GetProperty("shopId").GetGuid());
        Assert.Equal(February29, assignment.GetProperty("coverageEndsAt").GetDateTimeOffset());
        Assert.Equal(2L, await fixture.CountAsync("payment_fulfilment_shops", $"term_id='{term}'"));
        using var replay = await package.Payer.Client.PostAsJsonAsync(path, command);
        replay.EnsureSuccessStatusCode();
        Assert.Equal(assignment.GetProperty("assignmentId").GetGuid(),
            (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("assignmentId").GetGuid());
    }

    [Fact]
    public async Task MissingAcknowledgementStaleDeadlineAndIneligibleShopsLeaveCapacityUntouched()
    {
        await using var fixture = await StartAsync(database, January31);
        var (p, term) = await PrepareAsync(fixture);
        var path = Slots(p, term);
        var command = new AssignUnusedCoverageSlotV1(Guid.NewGuid(), p.Shops[2].ShopId, 3, February29, true, true);
        foreach (var (invalid, code) in new[]
        {
            (command with { AcknowledgedNoNewFee = false }, "Coverage.SlotInvalid"),
            (command with { AcknowledgedSharedEnd = false }, "Coverage.SlotInvalid"),
            (command with { ObservedCapacity = 4 }, "Coverage.SlotSourceChanged"),
            (command with { ObservedPaidThrough = February29.AddSeconds(1) }, "Coverage.SlotSourceChanged"),
            (command with { ShopId = p.Shops[0].ShopId }, "Coverage.SlotShopIneligible"),
            (command with { ShopId = Guid.NewGuid() }, "Coverage.SlotShopIneligible"),
        })
        {
            using var refused = await p.Payer.Client.PostAsJsonAsync(path, invalid);
            Assert.Equal(code, await P0206Fixture.CodeAsync(refused));
        }
        Assert.Equal(0L, await fixture.CountAsync("coverage_slot_assignments"));
        Assert.Equal(2L, await fixture.CountAsync("payment_fulfilment_shops"));
    }

    [Fact]
    public async Task ConcurrentRequestsForOneShopCannotReserveOverlappingCoverage()
    {
        await using var fixture = await StartAsync(database, January31);
        var (package, term) = await PrepareAsync(fixture);
        var path = Slots(package, term);
        var shop = package.Shops[2].ShopId;
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
            package.Payer.Client.PostAsJsonAsync(path, Request(shop, Guid.NewGuid()))));
        Assert.Single(responses, response => response.IsSuccessStatusCode);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(1L, await fixture.CountAsync("coverage_slot_assignments", $"shop_id='{shop}'"));
    }
}
