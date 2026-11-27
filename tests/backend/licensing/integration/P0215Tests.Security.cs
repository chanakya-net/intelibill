using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;
using static Intelibill.Licensing.Integration.Tests.P0215Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0215Tests
{
    [Fact]
    public async Task AuthorizationPrecedesReplayAndOnlyTheAcceptingActorCanRecover()
    {
        await using var fixture = await P0213Fixture.StartAsync(database);
        var payer = await SignInAsync(fixture.Hosts);
        var manager = await SignInAsync(fixture.Hosts);
        var shop = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var account = await CreateAccountAsync(payer.Client);
        var term = await SeedPaidAsync(fixture.Hosts, payer, account, shop);
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, $"""
            INSERT INTO billing_account_members(id, account_id, merchant_user_id, role, status, revision, created_at)
            VALUES (gen_random_uuid(), '{account:D}', '{manager.UserId:D}', 2, 1, 1, now())
            """);
        var path = CancellationPath(account, term);
        using var denied = await shop.Client.GetAsync(new Uri(path, UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var key = Guid.NewGuid();
        var preview = await PreviewAsync(manager, path);
        var command = Accept(preview, key);
        using var accepted = await manager.Client.PostAsJsonAsync(path, command);
        var receipt = await ReadAsync(accepted);
        var operationPath = new Uri(path + $"/operations/{key:D}", UriKind.Relative);
        using var wrongActor = await payer.Client.GetAsync(operationPath);
        Assert.Equal(HttpStatusCode.NotFound, wrongActor.StatusCode);
        using var replayedByOther = await payer.Client.PostAsJsonAsync(path, command);
        Assert.Equal(HttpStatusCode.Conflict, replayedByOther.StatusCode);

        fixture.Hosts.StatusClock.Now = EndsAt.AddDays(2);
        using var repeated = await manager.Client.PostAsJsonAsync(path, command);
        using var recovered = await manager.Client.GetAsync(operationPath);
        Assert.True(JsonElement.DeepEquals(receipt, await ReadAsync(repeated)));
        Assert.True(JsonElement.DeepEquals(receipt, await ReadAsync(recovered)));
        using var wrongTerm = await manager.Client.GetAsync(new Uri(CancellationPath(account, Guid.NewGuid()) + $"/operations/{key:D}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, wrongTerm.StatusCode);
        await RemoveAndVerifyAsync(fixture, manager, path, operationPath, command);
    }

    [Fact]
    public async Task ExpiredPreviewCannotScheduleAndForeignAccountCannotReadTermOrReceipt()
    {
        await using var fixture = await P0213Fixture.StartAsync(database);
        var payer = await SignInAsync(fixture.Hosts);
        var shop = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var account = await CreateAccountAsync(payer.Client);
        var otherAccount = await CreateAccountAsync(payer.Client);
        var term = await SeedPaidAsync(fixture.Hosts, payer, account, shop);
        var path = CancellationPath(account, term);
        var preview = await PreviewAsync(payer, path);
        var key = Guid.NewGuid();
        var command = Accept(preview, key);

        var foreignPath = CancellationPath(otherAccount, term);
        using var foreign = await payer.Client.GetAsync(new Uri(foreignPath, UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        using var read = await payer.Client.GetAsync(new Uri(foreignPath + $"/operations/{key:D}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);

        fixture.Hosts.StatusClock.Now = EndsAt;
        using var expired = await payer.Client.PostAsJsonAsync(path, command);
        Assert.Equal(HttpStatusCode.Conflict, expired.StatusCode);
        Assert.Equal(0L, await fixture.Trial.CountAsync("subscription_cancellation_receipts"));
    }

    private static async Task RemoveAndVerifyAsync(P0213Fixture fixture, Merchant manager, string path, Uri operationPath, object command)
    {
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, $"""
            UPDATE billing_account_members SET status = 2, revision = revision + 1
            WHERE merchant_user_id = '{manager.UserId:D}'
            """);
        using var removedRead = await manager.Client.GetAsync(operationPath);
        using var removedReplay = await manager.Client.PostAsJsonAsync(path, command);
        Assert.Equal(HttpStatusCode.Forbidden, removedRead.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, removedReplay.StatusCode);
    }

    [Fact]
    public async Task ProviderDetailUsesLiveGrantAndRedactsCommercialFactsFromSupport()
    {
        await using var fixture = await P0213Fixture.StartAsync(database);
        var owner = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var account = await CreateAccountAsync(owner.Client);
        var term = await SeedPaidAsync(fixture.Hosts, owner, account, owner);
        var path = CancellationPath(account, term);
        using var response = await owner.Client.PostAsJsonAsync(path, Accept(await PreviewAsync(owner, path), Guid.NewGuid()));
        await ReadAsync(response);

        var providerPath = $"/api/admin/accounts/{account:D}/subscriptions/{term:D}/cancellation";
        using var provider = await fixture.Trial.CatalogueManager!.SendAsync(HttpMethod.Get, providerPath);
        var commercial = await ReadAsync(provider);
        Assert.True(provider.Headers.CacheControl?.NoStore);
        Assert.True(JsonElement.DeepEquals(commercial, await PreviewAsync(owner, path)));
        Assert.Equal(149900L, commercial.GetProperty("commercial").GetProperty("pricePaise").GetInt64());

        var catalogue = fixture.Trial.Catalogue;
        await catalogue.GrantAsync(P0114Tests.Invitee, "support");
        using var support = await catalogue.SignInOperatorAsync(fixture.Trial.CatalogueManagerSeed!);
        using var read = await support.SendAsync(HttpMethod.Get, providerPath);
        var redacted = await ReadAsync(read);
        Assert.Equal(JsonValueKind.Null, redacted.GetProperty("commercial").ValueKind);
        Assert.True(JsonElement.DeepEquals(commercial.GetProperty("affectedShops"), redacted.GetProperty("affectedShops")));
        Assert.False(redacted.TryGetProperty("actorId", out _));
        Assert.False(redacted.TryGetProperty("operationKey", out _));
    }
}
