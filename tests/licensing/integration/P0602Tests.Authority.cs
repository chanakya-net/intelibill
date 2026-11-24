using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0602Tests
{
    [Fact]
    public async Task SuspensionAfterCheckoutPreservesCapturedPaymentAndRestrictedShop()
    {
        await using var fixture = await StartAsync(database, January31);
        var (package, term, path, request) = await PreviewAsync(fixture);
        using var converted = await package.Payer.Client.PostAsJsonAsync(path, request);
        converted.EnsureSuccessStatusCode();
        var order = await AcceptAsync(package, (await converted.Content.ReadFromJsonAsync<CheckoutQuoteV1>())!);
        var attempt = await StartPaymentAsync(package, order);
        using var security = await P0417Tests.SignInAsync(fixture.Checkout.Delivery, "security-administrator");
        await P0404Tests.SuspendAsync(security, $"/api/admin/accounts/{package.Account:D}/restrictions",
            "Shop", package.Shops[1].ShopId, "Trading");
        await P0302Fixture.CaptureAsync(package.Payer.Client, order, attempt);
        var paid = await P0302Fixture.VerifyAsync(package.Payer.Client, order, attempt);
        P0302Fixture.AssertStatus(paid, "Verified", "NeedsReview", "NotApplicable");
        Assert.False(paid.GetProperty("canStart").GetBoolean());
        Assert.Equal(1L, await fixture.CountAsync("payment_confirmations", $"order_id='{order.OrderId}'"));
        Assert.Equal(0L, await fixture.CountAsync("capacity_increases", $"order_id='{order.OrderId}'"));
        Assert.Equal(1L, await fixture.CountAsync("active_operational_restrictions", $"shop_id='{package.Shops[1].ShopId}'"));
        Assert.Equal(February29, (await AccountTermAsync(package, term)).GetProperty("paidThrough").GetDateTimeOffset());
    }

    [Fact]
    public async Task MemberOnlyIncreaseSupersedesAnOlderAcceptedRenewal()
    {
        await using var fixture = await StartAsync(database, January31);
        var package = await fixture.PackageAsync(2, ShopNames);
        var (_, term) = await PurchaseAsync(package, "Month");
        fixture.At(February14);
        var earlier = await AcceptAsync(package, await RenewalQuoteAsync(package, term, "Month"));
        var increase = await IncreaseSeatsAsync(fixture, package, term, February29, capacity: 3);
        var delivery = fixture.Checkout.Delivery;
        await delivery.ScanAsync(true);
        await delivery.DrainAsync(true);
        await delivery.DrainAsync(false);
        await PayAsync(package, increase);
        for (var round = 0; round < 2; round++)
        {
            await delivery.DrainAsync(true);
            await delivery.DrainAsync(false);
        }
        var applied = await AccountTermAsync(package, term);
        Assert.Equal(3, applied.GetProperty("purchasedCapacity").GetInt32());
        Assert.Equal(6, applied.GetProperty("memberAllowance").GetInt32());
        Assert.Equal(February29, applied.GetProperty("paidThrough").GetDateTimeOffset());
        using var stale = await package.Payer.Client.PostAsJsonAsync(P0302Fixture.AttemptsPath(earlier), P0302Fixture.Start(earlier));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("Billing.CheckoutSourceChanged", await P0206Fixture.CodeAsync(stale));
    }

    [Fact]
    public async Task ManagerAndUnrelatedOwnerCannotReplayOrStartAnIncrease()
    {
        await using var fixture = await StartAsync(database, January31);
        var (package, _, path, request) = await PreviewAsync(fixture);
        using var created = await package.Payer.Client.PostAsJsonAsync(path, request);
        created.EnsureSuccessStatusCode();
        var order = await AcceptAsync(package, (await created.Content.ReadFromJsonAsync<CheckoutQuoteV1>())!);
        var manager = package.Shops[1];
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, $"""
            INSERT INTO billing_account_members(id,account_id,merchant_user_id,role,status,revision,created_at)
            VALUES(gen_random_uuid(),'{package.Account}','{manager.UserId}',2,1,1,now())
            """);
        var foreign = await P0209Fixture.SignInAsync(fixture.Hosts, Intelibill.Domain.Enums.ShopRole.Owner);
        foreach (var caller in new[] { manager, foreign })
        {
            using var conversion = await caller.Client.PostAsJsonAsync(path, request);
            Assert.Contains(conversion.StatusCode, new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound });
            using var start = await caller.Client.PostAsJsonAsync(P0302Fixture.AttemptsPath(order), P0302Fixture.Start(order));
            Assert.Equal(HttpStatusCode.Forbidden, start.StatusCode);
        }
        Assert.Equal(0L, await fixture.CountAsync("payment_attempts", $"order_id='{order.OrderId}'"));
    }

    [Fact]
    public async Task VerifiedManualReceiptAndLateSimulatorCaptureHaveOneCapacityDecision()
    {
        await using var fixture = await StartAsync(database, January31, P0306Tests.Settings);
        var (package, term, path, request) = await PreviewAsync(fixture);
        using var converted = await package.Payer.Client.PostAsJsonAsync(path, request);
        converted.EnsureSuccessStatusCode();
        var order = await AcceptAsync(package, (await converted.Content.ReadFromJsonAsync<CheckoutQuoteV1>())!);
        var attempt = await StartPaymentAsync(package, order);
        var delivery = fixture.Checkout.Delivery;
        await delivery.ScanAsync(true);
        await delivery.DrainAsync(true);
        await delivery.DrainAsync(false);
        using var billing = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        var manual = P0306Tests.Receipt(order, February14);
        using var receipt = await P0212Fixture.PostAsync(billing, P0306Tests.Path(order), manual);
        Assert.True(receipt.IsSuccessStatusCode, await receipt.Content.ReadAsStringAsync());
        Assert.Equal("Pending", (await receipt.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("fulfilment").GetString());
        using var replay = await P0212Fixture.PostAsync(billing, P0306Tests.Path(order), manual);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        await P0302Fixture.CaptureAsync(package.Payer.Client, order, attempt);
        await P0302Fixture.VerifyAsync(package.Payer.Client, order, attempt);
        for (var round = 0; round < 2; round++)
        {
            await delivery.DrainAsync(true);
            await delivery.DrainAsync(false);
        }
        Assert.Equal(1L, await fixture.CountAsync("capacity_increases", $"order_id='{order.OrderId}' AND receipt_id IS NOT NULL AND state='Applied'"));
        Assert.Equal(1L, await fixture.CountAsync("unmatched_payments", $"order_id='{order.OrderId}' AND classification='DuplicateFunds'"));
        Assert.Equal(2L, await fixture.CountAsync("payment_confirmations", $"order_id='{order.OrderId}'"));
        Assert.Equal(February29, (await AccountTermAsync(package, term)).GetProperty("paidThrough").GetDateTimeOffset());
    }
}
