using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0417Tests
{
    private static readonly DateTimeOffset CoverageStart = P0202Fixture.WholeSecondNow();
    private static readonly DateTimeOffset PaidThrough = FirstOfMonth(CoverageStart).AddMonths(1).ToUniversalTime();

    [Theory]
    [InlineData(-1, "Active", true)]
    [InlineData(0, "Grace", false)]
    [InlineData(604800, "Expired", false)]
    [InlineData(-2, "Scheduled", false)]
    [InlineData(null, "PendingActivation", false)]
    public async Task ReopeningRequiresAdequateCurrentCoverageAtFixedBoundaries(int? secondsAfterPaidThrough,
        string expectedState, bool allowed)
    {
        var clock = new P0201Clock { Now = CoverageStart };
        await using var fixture = await P0213Fixture.StartAsync(database, businessClock: clock);
        var (owner, _, route) = await CloseAsync(fixture, clock.Now, paid: secondsAfterPaidThrough.HasValue);
        using var admin = await SignInAsync(fixture);
        if (secondsAfterPaidThrough is { } seconds)
            clock.Now = expectedState == "Scheduled" ? FirstOfMonth(CoverageStart).ToUniversalTime().AddTicks(-10)
                : seconds == -1 ? PaidThrough.AddTicks(-10) : PaidThrough.AddSeconds(seconds);
        var impact = await PreviewAsync(admin, route);
        var coverage = impact.GetProperty("coverage");
        Assert.Equal(expectedState, coverage.ValueKind == JsonValueKind.Null
            ? "PendingActivation" : coverage.GetProperty("state").GetString());
        Assert.Equal(allowed, impact.GetProperty("coverageAdequate").GetBoolean());
        using var response = await P0212Fixture.PostAsync(admin, route, Command(impact, owner));
        if (allowed)
            Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        else
            Assert.Equal("Reopening.CoverageRequired", await P0206Fixture.CodeAsync(response));
        Assert.Equal(allowed ? 1L : 0L, await fixture.Trial.CountAsync("operational_closure_reopenings"));
        Assert.Equal(allowed ? "Active" : "Closed", (await fixture.LatestSnapshotAsync(owner.ShopId)).Shop?.State);
    }

    [Fact]
    public async Task ClosedTimeIsNotCreditedBack()
    {
        var clock = new P0201Clock { Now = CoverageStart };
        await using var fixture = await P0213Fixture.StartAsync(database, businessClock: clock);
        var (owner, _, route) = await CloseAsync(fixture, clock.Now);
        using var admin = await SignInAsync(fixture);
        var closed = (await fixture.LatestSnapshotAsync(owner.ShopId)).Shop!;
        clock.Now = PaidThrough.AddTicks(-10);
        var impact = await PreviewAsync(admin, route);
        var command = Command(impact, owner);
        clock.Now = PaidThrough;
        using var stale = await P0212Fixture.PostAsync(admin, route, command);
        Assert.Equal("Reopening.VersionConflict", await P0206Fixture.CodeAsync(stale));

        clock.Now = PaidThrough.AddTicks(-10);
        using var reopened = await P0212Fixture.PostAsync(admin, route, command);
        Assert.True(reopened.StatusCode == HttpStatusCode.Created, await reopened.Content.ReadAsStringAsync());
        var active = (await fixture.LatestSnapshotAsync(owner.ShopId)).Shop!;
        Assert.Equal(closed.TermId, active.TermId);
        Assert.Equal(closed.StartsAt, active.StartsAt);
        Assert.Equal(closed.AccessEndsAt, active.AccessEndsAt);
        Assert.Equal(PaidThrough, active.NextBoundary);
        clock.Now = closed.AccessEndsAt!.Value;
        var status = await owner.Client.GetFromJsonAsync<JsonElement>($"/api/licensing/shops/{owner.ShopId:D}/status");
        Assert.Equal("Expired", status.GetProperty("state").GetString());
        Assert.Equal(1L, await fixture.Trial.CountAsync("subscription_coverage_terms"));
        Assert.Equal(0L, await fixture.Trial.CountAsync("trial_history"));
    }

    [Fact]
    public async Task ReopeningNeverCreatesOrResetsATrial()
    {
        var clock = new P0201Clock { Now = P0210Fixture.Start };
        await using var fixture = await P0213Fixture.StartAsync(database, businessClock: clock);
        var owner = await CreateTradingShopAsync(fixture.Hosts);
        var account = await CreateAccountAsync(owner.Client);
        await P0213Fixture.LinkAsync(account, owner, owner);
        var started = await P0210Tests.StartAndWaitAsync(owner, fixture.Trial.Accept());
        Assert.True(started.Status == "Applied", started.Reason);
        await fixture.DrainAsync(source: true);
        var route = await CloseShopAsync(owner, account);
        using var admin = await SignInAsync(fixture);
        var original = (await fixture.LatestSnapshotAsync(owner.ShopId)).Shop!;
        foreach (var state in new[] { "Active", "Expired" })
        {
            var impact = await PreviewAsync(admin, route);
            Assert.Equal("Trial", impact.GetProperty("coverage").GetProperty("basis").GetString());
            Assert.Equal(state, impact.GetProperty("coverage").GetProperty("state").GetString());
            using var refused = await P0212Fixture.PostAsync(admin, route, Command(impact, owner));
            Assert.Equal("Reopening.CoverageRequired", await P0206Fixture.CodeAsync(refused));
            clock.Now = clock.Now.AddDays(15);
        }
        Assert.Equal(original, (await fixture.LatestSnapshotAsync(owner.ShopId)).Shop);
        Assert.Equal(1L, await fixture.Trial.CountAsync("trial_history"));
        Assert.Equal(1L, await fixture.Trial.CountAsync("subscription_coverage_terms"));
        Assert.Equal(0L, await fixture.Trial.CountAsync("complimentary_grants"));
    }
}
