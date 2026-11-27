using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// P06-03: owner-selected capacity reduction at renewal. Real PostgreSQL, merchant and licensing APIs,
/// local payment simulator and fixed clocks; never claims live payment-provider behavior.
/// </summary>
public sealed partial class P0603Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    internal static readonly string[] ThreeShops = ["North Branch", "Central Branch", "South Branch"];

    [Fact]
    public async Task OwnerPreviewsExplicitShopsAndLiveSeatConflicts()
    {
        await using var fixture = await StartAsync(database, January31);
        var package = await fixture.PackageAsync(2, ShopNames);
        var (purchase, term) = await PurchaseAsync(package, "Month");
        Assert.Equal(3, purchase.Capacity);

        // Admit 3 active staff in Shop[1] -> 4 active members total (1 owner + 3 staff), exceeding target allowance 2.
        var addedStaff = new List<Guid>();
        for (var i = 0; i < 3; i++)
            addedStaff.Add((await P0209Fixture.SignInAsync(fixture.Hosts, ShopRole.Staff, package.Shops[1].ShopId)).UserId);

        var solo = await PublishReductionPlanAsync(fixture, kind: "single-shop", name: "Solo Shop",
            includedShops: 1, membersPerShop: 2, monthlyPricePaise: 6000L, extraShopsOffered: false);
        var fewerSeatsSameSlots = await PublishReductionPlanAsync(fixture, kind: "multi-shop", name: "Compact Three",
            includedShops: 3, membersPerShop: 3, monthlyPricePaise: 9500L, extraShopsOffered: false);
        var dropUnusedSlot = await PublishReductionPlanAsync(fixture, kind: "multi-shop", name: "Dual Branch",
            includedShops: 2, membersPerShop: 5, monthlyPricePaise: 10000L, extraShopsOffered: false);
        fixture.At(February14);

        var preview = await CreatePreviewAsync(package, term, solo, 1, 3, 5, February29);
        Assert.Equal(term, preview.TermId);
        Assert.Equal(3, preview.PriorCapacity);
        Assert.Equal(1, preview.TargetCapacity);
        Assert.Equal(5, preview.PriorMemberAllowance);
        Assert.Equal(2, preview.TargetMemberAllowance);
        Assert.Equal("Independent", preview.TargetMode);
        Assert.Equal(6000L, preview.AmountPaise);
        Assert.Equal(February29, preview.CurrentPaidThrough);
        Assert.Equal(February29, preview.RenewalStartsAt);
        Assert.True(preview.HasMemberConflicts);
        Assert.Equal(1, preview.ConflictedShopCount);
        Assert.Equal(2, preview.SlotsRemoved);
        Assert.Equal(1, preview.UnusedSlots);
        Assert.Equal(1, preview.MinimumShopsToRemove);

        // Paged preview shop list names every covered shop with live counts and NO preselected retained flags.
        var page = await ReadPreviewShopsAsync(package, term, preview.PreviewId);
        Assert.Equal(2, page.Items.Length);
        Assert.All(page.Items, item => Assert.Null(item.Retained));
        var shop0 = Assert.Single(page.Items, item => item.ShopId == package.Shops[0].ShopId);
        var shop1 = Assert.Single(page.Items, item => item.ShopId == package.Shops[1].ShopId);
        Assert.Equal(1, shop0.ActiveMembers);
        Assert.False(shop0.ExceedsAllowance);
        Assert.Equal(4, shop1.ActiveMembers);
        Assert.True(shop1.ExceedsAllowance);
        Assert.Equal(2, shop1.ExcessMembers);

        // Same-slot seat reduction and unused-slot-only reduction both preview cleanly.
        var seatOnly = await CreatePreviewAsync(package, term, fewerSeatsSameSlots, 3, 3, 5, February29);
        Assert.Equal(0, seatOnly.SlotsRemoved);
        Assert.Equal(0, seatOnly.MinimumShopsToRemove);
        Assert.Equal(1, seatOnly.ConflictedShopCount);

        var slotOnly = await CreatePreviewAsync(package, term, dropUnusedSlot, 2, 3, 5, February29);
        Assert.Equal(1, slotOnly.SlotsRemoved);
        Assert.Equal(1, slotOnly.UnusedSlots);
        Assert.Equal(0, slotOnly.MinimumShopsToRemove);
        Assert.False(slotOnly.HasMemberConflicts);

        // Owner suspends 2 staff in Shop[1] -> fresh preview reflects live active count 2 with zero conflicts.
        for (var i = 0; i < 2; i++)
        {
            using var patch = await UpdateMemberStatusAsync(package.Shops[1], addedStaff[i], "Suspended", expectedVersion: 0);
            Assert.True(patch.IsSuccessStatusCode, await patch.Content.ReadAsStringAsync());
        }
        var refreshed = await CreatePreviewAsync(package, term, solo, 1, 3, 5, February29);
        Assert.False(refreshed.HasMemberConflicts);
        Assert.Equal(0, refreshed.ConflictedShopCount);
        var refreshedShop1 = Assert.Single((await ReadPreviewShopsAsync(package, term, refreshed.PreviewId)).Items,
            item => item.ShopId == package.Shops[1].ShopId);
        Assert.Equal(2, refreshedShop1.ActiveMembers);
        Assert.False(refreshedShop1.ExceedsAllowance);
    }

    internal static string ReductionsPath(Guid account, Guid term) =>
        $"/api/billing/accounts/{account:D}/subscriptions/{term:D}/capacity-reductions";

    internal static async Task<CapacityReductionPreviewV1> CreatePreviewAsync(Package package, Guid term,
        Guid targetPlanVersionId, int targetCapacity, int observedCapacity, int observedMemberAllowance,
        DateTimeOffset observedPaidThrough, Guid? operationKey = null, HttpClient? caller = null)
    {
        using var response = await PostPreviewAsync(package, term, targetPlanVersionId, targetCapacity,
            observedCapacity, observedMemberAllowance, observedPaidThrough, operationKey, caller);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {text}");
        Assert.True(response.Headers.CacheControl?.NoStore);
        return JsonSerializer.Deserialize<CapacityReductionPreviewV1>(text, JsonSerializerOptions.Web)!;
    }

    internal static Task<HttpResponseMessage> PostPreviewAsync(Package package, Guid term, Guid targetPlanVersionId,
        int targetCapacity, int observedCapacity, int observedMemberAllowance, DateTimeOffset observedPaidThrough,
        Guid? operationKey = null, HttpClient? caller = null) =>
        (caller ?? package.Payer.Client).PostAsJsonAsync(ReductionsPath(package.Account, term) + "/previews",
            new CreateCapacityReductionPreviewV1(operationKey ?? Guid.NewGuid(), targetPlanVersionId, targetCapacity,
                observedCapacity, observedMemberAllowance, observedPaidThrough));

    internal static async Task<CapacityReductionShopPageV1> ReadPreviewShopsAsync(Package package, Guid term, Guid previewId,
        Guid? after = null, HttpClient? caller = null)
    {
        var query = after is { } c ? $"?after={c:D}" : string.Empty;
        using var response = await (caller ?? package.Payer.Client).GetAsync(
            $"{ReductionsPath(package.Account, term)}/previews/{previewId:D}/shops{query}");
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<CapacityReductionShopPageV1>(text, JsonSerializerOptions.Web)!;
    }

    internal static Task<HttpResponseMessage> PostScheduleAsync(Package package, Guid term, CapacityReductionPreviewV1 preview,
        Guid[] retainedShopIds, Guid? operationKey = null, HttpClient? caller = null, string? snapshotHash = null,
        bool confirmed = true) =>
        (caller ?? package.Payer.Client).PostAsJsonAsync(ReductionsPath(package.Account, term),
            new ScheduleCapacityReductionV1(operationKey ?? Guid.NewGuid(), preview.PreviewId,
                snapshotHash ?? preview.SnapshotHash, retainedShopIds, preview.CustomerTermsVersion,
                preview.RefundPolicyVersion, confirmed));

    internal static async Task<ScheduledCapacityReductionV1> ScheduleAsync(Package package, Guid term,
        CapacityReductionPreviewV1 preview, Guid[] retainedShopIds, Guid? operationKey = null)
    {
        using var response = await PostScheduleAsync(package, term, preview, retainedShopIds, operationKey);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK, $"{(int)response.StatusCode}: {text}");
        Assert.True(response.Headers.CacheControl?.NoStore);
        return JsonSerializer.Deserialize<ScheduledCapacityReductionV1>(text, JsonSerializerOptions.Web)!;
    }

    internal static async Task<ScheduledCapacityReductionV1> ReadScheduleAsync(Package package, Guid term, HttpClient? caller = null)
    {
        using var response = await (caller ?? package.Payer.Client).GetAsync(ReductionsPath(package.Account, term));
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<ScheduledCapacityReductionV1>(text, JsonSerializerOptions.Web)!;
    }

    internal static async Task<CheckoutQuoteV1> CreateReductionOrderAsync(Package package, Guid term,
        ScheduledCapacityReductionV1 schedule, Guid? operationKey = null, HttpClient? caller = null)
    {
        using var response = await (caller ?? package.Payer.Client).PostAsJsonAsync(
            $"{ReductionsPath(package.Account, term)}/{schedule.ScheduleId:D}/order",
            new CreateReductionOrderV1(operationKey ?? Guid.NewGuid(), schedule.SnapshotHash));
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<CheckoutQuoteV1>(text, JsonSerializerOptions.Web)!;
    }

    internal static Task<HttpResponseMessage> PostResolveAsync(Package package, Guid term, ScheduledCapacityReductionV1 schedule,
        Guid? operationKey = null, long? expectedGeneration = null, HttpClient? caller = null) =>
        (caller ?? package.Payer.Client).PostAsJsonAsync(
            $"{ReductionsPath(package.Account, term)}/{schedule.ScheduleId:D}/resolve",
            new ResolveCapacityReductionV1(operationKey ?? Guid.NewGuid(),
                expectedGeneration ?? schedule.ResolutionGeneration, schedule.SnapshotHash));

    internal static async Task DrainReductionAsync(P0213Fixture delivery, int rounds = 3)
    {
        await delivery.ScanAsync(source: true);
        for (var i = 0; i < rounds; i++)
        {
            await delivery.DrainAsync(source: true);
            await delivery.DrainAsync(source: false);
        }
    }

    internal static Task<HttpResponseMessage> UpdateMemberStatusAsync(P0209Fixture.Merchant shopOwner, Guid userId,
        string status, long expectedVersion) =>
        shopOwner.Client.PatchAsJsonAsync($"/api/shops/{shopOwner.ShopId:D}/members/{userId:D}",
            new { role = "Staff", status, expectedVersion });

    internal static async Task<Guid> PublishReductionPlanAsync(P0308Fixture fixture, string kind, string name,
        int includedShops, int membersPerShop, long monthlyPricePaise, bool extraShopsOffered = false,
        long? extraShopMonthlyPricePaise = null, long? annualPricePaise = null)
    {
        var manager = fixture.Checkout.Trial.CatalogueManager!;
        var plan = Guid.NewGuid();
        var input = new Dictionary<string, object?>
        {
            ["expectedVersion"] = 0,
            ["kind"] = kind,
            ["name"] = name,
            ["monthlyPricePaise"] = monthlyPricePaise,
            ["annualPricePaise"] = annualPricePaise ?? monthlyPricePaise * 10,
            ["includedShops"] = includedShops,
            ["membersPerShop"] = membersPerShop,
            ["extraShopsOffered"] = extraShopsOffered,
            ["extraShopMonthlyPricePaise"] = extraShopsOffered ? (extraShopMonthlyPricePaise ?? 5000L) : null,
            ["extraShopAnnualPricePaise"] = extraShopsOffered ? ((extraShopMonthlyPricePaise ?? 5000L) * 10) : null,
            ["customerTermsVersion"] = "customer-local-1",
            ["refundPolicyVersion"] = "refund-local-1",
        };
        var draft = await P0207Tests.SavePlanAsync(manager, plan, input, HttpStatusCode.Created);
        var version = draft.GetProperty("versionId").GetGuid();
        var path = $"/api/admin/plans/{plan:D}/versions/{version:D}";
        var prepared = await P0207Tests.PrepareAsync(manager, path, Guid.NewGuid(), 1, fixture.Checkout.Trial.PolicyId);
        using var published = await P0207Tests.ConfirmAsync(manager, path, prepared);
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        return version;
    }
}
