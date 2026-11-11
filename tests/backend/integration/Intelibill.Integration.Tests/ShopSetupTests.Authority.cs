using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace Intelibill.Integration.Tests;

public sealed partial class ShopSetupTests
{
    [Theory]
    [InlineData("Staff", false)]
    [InlineData("Manager", true)]
    public async Task Setup_UsesLiveReadAndWriteRoles(string role, bool canEditCatalog)
    {
        using var owner = CreateClient();
        using var member = CreateClient();
        var shop = await CreateShopAsync(owner);
        const string password = "Pass123!Aa";
        var email = $"setup-member-{Guid.NewGuid():N}@test.com";
        using var seed = new HttpRequestMessage
        {
            Content = JsonContent.Create(new { email, password, firstName = "Setup", lastName = "Member", phoneNumber = (string?)null, role, shopIds = new[] { shop } })
        };
        var account = await ReadAsync(await member.SeedVerifiedStaffAsync(seed));
        var user = account.GetProperty("userId").GetGuid();
        SetToken(member, await ReadAsync(await member.PostAsJsonAsync("/api/auth/login/email", new { email, password })));
        SetToken(member, await ReadAsync(await member.PostAsJsonAsync("/api/shops/switch", new { shopId = shop })));

        var status = await GetStatusAsync(member, shop);
        var permissions = status.GetProperty("permissions");
        Assert.False(permissions.GetProperty("canEditShop").GetBoolean());
        Assert.False(permissions.GetProperty("canSavePreferences").GetBoolean());
        Assert.Equal(canEditCatalog, permissions.GetProperty("canEditCatalog").GetBoolean());

        using var startup = await member.GetAsync($"/api/shops/{shop}/setup/preferences");
        Assert.Equal(HttpStatusCode.Forbidden, startup.StatusCode);
        using var denied = await SaveAsync(member, shop, 0, false);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var tax = await member.PutAsJsonAsync($"/api/shops/{shop}/tax-profile", new
        {
            taxRegime = "Unregistered", stateCode = "29", invoiceSeriesCode = "B"
        });
        Assert.Equal(HttpStatusCode.Forbidden, tax.StatusCode);

        _ = await ReadAsync(await owner.PatchAsJsonAsync($"/api/shops/{shop}/members/{user}", new { role, status = "Suspended", expectedVersion = 0 }));
        using var revoked = await member.GetAsync($"/api/shops/{shop}/setup");
        Assert.Contains(revoked.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden });
    }

    [Fact]
    public async Task Setup_ReadsVerifiedPhoneOwnersAndKeepsUndeclaredTaxIncomplete()
    {
        using var client = CreateClient();
        SetToken(client, await ReadAsync(await _factory.EnrollPhoneAsync(client, "+919900117755")));
        var shop = await CreateShopAsync(client, declareTax: false);
        _ = await AddServiceAsync(client);
        var status = await GetStatusAsync(client, shop);
        Assert.Equal("complete", Step(status, "identity"));
        Assert.Equal("incomplete", Step(status, "tax"));
        Assert.Contains("Invoice.TaxProfileRequired", Reasons(status));
        Assert.False(status.GetProperty("canTrade").GetBoolean());
    }

    [Fact]
    public async Task Preferences_EnforceForcedRlsForDirectRuntimeReadsAndWrites()
    {
        using var owner = CreateClient();
        var shop = await CreateShopAsync(owner);
        var other = await CreateShopAsync(owner);
        await using var connection = new NpgsqlConnection(_factory.RuntimeConnectionString);
        await connection.OpenAsync();
        await using var count = new NpgsqlCommand("SELECT count(*) FROM shop_setup_preferences", connection);
        Assert.Equal(0L, await count.ExecuteScalarAsync());

        await using var scope = new NpgsqlCommand("SELECT set_config('app.active_shop_id', @shop, false)", connection);
        scope.Parameters.AddWithValue("shop", shop.ToString());
        _ = await scope.ExecuteScalarAsync();
        Assert.Equal(1L, await count.ExecuteScalarAsync());

        await using var update = new NpgsqlCommand("UPDATE shop_setup_preferences SET show_on_startup = false WHERE shop_id = @shop", connection);
        update.Parameters.AddWithValue("shop", other);
        Assert.Equal(0, await update.ExecuteNonQueryAsync());
    }
}
