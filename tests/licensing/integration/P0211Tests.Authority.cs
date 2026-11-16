using System.Net;
using System.Net.Http.Headers;
using Intelibill.Domain.Enums;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0211Tests
{
    [Fact]
    public async Task OnlyALiveLicensingManagerWithRecentMfaCanExtendOrRecover()
    {
        await using var fixture = await P0210Fixture.StartAsync(database);
        var (owner, account) = await StartTrialAsync(fixture);
        var path = Path(account, owner.ShopId);
        var manager = fixture.CatalogueManager!;
        var command = Confirm(await PreviewAsync(manager, path), Guid.NewGuid());
        var recovery = $"{path}/operations/{command.OperationKey:D}";

        using (var platform = await fixture.Catalogue.SignInOwnerAsync())
            await AssertDeniedAsync(platform, path, command);
        using (var transport = await P0212Fixture.PostAsync(manager, path, command, "https://foreign.example"))
            Assert.Equal(HttpStatusCode.Forbidden, transport.StatusCode);
        using (var noCsrf = await manager.SendAsync(HttpMethod.Post, path, command))
            Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);
        await AssertOtherSchemesDeniedAsync(fixture, path, command);

        using (var created = await P0212Fixture.PostAsync(manager, path, command))
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        fixture.Catalogue.Clock.Advance(TimeSpan.FromMinutes(5));
        using (var stale = await P0212Fixture.PostAsync(manager, path, command))
            Assert.Equal("TrialExtension.RecentMfaRequired", await P0206Fixture.CodeAsync(stale));
        using (var staleRecovery = await manager.SendAsync(HttpMethod.Get, recovery))
            Assert.Equal("TrialExtension.RecentMfaRequired", await P0206Fixture.CodeAsync(staleRecovery));

        const string otherEmail = "other.manager@example.test";
        using var platformOwner = await fixture.Catalogue.SignInOwnerAsync();
        var seed = await fixture.Catalogue.EnrollOperatorAsync(platformOwner, otherEmail);
        await fixture.Catalogue.GrantAsync(otherEmail, "support", "billing", "security-administrator");
        using (var support = await fixture.Catalogue.SignInOperatorAsync(seed, otherEmail))
            await AssertDeniedAsync(support, path, command);

        fixture.Catalogue.Clock.Advance(TimeSpan.FromMinutes(10));
        await fixture.Catalogue.GrantAsync(otherEmail, P0206Fixture.Manager);
        using var other = await fixture.Catalogue.SignInOperatorAsync(seed, otherEmail);
        using var hidden = await other.SendAsync(HttpMethod.Get, recovery);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        using var reused = await P0212Fixture.PostAsync(other, path, command);
        Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);
        Assert.DoesNotContain(Reason, await reused.Content.ReadAsStringAsync());
        Assert.Equal(1L, await fixture.CountAsync("trial_extension_receipts"));
    }

    [Fact]
    public async Task ShopsOutsideTheAccountOrWithoutATrialDiscloseNothing()
    {
        await using var fixture = await P0210Fixture.StartAsync(database);
        var (owner, account) = await StartTrialAsync(fixture);
        var other = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var otherAccount = await CreateAccountAsync(other.Client);
        await fixture.LinkAsync(otherAccount, other.UserId, other);
        var manager = fixture.CatalogueManager!;
        foreach (var path in new[] { Path(otherAccount, owner.ShopId), Path(otherAccount, other.ShopId) })
        {
            using var response = await P0212Fixture.PostAsync(manager, path + "/preview",
                new { endsAt = "2027-03-10T13:15:30Z", reason = Reason, reviewReference = Review });
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("TrialExtension.TrialNotFound", await P0206Fixture.CodeAsync(response));
        }
        Assert.Equal(0L, await fixture.CountAsync("trial_extensions"));
        Assert.NotEqual(account, otherAccount);
    }

    private static async Task AssertDeniedAsync(P0110CookieBrowser browser, string path, ExtensionCommand command)
    {
        using var preview = await P0212Fixture.PostAsync(browser, path + "/preview",
            new { command.EndsAt, command.Reason, command.ReviewReference });
        Assert.Equal(HttpStatusCode.Forbidden, preview.StatusCode);
        Assert.Equal("TrialExtension.AccessDenied", await P0206Fixture.CodeAsync(preview));
        using var write = await P0212Fixture.PostAsync(browser, path, command);
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
        using var recovery = await browser.SendAsync(HttpMethod.Get, $"{path}/operations/{command.OperationKey:D}");
        Assert.Equal(HttpStatusCode.Forbidden, recovery.StatusCode);
    }

    private static async Task AssertOtherSchemesDeniedAsync(P0210Fixture fixture, string path, ExtensionCommand command)
    {
        using var merchant = P0114Tests.RawClient(fixture.Catalogue.Api);
        var token = P0109Tests.CreateMerchantToken(fixture.Catalogue.Clock.GetUtcNow());
        merchant.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var bearer = await merchant.GetAsync(new Uri($"{path}/operations/{command.OperationKey:D}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Unauthorized, bearer.StatusCode);

        using var local = new P0110CookieBrowser(P0114Tests.RawClient(fixture.Catalogue.Api));
        local.Set("Intelibill.Admin.FixtureSession", P0206Fixture.FixtureToken);
        using var synthetic = await local.SendAsync(HttpMethod.Post, path, command);
        Assert.Equal(HttpStatusCode.Unauthorized, synthetic.StatusCode);
    }
}
