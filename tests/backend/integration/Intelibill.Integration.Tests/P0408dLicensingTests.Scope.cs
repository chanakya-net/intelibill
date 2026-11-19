using System.Net;
using System.Net.Http.Json;
using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

public sealed partial class P0408dLicensingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AccessHintsDescribeTheRequestedShop(bool shopSuspension)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var selected = await CreateShopAsync(client);
        var active = await CreateShopAsync(client, register: false);
        var binding = await BindAsync(selected);
        var end = _clock.Now.AddDays(1);
        await ApplyAsync(binding, end, state: "PendingActivation");
        await EnforceAsync(selected);

        var activePath = $"/api/shops/{active}";
        var selectedPath = $"/api/shops/{selected}";
        using var a = await client.GetAsync(activePath, Ct);
        using var b = await client.GetAsync(selectedPath, Ct);
        Assert.True((await ReadSuccessAsync(a)).GetProperty("setupAccess").GetProperty("canEditOperatingSettings").GetBoolean());
        var hint = (await ReadSuccessAsync(b)).GetProperty("setupAccess");
        Assert.Equal(selected, hint.GetProperty("shopId").GetGuid());
        Assert.False(hint.GetProperty("canEditOperatingSettings").GetBoolean());
        Assert.True(hint.GetProperty("canPrepareTax").GetBoolean());

        await SwitchAsync(client, selected);
        using var status = await client.GetAsync($"{selectedPath}/setup", Ct);
        Assert.Equal(hint.GetRawText(), (await ReadSuccessAsync(status)).GetProperty("setupAccess").GetRawText());
        await SwitchAsync(client, active);

        if (shopSuspension)
            await ApplyAsync(binding, end, 2, shopSuspension: "Security");
        else
            await ApplyAsync(binding, end, 2, restriction: "Security");
        await AssertProblemAsync(await client.GetAsync(selectedPath, Ct), HttpStatusCode.Forbidden, "Licence.SecuritySuspended");
        await AssertProblemAsync(await client.PutAsJsonAsync(selectedPath, Profile("Barred", "42 MG Road"), Ct),
            HttpStatusCode.Forbidden, "Licence.SecuritySuspended");
        using var allowed = await client.PutAsJsonAsync(activePath, Profile("Independent", "42 MG Road"), Ct);
        Assert.Equal("Independent", (await ReadSuccessAsync(allowed)).GetProperty("name").GetString());

        var created = await CreateShopAsync(client, register: false);
        await using var db = Open(Migrator);
        Assert.False(await db.Set<ShopLicenseState>().AnyAsync(x => x.ShopId == created, Ct));
    }

    private static async Task SwitchAsync(HttpClient client, Guid shop)
    {
        using var response = await client.PostAsJsonAsync("/api/shops/switch", new { shopId = shop }, Ct);
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await ReadSuccessAsync(response)).GetProperty("accessToken").GetString());
    }
}
