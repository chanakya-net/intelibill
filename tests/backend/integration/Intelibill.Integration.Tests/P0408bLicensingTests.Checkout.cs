using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Intelibill.Application.Features.Customers.Commands.AddCustomer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

public sealed partial class P0408bLicensingTests
{
    [Fact]
    public async Task Checkout_cannot_create_customer_capacity()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var binding = await OpenTradingAsync(client);
        var service = await SeedServiceAsync(client);

        var sale = await PostSaleAsync(client, service);
        Assert.Equal("Walk-in", sale.GetProperty("customerName").GetString());
        await AssertCustomerCountAsync(binding.Shop, 0);
        await AssertProblemAsync(await client.PostAsJsonAsync("/api/sales", SaleRequest(service, due: true), Ct),
            HttpStatusCode.NotFound, "Sale.CreditCustomerNotFound");

        await ApplyAsync(binding, _clock.Now.AddDays(1), 2, restriction: "Trading");
        foreach (var due in new[] { false, true })
            await AssertProblemAsync(await client.PostAsJsonAsync("/api/sales", SaleRequest(service, due), Ct),
                HttpStatusCode.Forbidden, "Licence.TradingRestricted");
        await AssertCustomerCountAsync(binding.Shop, 0);
    }

    [Fact]
    public async Task Direct_customer_handler_cannot_bypass_the_source_fence()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var binding = await OpenTradingAsync(client);
        await ApplyAsync(binding, _clock.Now.AddDays(1), 2, restriction: "Trading");
        var session = ReadSession(client);
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var accessor = services.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext { User = ReadPrincipal(client) };
        try
        {
            var handler = ActivatorUtilities.CreateInstance<AddCustomerCommandHandler>(services);
            var result = await handler.HandleAsync(
                new(session, "Direct", "+919111111111", null, true, 0m), Ct);
            Assert.True(result.IsError, "Direct handler created a customer while trading was restricted.");
            Assert.Equal("Licence.TradingRestricted", result.FirstError.Code);
        }
        finally
        {
            accessor.HttpContext = null;
        }

        await AssertCustomerCountAsync(binding.Shop, 0);
    }

    [Theory]
    [InlineData(false, "Licence.Expired")]
    [InlineData(true, "Licence.TradingRestricted")]
    public async Task Prior_history_and_existing_dues_stay_available_after_restriction(bool suspended, string code)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var owner = CreateClient(host);
        using var staff = CreateClient(host);
        var shop = await CreateShopAsync(owner);
        var binding = await BindAsync(shop);
        var end = _clock.Now.AddMinutes(1);
        await ApplyAsync(binding, end);
        await EnforceAsync(shop);
        await SignInStaffAsync(owner, staff, shop);

        using var created = await CreateCustomerAsync(owner, "Existing", 10m);
        var customer = (await ReadSuccessAsync(created)).GetProperty("customerId").GetGuid();
        var service = await SeedServiceAsync(owner);
        var sale = await PostSaleAsync(owner, service, customer);
        var saleId = sale.GetProperty("saleId").GetGuid();

        if (suspended)
            await ApplyAsync(binding, end, 2, restriction: "Trading");
        else
            _clock.Now = end;

        var path = $"/api/customers/{customer}";
        await AssertHistoryAndSettlementAsync(owner, staff, path, saleId);
        await AssertCreditAuthorityAsync(owner, staff, path, suspended);

        await AssertProblemAsync(await CreateCustomerAsync(staff, "New"), HttpStatusCode.Forbidden, code);
        await AssertProblemAsync(await CreateCustomerAsync(staff, "Staff credit", 10m),
            HttpStatusCode.Forbidden, "Customer.CreditSettingsChangeNotPermitted");
        await AssertProblemAsync(await owner.PostAsJsonAsync("/api/sales", SaleRequest(service, true, customer), Ct),
            HttpStatusCode.Forbidden, code);
        await AssertProblemAsync(await CreateCustomerAsync(owner, "New"), HttpStatusCode.Forbidden, code);
        await AssertCustomerCountAsync(shop, 1);

        await using var db = Open(Migrator);
        Assert.Equal(2, await db.CustomerLedgerEntries.CountAsync(x => x.ShopId == shop, Ct));
        Assert.Single(await db.PaymentReceipts.Where(x => x.ShopId == shop).ToListAsync(Ct));
        _output.WriteLine("Existing due: 3.00; one posted and replayed payment: 2.00; closing balance: 1.00.");
    }

    private async Task AssertHistoryAndSettlementAsync(HttpClient owner, HttpClient staff, string path, Guid saleId)
    {
        foreach (var route in new[] { "/api/customers", "/api/customers/directory", path })
        {
            using var response = await owner.GetAsync(route, Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using var credit = await staff.GetAsync($"{path}/credit", Ct);
        Assert.Equal(3m, (await ReadSuccessAsync(credit)).GetProperty("currentExposure").GetDecimal());
        using var before = await owner.GetAsync($"{path}/account", Ct);
        Assert.Equal(3m, (await ReadSuccessAsync(before)).GetProperty("closingBalance").GetDecimal());

        var payment = new
        {
            amount = 2m, paymentDate = DateOnly.FromDateTime(_clock.Now.ToOffset(TimeSpan.FromHours(5.5)).DateTime),
            method = "Cash", operationKey = $"payment-{Guid.NewGuid():N}",
            allocations = new[] { new { targetId = saleId, amount = 2m } },
        };
        using var staffPayment = await staff.PostAsJsonAsync($"{path}/payments", payment, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, staffPayment.StatusCode);
        using var staffAccount = await staff.GetAsync($"{path}/account", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, staffAccount.StatusCode);

        using var paid = await owner.PostAsJsonAsync($"{path}/payments", payment, Ct);
        var receipt = await ReadSuccessAsync(paid);
        using var replay = await owner.PostAsJsonAsync($"{path}/payments", payment, Ct);
        Assert.Equal(receipt.GetRawText(), (await ReadSuccessAsync(replay)).GetRawText());
        using var after = await owner.GetAsync($"{path}/account", Ct);
        Assert.Equal(1m, (await ReadSuccessAsync(after)).GetProperty("closingBalance").GetDecimal());
        using var detail = await owner.GetAsync($"/api/payments/{receipt.GetProperty("receiptId").GetGuid()}", Ct);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
    }

    private static async Task AssertCreditAuthorityAsync(HttpClient owner, HttpClient staff, string path, bool suspended)
    {
        var contact = new { name = "Existing", phoneNumber = "+919111111111", address = "Updated address", isActive = true, creditLimit = 10m };
        var increase = new { contact.name, contact.phoneNumber, contact.address, contact.isActive, creditLimit = 20m };
        await AssertProblemAsync(await staff.PutAsJsonAsync(path, increase, Ct), HttpStatusCode.Forbidden,
            suspended ? "Licence.TradingRestricted" : "Customer.CreditSettingsChangeNotPermitted");
        if (suspended)
        {
            await AssertProblemAsync(await owner.PutAsJsonAsync(path, contact, Ct), HttpStatusCode.Forbidden,
                "Licence.TradingRestricted");
            return;
        }

        using var edited = await owner.PutAsJsonAsync(path, contact, Ct);
        Assert.Equal(contact.address, (await ReadSuccessAsync(edited)).GetProperty("address").GetString());
    }

    private static async Task SignInStaffAsync(HttpClient owner, HttpClient staff, Guid shop)
    {
        var email = $"p0408b-staff-{Guid.NewGuid():N}@fixture.test";
        const string password = "Pass123!Aa";
        using var seed = new HttpRequestMessage(HttpMethod.Post, "/api/users");
        seed.Headers.Authorization = owner.DefaultRequestHeaders.Authorization;
        seed.Content = JsonContent.Create(new
        {
            shopIds = new[] { shop }, email, password, firstName = "Test", lastName = "Staff",
            confirmPassword = password, phoneNumber = "+919876543211", role = "Staff",
        });
        using var seeded = await owner.SeedVerifiedStaffAsync(seed);
        seeded.EnsureSuccessStatusCode();
        using var login = await staff.PostAsJsonAsync("/api/auth/login/email", new { email, password }, Ct);
        staff.DefaultRequestHeaders.Authorization = new("Bearer", (await ReadSuccessAsync(login)).GetProperty("accessToken").GetString());
    }

    private static async Task<Guid> SeedServiceAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/services",
            new { name = "Repair", price = 3m, taxRatePercent = 0m, taxIncluded = false, isActive = true }, Ct);
        return (await ReadSuccessAsync(response)).GetProperty("serviceId").GetGuid();
    }

    private static async Task<JsonElement> PostSaleAsync(HttpClient client, Guid service, Guid? customer = null)
    {
        using var response = await client.PostAsJsonAsync("/api/sales", SaleRequest(service, customer.HasValue, customer), Ct);
        var sale = await ReadSuccessAsync(response);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return sale;
    }

    private static object SaleRequest(Guid service, bool due = false, Guid? customerId = null) => new
    {
        idempotencyKey = $"sale-{Guid.NewGuid():N}", paymentMethod = due ? "Credit" : "Cash",
        paidAmount = due ? 0m : 3m, dueAmount = due ? 3m : 0m,
        customerId, customerName = customerId is null ? "Walk-in" : "Existing",
        customerPhone = customerId is null ? "+919111111112" : "+919111111111",
        items = new[] { new
        {
            barcode = "SERVICE", batchNumber = "", itemName = "Repair", lineType = "Service",
            serviceId = service, inventoryBatchId = Guid.Empty, quantity = 1m, salesPrice = 3m,
        } },
    };
}
