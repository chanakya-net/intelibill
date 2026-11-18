using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

public sealed partial class P0405LicensingTests
{
    [Fact]
    public async Task Decision_uses_verified_boundaries_and_distinct_denials()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var end = _clock.Now.AddMinutes(1);
        var binding = await OpenTradingAsync(client, end);
        var service = await SeedServiceAsync(client);

        using var outageClient = CreateClient(host);
        var outageShop = await CreateShopAsync(outageClient);
        var verified = end.AddHours(-48).AddMinutes(1);
        await ApplyAsync(await BindAsync(outageShop), end.AddDays(1), verified: verified);
        await EnforceAsync(outageShop);
        var outageService = await SeedServiceAsync(outageClient);

        _clock.Now = end.AddMicroseconds(-1);
        await PostSaleAsync(client, SaleRequest(service));
        _clock.Now = end;
        await AssertSaleDeniedAsync(client, service, HttpStatusCode.Forbidden, "Licence.Expired");

        await ApplyAsync(binding, end.AddDays(1), 2, state: "PendingActivation");
        await AssertSaleDeniedAsync(client, service, HttpStatusCode.Forbidden, "Licence.PendingActivation");

        _clock.Now = verified.AddHours(48);
        await AssertSaleDeniedAsync(outageClient, outageService, HttpStatusCode.ServiceUnavailable, "Licence.VerificationUnavailable");
        await AssertSaleCountAsync(binding.Shop, 1);
        await AssertNoPostingAsync(outageShop);
    }

    [Fact]
    public async Task Uncommitted_key_cannot_authorize_a_sale_after_restriction()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var end = _clock.Now.AddMinutes(1);
        var binding = await OpenTradingAsync(client, end);
        var goods = await SeedGoodsAsync(client);
        var request = SaleRequest(goods with { Quantity = 11m });
        await AssertProblemAsync(await client.PostAsJsonAsync("/api/sales", request, Ct),
            HttpStatusCode.BadRequest, "Sale.InsufficientStock");
        await AssertNoPostingAsync(binding.Shop, goods.InventoryBatchId);

        _clock.Now = end;
        await AssertProblemAsync(await client.PostAsJsonAsync("/api/sales", request, Ct),
            HttpStatusCode.Forbidden, "Licence.Expired");
        await AssertNoPostingAsync(binding.Shop, goods.InventoryBatchId);

        await ApplyAsync(binding, end.AddDays(1), 2);
        await AssertProblemAsync(await client.PostAsJsonAsync("/api/sales", request, Ct),
            HttpStatusCode.BadRequest, "Sale.InsufficientStock");
        await AssertNoPostingAsync(binding.Shop, goods.InventoryBatchId);
    }

    [Fact]
    public async Task Operating_licence_preserves_the_tax_profile_gate()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client, hasTaxProfile: false);
        await ApplyAsync(await BindAsync(shop), _clock.Now.AddDays(1));
        await EnforceAsync(shop);
        await AssertSaleDeniedAsync(client, await SeedServiceAsync(client),
            HttpStatusCode.Conflict, "Invoice.TaxProfileRequired");
        await AssertNoPostingAsync(shop);
    }

    [Fact]
    public async Task Operating_licence_keeps_customer_credit_and_invoice_tax_checks()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var binding = await OpenTradingAsync(client);
        var service = await SeedServiceAsync(client);
        using var customer = await client.PostAsJsonAsync("/api/customers",
            new { name = "Fixture customer", phoneNumber = "+919111111111", isActive = true, creditPolicyMode = "NoCredit" }, Ct);
        var customerId = (await ReadSuccessAsync(customer)).GetProperty("customerId").GetGuid();
        var due = SaleRequest(service) with { PaymentMethod = "Credit", PaidAmount = 0m, DueAmount = 3m, CustomerId = customerId };
        await AssertProblemAsync(await client.PostAsJsonAsync("/api/sales", due, Ct),
            HttpStatusCode.Conflict, "Customer.CreditNotExtended");

        // This document refusal happens after allocation. The sequence write must roll back.
        var taxable = SaleRequest(service with { TaxRatePercent = 18m }) with { PaidAmount = 3.54m };
        await AssertProblemAsync(await client.PostAsJsonAsync("/api/sales", taxable, Ct),
            HttpStatusCode.BadRequest, "Invoice.UnregisteredCannotCollectTax");
        await AssertNoPostingAsync(binding.Shop);

        await ApplyAsync(binding, _clock.Now.AddDays(2), 2);
        Assert.EndsWith("/000001", (await PostSaleAsync(client, SaleRequest(service))).GetProperty("invoiceNumber").GetString());
    }

    [Fact]
    public async Task Invoice_lease_reservation_follows_the_trading_decision()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var end = _clock.Now.AddMinutes(1);
        var binding = await OpenTradingAsync(client, end);
        const string path = "/api/sales/invoice-leases/reserve";
        using var reserved = await client.PostAsJsonAsync(path, new { deviceId = "fixture-1", blockSize = 25 }, Ct);
        await ReadSuccessAsync(reserved);
        _clock.Now = end;

        await AssertProblemAsync(await client.PostAsJsonAsync(path, new { deviceId = "fixture-2", blockSize = 25 }, Ct),
            HttpStatusCode.Forbidden, "Licence.Expired");
        await using var db = Open(Migrator);
        Assert.Equal(1, await db.InvoiceLeases.CountAsync(x => x.ShopId == binding.Shop, Ct));
        Assert.Equal(26, await db.InvoiceSequences.Where(x => x.ShopId == binding.Shop).Select(x => x.NextNumber).SingleAsync(Ct));
    }

    [Fact]
    public async Task Trading_authority_denial_reports_enforcement()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var end = _clock.Now.AddMinutes(1);
        var binding = await OpenTradingAsync(client, end);
        _clock.Now = end;
        using var response = await client.GetAsync($"/api/licensing/shops/{binding.Shop:D}/trading-authority", Ct);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("Licence.Expired", body.GetProperty("code").GetString());
        Assert.True(body.TryGetProperty("enforced", out var enforced) && enforced.GetBoolean());
    }

    private static async Task AssertSaleDeniedAsync(HttpClient client, Line service, HttpStatusCode status, string code) =>
        await AssertProblemAsync(await client.PostAsJsonAsync("/api/sales", SaleRequest(service), Ct), status, code);
}
