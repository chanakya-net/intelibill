using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0214Tests(LicensingPostgreSqlFixture database)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    [Fact]
    public async Task SavedGrantReadsPendingUntilSignedAcknowledgement()
    {
        await using var fixture = await P0213Fixture.StartAsync(database);
        var (owner, account) = await LinkAndConfirmAsync(fixture);
        var manager = fixture.Trial.CatalogueManager!;
        await SaveGrantAsync(fixture, manager, account, owner.ShopId);

        var queued = await BothAsync(owner, manager, account, "Pending", "Queued");
        await fixture.DrainAsync(source: true);
        var awaiting = await BothAsync(owner, manager, account, "Pending", "AwaitingAcknowledgement");
        await fixture.DrainAsync(source: false);
        var applied = await BothAsync(owner, manager, account, "Applied", "Acknowledged");

        Assert.Equal(queued.GetProperty("savedAccountRevision").GetInt64(), applied.GetProperty("appliedAccountRevision").GetInt64());
        Assert.Equal(queued.GetProperty("savedShopRevision").GetInt64(), applied.GetProperty("appliedShopRevision").GetInt64());
        Assert.Equal(queued.GetProperty("savedVerifiedAt").GetDateTimeOffset(), awaiting.GetProperty("savedVerifiedAt").GetDateTimeOffset());
    }

    internal static async Task<(Merchant Owner, Guid Account)> LinkAndConfirmAsync(P0213Fixture fixture)
    {
        var owner = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var account = await CreateAccountAsync(owner.Client);
        await P0213Fixture.LinkAsync(account, owner, owner);
        await fixture.DrainAsync(source: true);
        await fixture.DrainAsync(source: false);
        return (owner, account);
    }

    internal static Task<JsonElement> SaveGrantAsync(P0213Fixture fixture, P0110CookieBrowser manager, Guid account, Guid shop)
    {
        var start = fixture.Hosts.StatusClock.Now;
        return P0212Fixture.GrantAsync(manager, account, P0212Fixture.Draft([shop], start, start.AddDays(5)));
    }

    private static async Task<JsonElement> MerchantStatusAsync(Merchant merchant)
    {
        using var response = await ShopStatusAsync(merchant.Client, merchant.ShopId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement> BothAsync(Merchant owner, P0110CookieBrowser manager, Guid account,
        string state, string delivery)
    {
        var merchant = await MerchantStatusAsync(owner);
        var provider = await P0212Fixture.ShopStatusAsync(manager, account, owner.ShopId);
        foreach (var status in new[] { merchant, provider })
        {
            Assert.True(status.GetProperty("observeOnly").GetBoolean());
            Assert.Equal(state, status.GetProperty("entitlementSynchronization").GetString());
        }
        var synchronization = merchant.GetProperty("synchronization");
        Assert.True(JsonElement.DeepEquals(synchronization, provider.GetProperty("synchronization")));
        Assert.Equal(delivery, synchronization.GetProperty("delivery").GetString());
        return synchronization;
    }
}
