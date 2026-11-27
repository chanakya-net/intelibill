using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0202Tests(LicensingPostgreSqlFixture database)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    private static readonly Uri MerchantAccounts = new("/api/billing/accounts", UriKind.Relative);

    [Fact]
    public async Task MerchantOrigin_CreatesAndListsThroughDelegation()
    {
        await database.PrepareMerchantAsync();
        using var fixture = await P0201Fixture.CreateAsync(database);
        fixture.Clock.Now = P0202Fixture.WholeSecondNow();
        await using var licensing = P0202Fixture.LicensingApi(fixture);
        await using var merchant = fixture.MerchantApi(handler: () => licensing.Server.CreateHandler(),
            configure: services => services.AddSingleton<TimeProvider>(fixture.Clock));
        using var client = merchant.CreateClient();
        var email = P0202Fixture.UniqueEmail();
        var actor = await P0201Tests.SeedMerchantAsync(merchant.Services, email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await P0201Tests.LoginAsync(client, email));

        using var empty = await client.GetAsync(MerchantAccounts);
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        Assert.True(empty.Headers.CacheControl?.NoStore);
        Assert.Empty((await empty.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accounts").EnumerateArray());

        var operationKey = Guid.NewGuid();
        using var created = await client.PostAsJsonAsync(MerchantAccounts, new { operationKey, name = "  Acme Billing  " });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.True(created.Headers.CacheControl?.NoStore);
        var account = await created.Content.ReadFromJsonAsync<JsonElement>();
        var accountId = account.GetProperty("accountId").GetGuid();
        Assert.Equal("Acme Billing", account.GetProperty("name").GetString());
        Assert.Equal("Owner", account.GetProperty("role").GetString());
        Assert.Equal(fixture.Clock.Now, account.GetProperty("createdAt").GetDateTimeOffset());
        Assert.Equal(4, account.EnumerateObject().Count());

        using var retried = await client.PostAsJsonAsync(MerchantAccounts, new { operationKey, name = "Acme Billing" });
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        Assert.Equal(account.GetRawText(), (await retried.Content.ReadFromJsonAsync<JsonElement>()).GetRawText());

        using var listed = await client.GetAsync(MerchantAccounts);
        var accounts = (await listed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accounts");
        Assert.Equal(account.GetRawText(), Assert.Single(accounts.EnumerateArray()).GetRawText());

        using var access = await client.GetAsync(new Uri($"/api/billing/accounts/{accountId:D}/access", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, access.StatusCode);

        var rows = await P0202Fixture.ReadAccountRowsAsync(database, fixture);
        Assert.Equal([new P0202Fixture.OwnerRow(accountId, actor, 1, 1)], rows);
    }
}
