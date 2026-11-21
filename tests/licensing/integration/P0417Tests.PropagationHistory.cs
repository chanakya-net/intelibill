using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Licensing.Application.Restrictions;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0417Tests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LaterReclosureCannotAcknowledgeAClosureOrReopeningThatNeverReachedTheMerchant(bool acknowledgeInOrder)
    {
        var clock = new P0201Clock { Now = P0202Fixture.WholeSecondNow() };
        await using var fixture = await P0213Fixture.StartAsync(database, businessClock: clock);
        var (owner, account, route) = await CloseAsync(fixture, clock.Now);
        var closed = await fixture.LatestSnapshotAsync(owner.ShopId);
        using var manager = await SignInAsync(fixture);
        if (acknowledgeInOrder) await DrainBothAsync(fixture);
        using var reopened = await P0212Fixture.PostAsync(manager, route, Command(await PreviewAsync(manager, route), owner));
        Assert.True(reopened.StatusCode == HttpStatusCode.Created, await reopened.Content.ReadAsStringAsync());
        var open = await fixture.LatestSnapshotAsync(owner.ShopId);

        if (acknowledgeInOrder)
        {
            await DrainBothAsync(fixture);
            await CloseShopAsync(owner, account);
        }
        else
        {
            // This merchant must remain behind throughout preparation. Model a committed closure whose
            // consent was already validated at the source before this merchant receives any of the three events.
            await QueueFixtureReclosureAsync(fixture, owner, open);
        }
        var final = await fixture.LatestSnapshotAsync(owner.ShopId);
        await AssertSnapshotDispositionAsync(fixture, final, "Applied");
        await DrainBothAsync(fixture);
        if (!acknowledgeInOrder)
        {
            await AssertSnapshotDispositionAsync(fixture, closed, "Superseded");
            await AssertSnapshotDispositionAsync(fixture, open, "Superseded");
        }
        await AssertClosureHistoryAsync(fixture, manager, account, acknowledgeInOrder);
    }

    private static async Task QueueFixtureReclosureAsync(P0213Fixture fixture, Merchant owner, EntitlementSnapshotV1 open)
    {
        await using var scope = fixture.Hosts.Licensing.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LicensingDbContext>();
        var closures = scope.ServiceProvider.GetRequiredService<IClosureStore>();
        var ct = CancellationToken.None;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.billing_account_id', {open.AccountId.ToString("D")}, true)", ct);
        await closures.LockAccountAsync(open.AccountId, ct);
        var target = Assert.Single(await closures.TargetsAsync(open.AccountId, owner.ShopId, ct));
        Assert.Equal(open.Shop!.ClosureRevision, target.ClosureVersion);
        var operation = Guid.NewGuid();
        var digest = new string('a', 64);
        await closures.SaveAsync(new(Guid.NewGuid(), open.AccountId, owner.UserId, owner.ShopId,
            operation, digest, EntitlementSnapshotsV1.Instant(fixture.AuthClock)), [target],
            new(open.AccountId, operation, Guid.NewGuid(), digest, [new(owner.ShopId, Guid.NewGuid(), digest)]), ct);
        await transaction.CommitAsync(ct);
    }

    private static async Task DrainBothAsync(P0213Fixture fixture)
    {
        await fixture.DrainAsync(source: true);
        await fixture.DrainAsync(source: false);
    }

    private static async Task AssertClosureHistoryAsync(P0213Fixture fixture, P0110CookieBrowser manager,
        Guid account, bool acknowledged)
    {
        using var response = await manager.SendAsync(HttpMethod.Get, $"/api/admin/accounts/{account:D}/closures");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var rows = (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray()
            .Select(row => Assert.Single(row.GetProperty("shops").EnumerateArray())).ToArray();
        Assert.Equal(2, rows.Length);
        var prior = Assert.Single(rows, row => row.TryGetProperty("reopening", out _));
        Assert.Equal(acknowledged ? "Applied" : "Superseded", prior.GetProperty("propagation").GetString());
        Assert.Equal(acknowledged ? "Acknowledged" : "Superseded", prior.GetProperty("reopening").GetProperty("propagation").GetString());
        Assert.Equal("Applied", Assert.Single(rows, row => !row.TryGetProperty("reopening", out _)).GetProperty("propagation").GetString());
        Assert.Equal(3L, await fixture.SourceScalarAsync($"""
            SELECT count(*) FROM public.entitlement_merchant_inbox i
            WHERE i.account_id = '{account:D}' AND i.kind = 'Acknowledgement'
                AND i.event_id IN (SELECT event_id FROM public.operational_closure_targets WHERE account_id = i.account_id
                    UNION ALL SELECT event_id FROM public.operational_closure_reopenings WHERE account_id = i.account_id)
                AND i.assertion <> ''
            """));
    }

}
