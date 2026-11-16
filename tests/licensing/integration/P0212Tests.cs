using System.Net;
using System.Text.Json;
using static Intelibill.Licensing.Integration.Tests.P0212Fixture;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// P02-12: a Licensing Manager issues an explicit, dated complimentary grant with named capacity and a reason.
/// It records no money, creates no grace and never displaces existing access or restrictions.
/// </summary>
[Collection(MerchantBillingHosts.Name)]
public sealed partial class P0212Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    [Fact]
    public async Task ManagerCanPreviewAndGrantNamedComplimentaryAccessWithoutRevenue()
    {
        await using var fixture = await StartAsync(database);
        var (manager, _) = await fixture.ManagerAsync();
        using var session = manager;
        var (account, _, shops) = await fixture.SeedAccountAsync(3);
        var named = shops[..2];
        var (other, _, otherShops) = await fixture.SeedAccountAsync(1);

        // Authentication control: the same session reads provider status for the account.
        using (var control = await manager.SendAsync(HttpMethod.Get, $"/api/admin/accounts/{account:D}/status"))
            Assert.Equal(HttpStatusCode.OK, control.StatusCode);

        var preview = await PreviewAsync(manager, account, Draft(named));
        Assert.Equal(1, preview.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("Complimentary", preview.GetProperty("basis").GetString());
        Assert.Equal(Starts, preview.GetProperty("startsAt").GetDateTimeOffset());
        Assert.Equal(Ends, preview.GetProperty("endsAt").GetDateTimeOffset());
        Assert.Equal("Grouped", preview.GetProperty("coverageMode").GetString());
        Assert.Equal(2, preview.GetProperty("shopCapacity").GetInt32());
        Assert.Equal(4, preview.GetProperty("memberAllowance").GetInt32());
        Assert.Equal(0, preview.GetProperty("expectedVersion").GetInt64());
        Assert.False(preview.GetProperty("paymentRecorded").GetBoolean());
        Assert.Equal(JsonValueKind.Null, preview.GetProperty("graceEndsAt").ValueKind);
        Assert.Equal(64, preview.GetProperty("sourceHash").GetString()!.Length);
        Assert.Equal(named.Order().ToArray(), preview.GetProperty("shopIds").EnumerateArray().Select(item => item.GetGuid()).ToArray());
        Assert.Equal(0L, await fixture.CountAsync("SELECT count(*) FROM complimentary_grants"));

        var key = Guid.NewGuid();
        using var created = await PostAsync(manager, Base(account), Confirm(preview, key));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var grant = await P0206Fixture.BodyAsync(created);
        Assert.Equal(key, grant.GetProperty("operationKey").GetGuid());
        Assert.Equal(1, grant.GetProperty("grantedVersion").GetInt64());
        Assert.Equal(Ends, grant.GetProperty("endsAt").GetDateTimeOffset());

        // One immutable grant, two named links, a receipt and a revision; no money or paid supplement at all.
        Assert.Equal(1L, await fixture.CountAsync("SELECT count(*) FROM complimentary_grants"));
        Assert.Equal(2L, await fixture.CountAsync("SELECT count(*) FROM complimentary_grant_shops"));
        Assert.Equal(1L, await fixture.CountAsync("SELECT count(*) FROM complimentary_grant_receipts"));
        Assert.Equal(1L, await fixture.CountAsync($"SELECT revision FROM complimentary_grant_scopes WHERE account_id = '{account:D}'"));
        Assert.Equal(0L, await fixture.CountAsync("SELECT count(*) FROM subscription_term_facts"));
        Assert.Equal(1L, await fixture.CountAsync(
            $"SELECT count(*) FROM subscription_coverage_terms WHERE account_id = '{account:D}' AND mode = 'Grouped' AND capacity = 2"));

        var status = await ShopStatusAsync(manager, account, named[0]);
        var term = status.GetProperty("term");
        Assert.Equal("Active", status.GetProperty("state").GetString());
        Assert.Equal("Complimentary", term.GetProperty("basis").GetString());
        Assert.Equal(Ends, term.GetProperty("accessEndsAt").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, term.GetProperty("graceEndsAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, term.GetProperty("nextPeriodEndsAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, term.GetProperty("commercial").ValueKind);
        Assert.Equal(4, term.GetProperty("memberAllowance").GetInt32());
        Assert.Equal(2, term.GetProperty("purchasedCapacity").GetInt32());
        Assert.DoesNotContain("Launch partner", status.GetRawText());
        // The unnamed sibling and the unrelated account gain nothing.
        Assert.Equal("PendingActivation", (await ShopStatusAsync(manager, account, shops[2])).GetProperty("state").GetString());
        Assert.Equal("PendingActivation", (await ShopStatusAsync(manager, other, otherShops[0])).GetProperty("state").GetString());

        using var replay = await PostAsync(manager, Base(account), Confirm(preview, key));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(grant.GetRawText(), (await P0206Fixture.BodyAsync(replay)).GetRawText());
        using var recovered = await manager.SendAsync(HttpMethod.Get, $"{Base(account)}/operations/{key:D}");
        Assert.Equal(grant.GetRawText(), (await P0206Fixture.BodyAsync(recovered)).GetRawText());
        Assert.Equal(1L, await fixture.CountAsync("SELECT count(*) FROM complimentary_grants"));
    }

    [Fact]
    public async Task ComplimentaryGrantRouteRequiresConfirmation()
    {
        await using var fixture = await StartAsync(database);
        var (manager, _) = await fixture.ManagerAsync();
        using var session = manager;
        var (account, _, shops) = await fixture.SeedAccountAsync(1);

        using var unconfirmed = await PostAsync(manager, Base(account), new
        {
            operationKey = Guid.NewGuid(), shopIds = shops, startsAt = Starts.ToString("O"), endsAt = Ends.ToString("O"),
            memberAllowance = 3, reason = "Courtesy", expectedVersion = 0, sourceHash = new string('a', 64), confirmed = false,
        });
        Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.StatusCode);
        Assert.Equal("Grant.Invalid", await P0206Fixture.CodeAsync(unconfirmed));
        Assert.Equal(0L, await fixture.CountAsync("SELECT count(*) FROM subscription_coverage_terms"));
    }
}
