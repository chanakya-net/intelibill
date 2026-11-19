using Intelibill.Application.Features.Suppliers.Commands.AddSupplier;
using Intelibill.Application.Features.Suppliers.Commands.EditSupplier;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

public sealed partial class P0408cLicensingTests
{
    [Fact]
    public async Task Supplier_creation_uses_current_operating_access()
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
        using var allowed = await CreateSupplierAsync(client, "Existing");
        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);

        _clock.Now = end;
        using var expired = await CreateSupplierAsync(client, "Expired");
        await ApplyAsync(binding, end.AddDays(1), revision: 2, state: "PendingActivation");
        using var pending = await CreateSupplierAsync(client, "Pending", isActive: false);
        var names = await SupplierNamesAsync(shop);
        _output.WriteLine("POST at expiry: {0}; pending: {1}. Ordinary PostgreSQL rows: {2}",
            (int)expired.StatusCode, (int)pending.StatusCode, string.Join(", ", names));
        await AssertProblemAsync(expired, HttpStatusCode.Forbidden, "Licence.Expired");
        await AssertProblemAsync(pending, HttpStatusCode.Forbidden, "Licence.PendingActivation");
        Assert.Equal("Existing", Assert.Single(names));
    }

    [Fact]
    public async Task Restricted_edit_cannot_restore_supplier_capacity()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        var binding = await BindAsync(shop);
        var end = _clock.Now.AddMinutes(1);
        await ApplyAsync(binding, end);
        await EnforceAsync(shop);
        using var created = await CreateSupplierAsync(client, "Inactive", isActive: false);
        var supplierId = (await ReadSuccessAsync(created)).GetProperty("supplierId").GetGuid();
        var before = await SupplierRowsAsync(shop);
        _clock.Now = end;

        using var response = await client.PutAsJsonAsync($"/api/suppliers/{supplierId}",
            SupplierInput("Changed and reactivated", true), Ct);
        var names = await SupplierNamesAsync(shop);
        _output.WriteLine("PUT reactivation at expiry: {0}. Ordinary PostgreSQL rows: {1}",
            (int)response.StatusCode, string.Join(", ", names));
        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "Licence.Expired");
        Assert.Equal("Inactive", Assert.Single(names));
        Assert.Equal(before, await SupplierRowsAsync(shop));

        var path = $"/api/suppliers/{supplierId}";
        using var contactEdit = await client.PutAsJsonAsync(path, SupplierInput("Updated contact", false), Ct);
        Assert.False((await ReadSuccessAsync(contactEdit)).GetProperty("isActive").GetBoolean());

        await ApplyAsync(binding, end.AddDays(1), 2, state: "PendingActivation");
        await AssertProblemAsync(await client.PutAsJsonAsync(path, SupplierInput("Pending edit", true), Ct),
            HttpStatusCode.Forbidden, "Licence.PendingActivation");

        await ApplyAsync(binding, end.AddDays(1), 3);
        using var reactivated = await client.PutAsJsonAsync(path, SupplierInput("Reactivated", true), Ct);
        Assert.True((await ReadSuccessAsync(reactivated)).GetProperty("isActive").GetBoolean());
        Assert.Equal("Reactivated", Assert.Single(await SupplierNamesAsync(shop)));
    }

    [Theory]
    [InlineData("Trading", HttpStatusCode.Forbidden, "Licence.TradingRestricted")]
    [InlineData("Security", HttpStatusCode.Forbidden, "Licence.SecuritySuspended")]
    [InlineData("Stale", HttpStatusCode.ServiceUnavailable, "Licence.VerificationUnavailable")]
    public async Task Enrolled_restrictions_refuse_new_suppliers(string phase, HttpStatusCode status, string code)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        var binding = await BindAsync(shop);
        var stale = phase == "Stale";
        var verified = _clock.Now.AddHours(-48).AddMinutes(1);
        await ApplyAsync(binding, _clock.Now.AddDays(3), restriction: stale ? "Unrestricted" : phase,
            verified: stale ? verified : null);
        await EnforceAsync(shop);

        if (stale)
            _clock.Now = verified.AddHours(48);

        await AssertProblemAsync(await CreateSupplierAsync(client, "Refused"), status, code);
        await AssertSupplierCountAsync(shop, 0);
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("PendingActivation", "Paid", false)]
    [InlineData("Active", "Trial", true)]
    [InlineData("Grace", "Paid", true)]
    public async Task Compatible_shops_can_create_suppliers(string? state, string? basis, bool enrolled)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        if (state is not null)
            await ApplyAsync(await BindAsync(shop), _clock.Now.AddDays(1), state: state, basis: basis!);
        if (enrolled)
            await EnforceAsync(shop);

        using var created = await CreateSupplierAsync(client, "Allowed", isActive: false);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("Allowed", Assert.Single(await SupplierNamesAsync(shop)));
    }

    [Fact]
    public async Task Direct_supplier_handlers_require_current_access_and_explicit_release()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var binding = await OpenTradingAsync(client);
        using var created = await CreateSupplierAsync(client, "Inactive", isActive: false);
        var supplierId = (await ReadSuccessAsync(created)).GetProperty("supplierId").GetGuid();
        var end = _clock.Now.AddDays(1);
        await ApplyAsync(binding, end, 2, restriction: "Trading");

        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var accessor = services.GetRequiredService<IHttpContextAccessor>();
        var session = ReadSession(client);
        accessor.HttpContext = new DefaultHttpContext { User = ReadPrincipal(client) };
        try
        {
            var create = ActivatorUtilities.CreateInstance<AddSupplierCommandHandler>(services);
            var edit = ActivatorUtilities.CreateInstance<EditSupplierCommandHandler>(services);
            var added = await create.HandleAsync(new(session, "Direct", null, null, "Road", "City", "State",
                "560001", true, false), Ct);
            var edited = await edit.HandleAsync(new(session, supplierId, "Direct", null, null, "Road", "City",
                "State", "560001", false, false), Ct);
            Assert.Equal("Licence.TradingRestricted", added.FirstError.Code);
            Assert.Equal("Licence.TradingRestricted", edited.FirstError.Code);
        }
        finally
        {
            accessor.HttpContext = null;
        }

        await ApplyAsync(binding, end, 3, restriction: "Unrestricted", releasedRevision: 2);
        using var resumed = await client.PutAsJsonAsync($"/api/suppliers/{supplierId}", SupplierInput("Resumed", true), Ct);
        Assert.True((await ReadSuccessAsync(resumed)).GetProperty("isActive").GetBoolean());
        Assert.Equal("Resumed", Assert.Single(await SupplierNamesAsync(binding.Shop)));
    }

    private async Task<string[]> SupplierNamesAsync(Guid shop)
    {
        await using var db = Open(Migrator);
        return await db.Suppliers.Where(x => x.ShopId == shop && !x.IsSystem)
            .OrderBy(x => x.Name).Select(x => x.Name).ToArrayAsync(Ct);
    }

    private static Task<HttpResponseMessage> CreateSupplierAsync(HttpClient client, string name, bool isActive = true) =>
        client.PostAsJsonAsync("/api/suppliers", SupplierInput(name, isActive), Ct);

    private static object SupplierInput(string name, bool isActive) => new
    {
        name, isActive, isPreferred = true, contactPersonName = "Sales", contactPersonPhone = "+919111111111",
        address = "42 MG Road", city = "Bengaluru", state = "Karnataka", pin = "560001",
    };
}
