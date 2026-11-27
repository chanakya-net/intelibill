using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

public sealed partial class P0415LicensingTests
{
    [Fact]
    public async Task Keyed_payment_delivery_contention_is_uncertain_and_receipt_recovers_one_posting()
    {
        using var key = RSA.Create(2048);
        var pause = new PauseResult("RecordPayment");
        using var host = CreateHost(key, services => services.AddControllers(options => options.Filters.Add(pause)), productionLockBudgets: true);
        await host.InitializeAsync();
        using var merchant = CreateClient(host);
        using var delivery = CreateClient(host);
        var shop = await CreateShopAsync(merchant);
        var grant = await BindAsync(shop);
        await DeliverAsync(delivery, key, grant);
        await EnforceAsync(shop);

        using var added = await merchant.PostAsJsonAsync("/api/customers", new
        {
            name = "Payment fixture", phoneNumber = "+919812345678", isActive = true,
            creditLimit = 1_000_000m, creditPolicyMode = "Enforced",
        }, Ct);
        var customer = (await ReadSuccessAsync(added)).GetProperty("customerId").GetGuid();
        await CreateSaleAsync(merchant, customer);
        var paymentKey = $"p0415-handoff-{Guid.NewGuid():N}";
        var payload = new
        {
            amount = 1m, paymentDate = DateOnly.FromDateTime(_clock.Now.DateTime.AddHours(5.5)),
            operationKey = paymentKey, method = "Cash",
        };

        var path = $"/api/customers/{customer:D}/payments";
        var payment = merchant.PostAsJsonAsync(path, payload, Ct);
        await using (var db = OpenMigrator())
        await using (var transaction = await db.Database.BeginTransactionAsync(Ct))
        {
            try
            {
                await pause.Ready.Task.WaitAsync(TimeSpan.FromSeconds(15), Ct);
                Assert.Equal(1, await db.PaymentReceipts.CountAsync(row => row.ShopId == shop, Ct));
                await db.Database.ExecuteSqlAsync(
                    $"SELECT public.take_merchant_licence_fence({grant.AccountId}, {shop}, false)", Ct);
                pause.Resume.TrySetResult();
                using var response = await payment.WaitAsync(TimeSpan.FromSeconds(15), Ct);
                await AssertHandoffProblemAsync(response, HttpStatusCode.ServiceUnavailable, "Licence.DeliveryUnconfirmed");

                using var rejected = await merchant.PostAsJsonAsync(path, payload with { operationKey = paymentKey + "-new" }, Ct);
                await AssertHandoffProblemAsync(rejected, HttpStatusCode.Conflict, "Licence.Busy");
            }
            finally
            {
                pause.Resume.TrySetResult();
            }
        }

        using var recovery = await merchant.GetAsync($"/api/financial-operations/customer-payment/{paymentKey}", Ct);
        var recovered = await ReadSuccessAsync(recovery);
        Assert.Equal(paymentKey, recovered.GetProperty("operationKey").GetString());
        Assert.Equal(payload.amount, recovered.GetProperty("response").GetProperty("amount").GetDecimal());
        await using var verify = OpenMigrator();
        Assert.Equal(1, await verify.PaymentReceipts.CountAsync(row => row.ShopId == shop, Ct));
    }

    [Fact]
    public async Task Prepared_export_is_withheld_after_the_original_session_switches_shops()
    {
        using var key = RSA.Create(2048);
        var pause = new PauseResult("ExportSales");
        using var host = CreateHost(key, services => services.AddControllers(options => options.Filters.Add(pause)));
        await host.InitializeAsync();
        using var merchant = CreateClient(host);
        using var other = CreateClient(host);
        var shop = await CreateShopAsync(merchant);
        var otherShop = await CreateShopAsync(other);
        var otherSale = await CreateSaleAsync(other);
        await CreateSaleAsync(merchant);
        var oldToken = merchant.DefaultRequestHeaders.Authorization!.Parameter!;
        var user = Guid.Parse(new JwtSecurityToken(oldToken).Subject);

        await using (var db = OpenMigrator())
        {
            db.Add(ShopMembership.Create(otherShop, user, ShopRole.Owner, isDefault: false));
            await db.SaveChangesAsync(Ct);
        }

        var export = merchant.GetAsync("/api/exports/sales?format=xlsx", Ct);
        try
        {
            await pause.Ready.Task.WaitAsync(TimeSpan.FromSeconds(15), Ct);
            using var switched = await merchant.PostAsJsonAsync("/api/shops/switch", new { shopId = otherShop }, Ct);
            var token = (await ReadSuccessAsync(switched)).GetProperty("accessToken").GetString()!;
            Assert.NotEqual(shop, otherShop);
            merchant.DefaultRequestHeaders.Authorization = new("Bearer", token);

            using var history = await merchant.GetAsync($"/api/sales/{otherSale:D}", Ct);
            Assert.Equal(otherSale, (await ReadSuccessAsync(history)).GetProperty("saleId").GetGuid());
        }
        finally
        {
            pause.Resume.TrySetResult();
        }

        using var response = await export;
        Assert.Null(response.Content.Headers.ContentDisposition);
        await AssertHandoffProblemAsync(response, HttpStatusCode.Unauthorized, "Auth.SessionInvalid");
    }

    private static async Task AssertHandoffProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(code, problem.GetProperty("code").GetString());
    }
}
