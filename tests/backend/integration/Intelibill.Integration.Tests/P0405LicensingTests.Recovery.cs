using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

public sealed partial class P0405LicensingTests
{
    [Fact]
    public async Task Committed_sale_replays_only_under_current_recovery_authority()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var owner = CreateClient(host);
        using var staff = CreateClient(host);
        var end = _clock.Now.AddMinutes(1);
        var binding = await OpenTradingAsync(owner, end);
        var staffId = await JoinAsStaffAsync(staff, binding.Shop);
        var service = await SeedServiceAsync(owner);
        var payload = SaleRequest(service);
        var lookup = $"/api/sales/by-idempotency-key/{payload.IdempotencyKey}";
        var original = await PostSaleAsync(owner, payload);

        _clock.Now = end;
        AssertReplay(original, await PostSaleAsync(owner, payload));
        using var found = await owner.GetAsync(lookup, Ct);
        Assert.Equal(original.GetProperty("saleId").GetGuid(), (await ReadSuccessAsync(found)).GetProperty("saleId").GetGuid());
        using var other = await staff.GetAsync(lookup, Ct);
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        await AssertProblemAsync(await staff.PostAsJsonAsync("/api/sales", payload, Ct),
            HttpStatusCode.Forbidden, "Licence.Expired");
        await AssertProblemAsync(await owner.PostAsJsonAsync("/api/sales", payload with { PaidAmount = 2m }, Ct),
            HttpStatusCode.Conflict, "Sale.IdempotencyConflict");

        await ApplyAsync(binding, end.AddDays(1), 2, "Trading", "Expired");
        AssertReplay(original, await PostSaleAsync(owner, payload));
        await ApplyAsync(binding, end.AddDays(1), 3, "Security", "Expired");
        await AssertProblemAsync(await owner.PostAsJsonAsync("/api/sales", payload, Ct),
            HttpStatusCode.Forbidden, "Licence.SecuritySuspended");
        await AssertProblemAsync(await owner.GetAsync(lookup, Ct),
            HttpStatusCode.Forbidden, "Licence.SecuritySuspended");

        await ApplyAsync(binding, end.AddDays(1), 4, state: "Expired", releasedRevision: 3);
        AssertReplay(original, await PostSaleAsync(owner, payload));
        await RevokeMembershipAsync(binding.Shop, ReadUserId(owner));
        using var revoked = await owner.PostAsJsonAsync("/api/sales", payload, Ct);
        Assert.Contains(revoked.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden });
        Assert.NotEqual(ReadUserId(owner), staffId);
        await AssertSaleCountAsync(binding.Shop, 1);
    }

    [Fact]
    public async Task Operating_licence_keeps_invoice_lease_role_requirement()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var owner = CreateClient(host);
        using var staff = CreateClient(host);
        var binding = await OpenTradingAsync(owner);
        await JoinAsStaffAsync(staff, binding.Shop);
        using var refused = await staff.PostAsJsonAsync("/api/sales/invoice-leases/reserve",
            new { deviceId = "staff-fixture", blockSize = 25 }, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        await AssertNoPostingAsync(binding.Shop);
        await PostSaleAsync(staff, SaleRequest(await SeedServiceAsync(owner)));
    }

    [Fact]
    public async Task Already_issued_offline_invoice_can_still_post_after_expiry()
    {
        using var host = CreateHost(runtimeIdentityOutsideRequests: false);
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var end = _clock.Now.AddMinutes(1);
        var binding = await OpenTradingAsync(client, end);
        var goods = await SeedGoodsAsync(client);
        await LegacyOfflineExpiryHistory.SeedAsync(host, client.DefaultRequestHeaders.Authorization!.Parameter!);
        const string device = "legacy-device";
        using var reserved = await client.PostAsJsonAsync("/api/sales/invoice-leases/reserve",
            new { deviceId = device, blockSize = 25 }, Ct);
        var lease = await ReadSuccessAsync(reserved);
        var counter = lease.GetProperty("rangeStart").GetInt32().ToString("D6", CultureInfo.InvariantCulture);
        var invoice = lease.GetProperty("prefix").GetString() + counter;
        var payload = OfflinePayload(device, invoice, goods, end.AddMicroseconds(-1));
        _clock.Now = end;

        using var sync = await client.PostAsJsonAsync("/api/sales/offline-sync", payload, Ct);
        var result = (await ReadSuccessAsync(sync)).GetProperty("results").EnumerateArray().Single();
        Assert.Equal("created", result.GetProperty("status").GetString());
        await using var db = Open(Migrator);
        var sale = await db.Sales.SingleAsync(x => x.ShopId == binding.Shop, Ct);
        Assert.Equal(invoice, sale.InvoiceNumber);
        Assert.Equal(SaleSource.Offline, sale.Source);
        Assert.Equal(sale.Id, (await db.OfflineSubmissions.SingleAsync(x => x.ShopId == binding.Shop, Ct)).SaleId);
    }

    private static object OfflinePayload(string device, string invoice, Line goods, DateTimeOffset soldAt) => new
    {
        deviceId = device,
        sales = new[] { new
        {
            clientSaleId = $"offline-{Guid.NewGuid():N}", invoiceNumber = invoice, soldAt,
            paymentMethod = (int)PaymentMethod.Cash, paidAmount = 2m, dueAmount = 0m,
            subtotalBeforeDiscount = 2m, totalBeforeDiscount = 2m, totalDiscountAmount = 0m,
            totalTaxAmount = 0m, totalAmount = 2m, saleDiscountOverrideType = 0, saleDiscountOverrideValue = 0m,
            items = new[] { new
            {
                goods.Barcode, goods.BatchNumber, goods.ItemName, goods.Quantity, goods.CostPrice, goods.SalesPrice,
                goods.Mrp, goods.TaxRatePercent, goods.IsPriceIncludingTax, goods.InventoryBatchId,
                preTaxAmountBeforeDiscount = 2m, itemDiscountAmount = 0m, saleDiscountAmount = 0m, taxableAmount = 2m,
                taxAmount = 0m, totalAmount = 2m, itemDiscountOverrideType = 0, itemDiscountOverrideValue = 0m,
            } },
        } },
    };

    private async Task<Guid> JoinAsStaffAsync(HttpClient client, Guid shop)
    {
        await RegisterAsync(client);
        var user = ReadUserId(client);
        await using var db = Open(Migrator);
        db.ShopMemberships.Add(ShopMembership.Create(shop, user, ShopRole.Staff, isDefault: false));
        await db.SaveChangesAsync(Ct);
        using var switched = await client.PostAsJsonAsync("/api/shops/switch", new { shopId = shop }, Ct);
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await ReadSuccessAsync(switched)).GetProperty("accessToken").GetString());
        return user;
    }

    private async Task RevokeMembershipAsync(Guid shop, Guid user)
    {
        await using var db = Open(Migrator);
        var membership = await db.ShopMemberships.SingleAsync(x => x.ShopId == shop && x.UserId == user, Ct);
        membership.ChangeAuthority(membership.Role, MembershipStatus.Removed);
        await db.SaveChangesAsync(Ct);
    }

    private static Guid ReadUserId(HttpClient client) =>
        Guid.Parse(new JwtSecurityToken(client.DefaultRequestHeaders.Authorization!.Parameter!).Subject);
}
