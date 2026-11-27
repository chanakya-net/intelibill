using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// P06-06: Reviewed early coverage-replacement exceptions with live owner consents, recent Licensing Manager MFA,
/// merchant-fenced permit verification, durable source cutoff, paid-slot conservation and separately auditable
/// courtesy bridges. Real PostgreSQL, real merchant and licensing APIs, fixed clocks, local simulator only.
/// </summary>
public sealed partial class P0606Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    internal static readonly DateTimeOffset EarlyStart = new(2028, 2, 14, 10, 0, 0, TimeSpan.Zero);

    internal static string PathFor(Package p, Guid term) =>
        $"/api/billing/accounts/{p.Account}/subscriptions/{term}/coverage-replacement-exceptions";

    internal static string OwnerConsentPath(Package p, Guid shopId, Guid exceptionId) =>
        $"/api/billing/accounts/{p.Account}/coverage-replacement-exceptions/{shopId}/{exceptionId}/consent";

    internal static string AdminListPath(Guid account) =>
        $"/api/admin/accounts/{account}/coverage-replacement-exceptions";

    internal static string AdminPreviewPath(Guid account, Guid exceptionId) =>
        $"/api/admin/accounts/{account}/coverage-replacement-exceptions/{exceptionId}/preview";

    internal static string AdminApprovePath(Guid account, Guid exceptionId) =>
        $"/api/admin/accounts/{account}/coverage-replacement-exceptions/{exceptionId}/approve";

    internal static string AdminRecoverPath(Guid account, Guid exceptionId, Guid operationKey) =>
        $"/api/admin/accounts/{account}/coverage-replacement-exceptions/{exceptionId}/operations/{operationKey}";

    [Fact]
    public async Task UnhandledPermitKeepsPaidSlotAndPreviewShowsUnknownQueues()
    {
        using var key = RSA.Create(2048);
        await using var f = await StartPermitFixtureAsync(database, key, January31);
        var (p, term) = await PrepareWithTradingNorthAsync(f);
        var permit = await IssueRealPermitAsync(f, p.Shops[2], EarlyStart, deliverObservation: true);
        Assert.True(permit.ValidUntil > EarlyStart, permit.ValidUntil.ToString("O"));
        Assert.True(permit.ValidUntil < February29, permit.ValidUntil.ToString("O"));

        // Unhandled old permit keeps the paid slot occupied; ordinary slot reuse has zero spare slots and South cannot trade.
        using var slots = await p.Payer.Client.GetAsync($"/api/billing/accounts/{p.Account}/subscriptions/{term}/slot-assignments");
        Assert.Equal(HttpStatusCode.OK, slots.StatusCode);
        Assert.Equal(0, (await slots.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("unusedSlots").GetInt32());
        Assert.Equal("Licence.PendingActivation", await FencedTradingAtAsync(f, p.Shops[3].ShopId, EarlyStart));

        // Billing Owner previews early replacement: sees normal renewal date, observed permit deadline and unknown queues notice.
        using var previewResponse = await p.Payer.Client.GetAsync(PathFor(p, term));
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = await previewResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(February29, preview.GetProperty("normalRenewalAt").GetDateTimeOffset());
        Assert.Contains("unknown", preview.GetProperty("disconnectedQueuesNotice").GetString(), StringComparison.OrdinalIgnoreCase);
        var north = Assert.Single(preview.GetProperty("coveredShops").EnumerateArray(),
            s => s.GetProperty("shopId").GetGuid() == p.Shops[2].ShopId);
        Assert.Equal("North", north.GetProperty("shopName").GetString());
        Assert.Equal(permit.ValidUntil, north.GetProperty("observedPermitDeadline").GetDateTimeOffset());
        var south = Assert.Single(preview.GetProperty("eligibleShops").EnumerateArray());
        Assert.Equal("South", south.GetProperty("shopName").GetString());
        Assert.Equal(p.Shops[3].ShopId, south.GetProperty("shopId").GetGuid());

        // Billing Owner opens an early-review request binding the exact commercial terms and acknowledgements.
        using var createdResponse = await p.Payer.Client.PostAsJsonAsync(PathFor(p, term), BuildRequest(p, Guid.NewGuid(),
            EarlyStart, permit.ValidUntil, "CourtesyBridge", permit.ValidUntil));
        Assert.Equal(HttpStatusCode.OK, createdResponse.StatusCode);
        var created = await createdResponse.Content.ReadFromJsonAsync<JsonElement>();
        var exceptionId = created.GetProperty("exceptionId").GetGuid();
        Assert.Equal("AwaitingConsents", created.GetProperty("state").GetString());
        Assert.Equal(February29, created.GetProperty("normalRenewalAt").GetDateTimeOffset());
        Assert.Equal(EarlyStart, created.GetProperty("requestedEarlyStartAt").GetDateTimeOffset());
        Assert.Equal(permit.ValidUntil, created.GetProperty("paidCutoverAt").GetDateTimeOffset());
        Assert.True(created.GetProperty("disconnectedQueuesUnknown").GetBoolean());

        // Licensing Manager inspects the request and previews it: sees named shops, normal renewal, observed permit, unknown queues, and cannot approve yet.
        var (manager, _) = await f.Checkout.Trial.Catalogue.ManagerAsync("licensing.manager@example.test");
        using var _ = manager;
        using var listResponse = await manager.SendAsync(HttpMethod.Get, AdminListPath(p.Account));
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listed = Assert.Single((await P0206Fixture.BodyAsync(listResponse)).GetProperty("items").EnumerateArray());
        Assert.Equal(exceptionId, listed.GetProperty("exceptionId").GetGuid());

        using var adminPreviewResponse = await manager.SendAsync(HttpMethod.Post, AdminPreviewPath(p.Account, exceptionId),
            new { }, await P0206Fixture.ProofAsync(manager));
        Assert.Equal(HttpStatusCode.OK, adminPreviewResponse.StatusCode);
        var adminPreview = await P0206Fixture.BodyAsync(adminPreviewResponse);
        Assert.Equal("North", adminPreview.GetProperty("outgoingShopName").GetString());
        Assert.Equal("South", adminPreview.GetProperty("incomingShopName").GetString());
        Assert.Equal(February29, adminPreview.GetProperty("normalRenewalAt").GetDateTimeOffset());
        Assert.Equal(permit.ValidUntil, adminPreview.GetProperty("observedPermitDeadline").GetDateTimeOffset());
        Assert.True(adminPreview.GetProperty("disconnectedQueuesUnknown").GetBoolean());
        Assert.False(adminPreview.GetProperty("canApprove").GetBoolean());
        Assert.Equal(3L, await f.CountAsync("payment_fulfilment_shops", $"term_id='{term}'"));
        Assert.Equal("Licence.PendingActivation", await FencedTradingAtAsync(f, p.Shops[3].ShopId, EarlyStart));
    }

    internal static object BuildRequest(Package p, Guid key, DateTimeOffset earlyStart, DateTimeOffset paidCutover,
        string disposition, DateTimeOffset? observedPermitDeadline, Guid? outgoing = null, Guid? incoming = null,
        string reason = "Branch relocating mid-term before lease handback", bool ackSelection = true,
        bool ackAllocation = true, bool ackPermits = true, bool ackUnknown = true) => new
    {
        operationKey = key,
        outgoingShopId = outgoing ?? p.Shops[2].ShopId,
        incomingShopId = incoming ?? p.Shops[3].ShopId,
        requestedEarlyStartAt = earlyStart,
        proposedPaidCutoverAt = paidCutover,
        overlapDisposition = disposition,
        reason,
        observedCapacity = 3,
        observedMemberAllowance = 5,
        observedNormalRenewalAt = February29,
        observedPermitDeadline,
        acknowledgedShopSelection = ackSelection,
        acknowledgedRemainingAccessAllocation = ackAllocation,
        acknowledgedExistingPermits = ackPermits,
        acknowledgedUnknownDisconnectedQueues = ackUnknown,
    };

    internal static async Task<P0308Fixture> StartPermitFixtureAsync(LicensingPostgreSqlFixture db, RSA key, DateTimeOffset at,
        bool browser = false)
    {
        var business = new P0201Clock { Now = at };
        var checkout = await P0302Fixture.StartAsync(db, business, browser: browser, merchantExtras: new Dictionary<string, string?>
        {
            ["Jwt:AccessTokenExpiryMinutes"] = "360",
            ["OfflineLicensePermit:LocalKeyId"] = "p0606-test",
            ["OfflineLicensePermit:LocalPrivateKeyPem"] = key.ExportPkcs8PrivateKeyPem(),
        }, merchantServices: services => services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .PostConfigure<TimeProvider>((options, clock) => options.TokenValidationParameters.LifetimeValidator =
                (nbf, exp, _, _) => nbf <= clock.GetUtcNow().UtcDateTime && exp > clock.GetUtcNow().UtcDateTime));
        var f = new P0308Fixture(checkout);
        f.At(at);
        return f;
    }

    internal static async Task<(Package Package, Guid Term)> PrepareWithTradingNorthAsync(P0308Fixture f)
    {
        var p = await f.PackageAsync(4, ["Central", "East", "North", "South"], tradingIndex: 2);
        var (_, term) = await PurchaseAsync(p with { Shops = p.Shops[..3] }, "Month");
        await f.Checkout.Delivery.DrainAsync(true);
        await f.Checkout.Delivery.DrainAsync(false);
        f.At(EarlyStart);
        return (p, term);
    }

    internal static async Task<P0507Tests.Permit> IssueRealPermitAsync(P0308Fixture f, P0209Fixture.Merchant shop,
        DateTimeOffset at, bool deliverObservation, string device = "North disconnected till")
    {
        var auth = f.Hosts.Fixture.Clock;
        var originalAuth = auth.Now;
        var originalHeader = shop.Client.DefaultRequestHeaders.Authorization;
        try
        {
            f.At(at);
            auth.Now = at;
            f.Checkout.Delivery.Transport.LeaseClock.Now = at.AddSeconds(1);
            await f.Checkout.Delivery.ScanAsync(true);
            await f.Checkout.Delivery.DrainAsync(true);
            await SignInAtAsync(shop);
            var permit = await P0507Tests.Run.IssuePermitAsync(shop, device);
            if (deliverObservation)
                await f.Checkout.Delivery.DrainAsync(false);
            return permit;
        }
        finally
        {
            auth.Now = originalAuth;
            shop.Client.DefaultRequestHeaders.Authorization = originalHeader;
        }
    }

    internal static async Task<string?> FencedTradingAtAsync(P0308Fixture f, Guid shopId, DateTimeOffset at)
    {
        var clock = f.Hosts.Fixture.Clock;
        var original = clock.Now;
        try
        {
            clock.Now = at;
            return await P0604Tests.FencedTradingAsync(f, shopId);
        }
        finally { clock.Now = original; }
    }

    internal static async Task SignInAtAsync(P0209Fixture.Merchant merchant)
    {
        merchant.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await P0201Tests.LoginAsync(merchant.Client, merchant.Email));
        using var switched = await merchant.Client.PostAsJsonAsync("/api/shops/switch", new { shopId = merchant.ShopId });
        switched.EnsureSuccessStatusCode();
        merchant.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            (await switched.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString());
    }
}
