using System.Net;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0404Tests
{
    [Fact]
    public async Task ReleaseAndPublicationCommitTogether()
    {
        await using var fixture = await StartAsync();
        var shop = await CreateTradingShopAsync(fixture.Hosts);
        var account = await LinkAsync(fixture, shop);
        using var admin = await SignInSecurityAsync(fixture);
        var path = $"/api/admin/accounts/{account:D}/restrictions";
        var id = await SuspendAsync(admin, path, "Account", null);
        var releasePath = $"{path}/{id:D}/release";
        var command = await PreviewReleaseAsync(admin, releasePath);
        var suspended = await fixture.LatestSnapshotAsync(shop.ShopId);
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, """
            CREATE FUNCTION public.reject_p0404_outbox_fixture() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Release outbox fixture failure'; END $$;
            CREATE TRIGGER reject_p0404_outbox_fixture BEFORE INSERT ON public.entitlement_messages
                FOR EACH ROW EXECUTE FUNCTION public.reject_p0404_outbox_fixture();
            """);
        try
        {
            using var refusal = await P0212Fixture.PostAsync(admin, releasePath, command);
            Assert.Equal("Status.Unavailable", await P0206Fixture.CodeAsync(refusal));
            Assert.Equal(suspended, await fixture.LatestSnapshotAsync(shop.ShopId));
            Assert.Equal(0L, await fixture.Trial.CountAsync("operational_restriction_releases"));
        }
        finally
        {
            await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, """
                DROP TRIGGER reject_p0404_outbox_fixture ON public.entitlement_messages;
                DROP FUNCTION public.reject_p0404_outbox_fixture();
                """);
        }
        using var completed = await P0212Fixture.PostAsync(admin, releasePath, command);
        Assert.True(completed.StatusCode == HttpStatusCode.Created, await completed.Content.ReadAsStringAsync());
        Assert.Equal(1L, await fixture.Trial.CountAsync("operational_restriction_releases"));
    }

    [Fact]
    public async Task EmptyAccountReleaseIsDeliveredToItsFirstAffiliation()
    {
        await using var fixture = await StartAsync();
        var shop = await CreateTradingShopAsync(fixture.Hosts);
        var account = await CreateAccountAsync(shop.Client);
        using var admin = await SignInSecurityAsync(fixture);
        var path = $"/api/admin/accounts/{account:D}/restrictions";
        var id = await SuspendAsync(admin, path, "Account", null);
        var result = await SubmitReleaseAsync(admin, $"{path}/{id:D}/release");
        Assert.Equal("NoShops", result.GetProperty("release").GetProperty("propagation").GetString());
        Assert.Equal(0L, await fixture.Trial.CountAsync("operational_restriction_release_deliveries"));
        await LinkAsync(fixture, shop, existingAccount: account);
        Assert.Equal("Pending", await ReleasePropagationAsync(admin, path, id));
        await fixture.DrainAsync(source: true);
        await fixture.DrainAsync(source: false);
        Assert.Equal("Acknowledged", await ReleasePropagationAsync(admin, path, id));
    }
}
