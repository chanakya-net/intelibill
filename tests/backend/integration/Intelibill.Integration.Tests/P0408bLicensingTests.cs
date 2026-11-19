using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

public sealed partial class P0408bLicensingTests
{
    [Fact]
    public async Task Customer_creation_uses_current_capacity()
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
        using var allowed = await CreateCustomerAsync(client, "Existing");
        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);

        _clock.Now = end;
        using var expired = await CreateCustomerAsync(client, "Expired");
        await ApplyAsync(binding, end.AddDays(1), revision: 2, state: "PendingActivation");
        using var pending = await CreateCustomerAsync(client, "Pending");
        _output.WriteLine("Customer POST status at expiry: {0}; pending: {1}", (int)expired.StatusCode, (int)pending.StatusCode);
        await AssertProblemAsync(expired, HttpStatusCode.Forbidden, "Licence.Expired");
        await AssertProblemAsync(pending, HttpStatusCode.Forbidden, "Licence.PendingActivation");

        await using var db = Open(Migrator);
        var names = await db.Customers.Where(x => x.ShopId == shop).Select(x => x.Name).ToArrayAsync(Ct);
        Assert.Equal("Existing", Assert.Single(names));
        _output.WriteLine("PostgreSQL customer rows after expiry and pending refusals: {0}", names.Length);
    }

    [Theory]
    [InlineData("Trading", HttpStatusCode.Forbidden, "Licence.TradingRestricted")]
    [InlineData("Security", HttpStatusCode.Forbidden, "Licence.SecuritySuspended")]
    [InlineData("Stale", HttpStatusCode.ServiceUnavailable, "Licence.VerificationUnavailable")]
    public async Task Enrolled_restrictions_refuse_new_customers(string state, HttpStatusCode status, string code)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        var binding = await BindAsync(shop);
        var verified = _clock.Now.AddHours(-48).AddMinutes(1);
        await ApplyAsync(binding, _clock.Now.AddDays(3),
            restriction: state == "Stale" ? "Unrestricted" : state, verified: state == "Stale" ? verified : null);
        await EnforceAsync(shop);

        if (state == "Stale")
            _clock.Now = verified.AddHours(48);

        await AssertProblemAsync(await CreateCustomerAsync(client, "Refused"), status, code);
        await AssertCustomerCountAsync(shop, 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Shops_outside_enforcement_can_still_create_customers(bool projected)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        if (projected)
            await ApplyAsync(await BindAsync(shop), _clock.Now.AddDays(1), state: "PendingActivation");

        using var response = await CreateCustomerAsync(client, "Observe only");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await AssertCustomerCountAsync(shop, 1);
    }

    private static Task<HttpResponseMessage> CreateCustomerAsync(HttpClient client, string name, decimal creditLimit = 0m) =>
        client.PostAsJsonAsync("/api/customers", new
        {
            name, phoneNumber = "+919111111111", isActive = true, creditLimit,
            creditPolicyMode = creditLimit > 0 ? "Enforced" : null,
        }, Ct);
}
