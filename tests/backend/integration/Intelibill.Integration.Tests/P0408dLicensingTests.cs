using System.Net;
using System.Net.Http.Json;
using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

public sealed partial class P0408dLicensingTests
{
    [Fact]
    public async Task RecoveryAllowsIdentityAndContactOnly()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        var end = _clock.Now.AddMinutes(1);
        await ApplyAsync(await BindAsync(shop), end);
        await EnforceAsync(shop);
        _clock.Now = end;

        var path = $"/api/shops/{shop}";
        using var recovery = await client.PutAsJsonAsync(path, Profile(" Contact repaired ", " 42 MG Road "), Ct);
        Assert.Equal("Contact repaired", (await ReadSuccessAsync(recovery)).GetProperty("name").GetString());

        using var mixed = await client.PutAsJsonAsync(path, Profile("Do not apply", "Different location"), Ct);
        await using var db = Open(Migrator);
        var stored = await db.Shops.SingleAsync(x => x.Id == shop, Ct);
        _output.WriteLine("Mixed profile PUT: {0}; stored name/address: {1}/{2}",
            (int)mixed.StatusCode, stored.Name, stored.Address);
        await AssertProblemAsync(mixed, HttpStatusCode.Forbidden, "Licence.Expired");
        Assert.Equal("Contact repaired", stored.Name);
        Assert.Equal("42 MG Road", stored.Address);
    }

    [Fact]
    public async Task PendingOwnerPreparesTaxWithoutTradingOrCoverage()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        using var service = await client.PostAsJsonAsync("/api/services", new
            { name = "Repair", price = 3m, taxRatePercent = 0m, taxIncluded = false, isActive = true }, Ct);
        var serviceId = (await ReadSuccessAsync(service)).GetProperty("serviceId").GetGuid();
        var binding = await BindAsync(shop);
        var end = _clock.Now.AddDays(1);
        await ApplyAsync(binding, end, state: "PendingActivation");
        await EnforceAsync(shop);

        var path = $"/api/shops/{shop}";
        using var profile = await client.PutAsJsonAsync(path, Profile("Pending prepared", "Updated location"), Ct);
        Assert.Equal("Updated location", (await ReadSuccessAsync(profile)).GetProperty("address").GetString());
        using var tax = await client.PutAsJsonAsync($"{path}/tax-profile", Tax("B"), Ct);
        Assert.Equal("B", (await ReadSuccessAsync(tax)).GetProperty("taxProfile").GetProperty("invoiceSeriesCode").GetString());
        using var billing = await client.PostAsJsonAsync("/api/billing/accounts",
            new { operationKey = Guid.NewGuid(), name = "Fixture billing account" }, Ct);
        Assert.Equal(HttpStatusCode.Created, billing.StatusCode);
        Assert.Equal(shop, _billing.Caller?.Session.ActiveShopId);
        await AssertProblemAsync(await client.PostAsJsonAsync("/api/sales", Sale(serviceId), Ct),
            HttpStatusCode.Forbidden, "Licence.PendingActivation");

        await using var db = Open(Migrator);
        var projection = await db.Set<ShopLicenseState>().SingleAsync(x => x.ShopId == shop, Ct);
        Assert.Equal(1, projection.Revision);
        Assert.Equal("PendingActivation", projection.State);
        Assert.Equal(binding.Term, projection.TermId);
        Assert.Equal(binding.Account, projection.AccountId);
        Assert.Equal(end, projection.AccessEndsAt);
        Assert.Equal(2, projection.ShopCapacity);
        Assert.Equal(3, projection.MemberAllowance);
        Assert.Equal(2, await db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM shop_affiliation_consent_events WHERE shop_id = {shop}
            """).SingleAsync(Ct));
        Assert.False(await db.Sales.AnyAsync(x => x.ShopId == shop, Ct));
        Assert.False(await db.InvoiceSequences.AnyAsync(x => x.ShopId == shop, Ct));

        await ApplyAsync(binding, end, 2, restriction: "Trading");
        using var restricted = await client.PutAsJsonAsync($"{path}/tax-profile", Tax("C"), Ct);
        var series = await db.Shops.Where(x => x.Id == shop).Select(x => x.InvoiceSeriesCode).SingleAsync(Ct);
        _output.WriteLine("Trading-restricted tax PUT: {0}; stored series: {1}", (int)restricted.StatusCode, series);
        await AssertProblemAsync(restricted, HttpStatusCode.Forbidden, "Licence.TradingRestricted");
        Assert.Equal("B", series);
    }

    [Fact]
    public async Task PaymentDestinationRequiresOperatingAccessOnBothRoutes()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        await ApplyAsync(await BindAsync(shop), _clock.Now.AddDays(1), state: "PendingActivation");
        await EnforceAsync(shop);
        var input = Bank("100000000001");
        using var setup = await client.PutAsJsonAsync($"/api/shops/{shop}/bank-details", input, Ct);
        using var generic = await client.PostAsJsonAsync("/api/bank-accounts", input, Ct);
        await using var db = Open(Migrator);
        var count = await db.BankAccounts.CountAsync(x => x.ShopId == shop, Ct);
        _output.WriteLine("Bank setup PUT: {0}; generic POST: {1}; stored accounts: {2}",
            (int)setup.StatusCode, (int)generic.StatusCode, count);
        await AssertProblemAsync(setup, HttpStatusCode.Forbidden, "Licence.PendingActivation");
        await AssertProblemAsync(generic, HttpStatusCode.Forbidden, "Licence.PendingActivation");
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task CreateDistinguishesFirstShopFromExpansion()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        await ApplyAsync(await BindAsync(shop), _clock.Now.AddDays(1), state: "PendingActivation");
        await EnforceAsync(shop);
        using var additional = await client.PostAsJsonAsync("/api/shops", Profile("Additional shop", "Second location"), Ct);
        await using var db = Open(Migrator);
        var owner = await db.ShopMemberships.Where(x => x.ShopId == shop).Select(x => x.UserId).SingleAsync(Ct);
        var memberships = await db.ShopMemberships.CountAsync(x => x.UserId == owner, Ct);
        _output.WriteLine("Additional POST: {0}; stored memberships: {1}", (int)additional.StatusCode, memberships);
        await AssertProblemAsync(additional, HttpStatusCode.Forbidden, "Licence.PendingActivation");
        Assert.Equal(1, memberships);
    }

    [Fact]
    public async Task SetupPreferencesAndOperatingSettingsUseTheirOwnMatrixRows()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        var binding = await BindAsync(shop);
        var end = _clock.Now.AddDays(1);
        await ApplyAsync(binding, end, state: "PendingActivation");
        await EnforceAsync(shop);

        var path = $"/api/shops/{shop}/setup/preferences";
        using var preparation = await client.PutAsJsonAsync(path, new { expectedVersion = 0, showOnStartup = false }, Ct);
        var preferences = await ReadSuccessAsync(preparation);
        Assert.False(preferences.GetProperty("showOnStartup").GetBoolean());
        var version = preferences.GetProperty("version").GetInt32();
        await ApplyAsync(binding, end, 2, restriction: "Trading");

        using var startup = await client.PutAsJsonAsync(path, new { expectedVersion = version, showOnStartup = true }, Ct);
        using var till = await client.PutAsJsonAsync($"/api/shops/{shop}/till-settings", new
            { enabled = true, expectedVersion = 0, reason = "Fixture", confirmed = true }, Ct);
        using var expiry = await client.PutAsJsonAsync($"/api/expiry-policies/items/{Guid.NewGuid()}", new
            { expectedCatalogVersion = 1, reason = "Fixture" }, Ct);
        await AssertProblemAsync(startup, HttpStatusCode.Forbidden, "Licence.TradingRestricted");
        await AssertProblemAsync(till, HttpStatusCode.Forbidden, "Licence.TradingRestricted");
        await AssertProblemAsync(expiry, HttpStatusCode.Forbidden, "Licence.TradingRestricted");

        using var read = await client.GetAsync(path, Ct);
        Assert.Equal(version, (await ReadSuccessAsync(read)).GetProperty("version").GetInt32());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecoveryComparesTheEffectiveGstin(bool declared)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        const string gstin = "27AAPFU0939F1ZV";
        var shop = await CreateShopAsync(client, legacyGstin: gstin);
        var path = $"/api/shops/{shop}";
        if (declared)
        {
            using var tax = await client.PutAsJsonAsync($"{path}/tax-profile", new
                { stateCode = "27", taxRegime = "Regular", gstNumber = gstin, eInvoiceStatus = "BelowThreshold" }, Ct);
            await ReadSuccessAsync(tax);
        }

        var end = _clock.Now.AddMinutes(1);
        await ApplyAsync(await BindAsync(shop), end);
        await EnforceAsync(shop);
        _clock.Now = end;
        using var omitted = await client.PutAsJsonAsync(path, Profile("Contact update", "42 MG Road"), Ct);
        if (declared)
            await ReadSuccessAsync(omitted);
        else
            await AssertProblemAsync(omitted, HttpStatusCode.Forbidden, "Licence.Expired");

        await using var db = Open(Migrator);
        Assert.Equal(gstin, await db.Shops.Where(x => x.Id == shop).Select(x => x.GstNumber).SingleAsync(Ct));
    }

    private static object Profile(string name, string address) => new
    {
        name, address, city = "Bengaluru", state = "Karnataka", pincode = "560001",
        contactPerson = "Owner", mobileNumber = "+919111111111",
    };

    private static object Tax(string invoiceSeriesCode) => new
        { stateCode = "29", taxRegime = "Unregistered", legalName = "Fixture Owner", invoiceSeriesCode };

    private static object Sale(Guid serviceId) => new
    {
        idempotencyKey = $"setup-{Guid.NewGuid():N}", paymentMethod = "Cash", paidAmount = 3m, dueAmount = 0m,
        items = new[] { new { barcode = "SERVICE", batchNumber = "", itemName = "Repair", costPrice = 0m,
            salesPrice = 3m, mrp = 0m, inventoryBatchId = Guid.Empty, lineType = "Service", serviceId,
            quantity = 1m, taxRatePercent = 0m, isPriceIncludingTax = false } },
    };
}
