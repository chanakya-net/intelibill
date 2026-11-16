using System.Net;
using System.Text.Json;
using static Intelibill.Licensing.Integration.Tests.P0212Fixture;
using Seed = Intelibill.Licensing.Integration.Tests.P0209Fixture.TermSeed;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0212Tests
{
    private static readonly DateTimeOffset PaidEnds = P0209Fixture.Utc("2027-03-01T04:30:00Z");
    private static readonly DateTimeOffset FutureStarts = P0209Fixture.Utc("2027-03-15T04:30:00Z");

    [Fact]
    public async Task ComplimentaryWindowsPreserveBaselineAccessAtEveryBoundary()
    {
        await using var fixture = await StartAsync(database);
        var (manager, _) = await fixture.ManagerAsync();
        using var session = manager;
        var (account, payer, free) = await fixture.SeedAccountAsync(1);
        var paid = await fixture.AffiliatedShopAsync(account, payer, new Seed(P0209Fixture.Utc("2027-02-01T04:30:00Z"), PaidEnds,
            AnchorLocal: "2027-02-01 10:00:00"));
        var future = await fixture.AffiliatedShopAsync(account, payer, new Seed(FutureStarts, P0209Fixture.Utc("2027-04-15T04:30:00Z"),
            AnchorLocal: "2027-03-15 10:00:00"));
        var legacy = await fixture.AffiliatedShopAsync(account, payer, new Seed(P0209Fixture.Utc("2027-01-01T00:00:00Z"),
            P0209Fixture.Utc("2027-06-01T00:00:00Z"), Basis: null));
        var priorFacts = await FactsSnapshotAsync(fixture);
        var grant = await GrantAsync(manager, account, Draft([free[0], paid, future, legacy]));
        Assert.Equal("Grouped", grant.GetProperty("coverageMode").GetString());
        Assert.Equal(4, grant.GetProperty("shopCapacity").GetInt32());

        async Task Expect(Guid shop, DateTimeOffset at, string state, string? basis)
        {
            fixture.Lifecycle.Now = at;
            var status = await ShopStatusAsync(manager, account, shop);
            Assert.True(state == status.GetProperty("state").GetString(), $"{shop} @ {at:O}: {status}");
            Assert.Equal(basis, status.GetProperty("term").GetProperty("basis").GetString());
        }

        await Expect(free[0], Starts.AddTicks(-10), "Scheduled", "Complimentary");
        await Expect(free[0], Starts, "Active", "Complimentary");
        await Expect(paid, Starts, "Active", "Paid");
        await Expect(paid, PaidEnds, "Grace", "Paid");
        await Expect(paid, PaidEnds.AddDays(7).AddTicks(-10), "Grace", "Paid");
        await Expect(paid, PaidEnds.AddDays(7), "Active", "Complimentary");
        await Expect(future, Now, "Active", "Complimentary");
        await Expect(future, FutureStarts, "Active", "Paid");
        await Expect(legacy, Now, "EvidenceIncomplete", null);
        await Expect(free[0], Ends.AddTicks(-10), "Active", "Complimentary");
        await Expect(free[0], Ends, "Expired", "Complimentary");
        await Expect(paid, Ends, "Expired", "Complimentary");
        await Expect(future, Ends, "Active", "Paid");

        // The end creates no grace, now or seven days later.
        fixture.Lifecycle.Now = Ends.AddDays(7);
        var ended = (await ShopStatusAsync(manager, account, free[0])).GetProperty("term");
        Assert.Equal("Expired", ended.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, ended.GetProperty("graceEndsAt").ValueKind);
        Assert.Equal(Ends, ended.GetProperty("accessEndsAt").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, ended.GetProperty("nextDeadline").ValueKind);

        // The account lists the grant once with its named shops; prior paid facts are unchanged.
        using var page = await manager.SendAsync(HttpMethod.Get, $"/api/admin/accounts/{account:D}/status");
        var items = (await P0206Fixture.BodyAsync(page)).GetProperty("items").EnumerateArray().ToArray();
        var granted = Assert.Single(items, item => item.GetProperty("status").GetProperty("basis").GetString() == "Complimentary");
        Assert.Equal(4, granted.GetProperty("coveredShopCount").GetInt32());
        Assert.Equal(4, items.Length);
        Assert.Equal(priorFacts, await FactsSnapshotAsync(fixture));
    }

    [Fact]
    public async Task AdjacentWindowsAreLegalAndOverlapsAreRefused()
    {
        await using var fixture = await StartAsync(database);
        var (manager, _) = await fixture.ManagerAsync();
        using var session = manager;
        var (account, _, shops) = await fixture.SeedAccountAsync(2);
        await GrantAsync(manager, account, Draft([shops[0]]));

        var next = await GrantAsync(manager, account, Draft([shops[0]], Ends, Ends.AddDays(30)));
        Assert.Equal(2, next.GetProperty("grantedVersion").GetInt64());
        Assert.Equal("Independent", next.GetProperty("coverageMode").GetString());
        using var overlap = await PostAsync(manager, Base(account) + "/preview", Draft(shops, Ends.AddDays(-1), Ends.AddDays(1)));
        Assert.Equal(HttpStatusCode.Conflict, overlap.StatusCode);
        Assert.Equal("Grant.Overlap", await P0206Fixture.CodeAsync(overlap));
        // Disjoint shops stay independent: the second shop may take the same window.
        await GrantAsync(manager, account, Draft([shops[1]], Ends.AddDays(-1), Ends.AddDays(1)));
        Assert.Equal(3L, await fixture.CountAsync($"SELECT revision FROM complimentary_grant_scopes WHERE account_id = '{account:D}'"));
    }

    [Fact]
    public async Task InvalidGrantRequestsAreRefusedWithoutFacts()
    {
        await using var fixture = await StartAsync(database);
        var (manager, _) = await fixture.ManagerAsync();
        using var session = manager;
        var (account, _, shops) = await fixture.SeedAccountAsync(1);
        var (_, _, foreign) = await fixture.SeedAccountAsync(1);
        var preview = Base(account) + "/preview";

        async Task Refused(object body, HttpStatusCode status, string code)
        {
            using var response = await PostAsync(manager, preview, body);
            Assert.True(status == response.StatusCode, $"{code}: {await response.Content.ReadAsStringAsync()}");
            Assert.Equal(code, await P0206Fixture.CodeAsync(response));
        }

        await Refused(Draft(shops, Ends, Starts), HttpStatusCode.BadRequest, "Grant.Invalid");
        await Refused(Draft(shops, Starts, Starts), HttpStatusCode.BadRequest, "Grant.Invalid");
        await Refused(Draft([]), HttpStatusCode.BadRequest, "Grant.Invalid");
        await Refused(Draft([shops[0], shops[0]]), HttpStatusCode.BadRequest, "Grant.Invalid");
        await Refused(Draft(shops, memberAllowance: 0), HttpStatusCode.BadRequest, "Grant.Invalid");
        await Refused(Draft(shops, memberAllowance: 100_001), HttpStatusCode.BadRequest, "Grant.Invalid");
        await Refused(Draft(shops, reason: " "), HttpStatusCode.BadRequest, "Grant.Invalid");
        await Refused(Draft(shops, reason: new string('r', 501)), HttpStatusCode.BadRequest, "Grant.Invalid");
        await Refused(Draft(shops, reason: "line\nbreak"), HttpStatusCode.BadRequest, "Grant.Invalid");
        await Refused(Draft([.. Enumerable.Range(0, 101).Select(_ => Guid.NewGuid())]), HttpStatusCode.BadRequest, "Grant.Invalid");
        await Refused(new { shopIds = shops, startsAt = "2027-03-01T00:00:00", endsAt = "2027-04-01T00:00:00Z", memberAllowance = 2, reason = "No offset" },
            HttpStatusCode.BadRequest, "Grant.Invalid");
        await Refused(new { shopIds = shops, startsAt = "2027-03-01T00:00:00.0000001Z", endsAt = "2027-04-01T00:00:00Z", memberAllowance = 2, reason = "Sub-microsecond" },
            HttpStatusCode.BadRequest, "Grant.Invalid");
        await Refused(new { shopIds = shops, startsAt = "9999-12-31T23:59:59Z", endsAt = "+10000-01-01T00:00:00Z", memberAllowance = 2, reason = "Overflow" },
            HttpStatusCode.BadRequest, "Grant.Invalid");
        await Refused(new { shopIds = shops, startsAt = (string?)null, endsAt = "2027-04-01T00:00:00Z", memberAllowance = 2, reason = "Open" },
            HttpStatusCode.BadRequest, "Grant.Invalid");
        await Refused(new { shopIds = shops, startsAt = "2027-03-01T00:00:00Z", endsAt = "2027-04-01T00:00:00Z", memberAllowance = "2", reason = "String number" },
            HttpStatusCode.BadRequest, "Grant.Invalid");
        await Refused(Draft(foreign), HttpStatusCode.BadRequest, "Grant.ShopNotAffiliated");
        await Refused(Draft([Guid.NewGuid()]), HttpStatusCode.BadRequest, "Grant.ShopNotAffiliated");
        await Refused(Draft(shops, Now.AddDays(-30), Now), HttpStatusCode.BadRequest, "Grant.AlreadyEnded");

        // An explicit already-started interval keeps its recorded start; offsets normalize to the same instant.
        var started = await PreviewAsync(manager, account, new
        {
            shopIds = shops, startsAt = "2027-02-20T05:30:00+05:30", endsAt = "2027-03-31T18:30:00.000001Z",
            memberAllowance = 2, reason = "Already started",
        });
        Assert.Equal("2027-02-20T00:00:00.000000Z", started.GetProperty("startsAt").GetString());
        Assert.Equal("2027-03-31T18:30:00.000001Z", started.GetProperty("endsAt").GetString());
        Assert.Equal(0L, await fixture.CountAsync("SELECT count(*) FROM complimentary_grants"));
        Assert.Equal(0L, await fixture.CountAsync("SELECT count(*) FROM complimentary_grant_scopes"));
    }

    private static async Task<string> FactsSnapshotAsync(P0212Fixture fixture) =>
        (string)(await fixture.Provider.ScalarAsync("""
            SELECT json_build_array(
                (SELECT json_agg(f ORDER BY term_id) FROM subscription_term_facts f),
                (SELECT json_agg(a ORDER BY request_id) FROM shop_affiliations a),
                (SELECT json_agg(m ORDER BY id) FROM billing_account_members m))::text
            """))!;
}
