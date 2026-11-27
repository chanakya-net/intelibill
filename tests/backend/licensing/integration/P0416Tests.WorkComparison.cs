using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0416Tests
{
    [Fact]
    public async Task EqualWorkCountsRequireFreshReviewAtBothConfirmationBoundaries()
    {
        var clock = new P0201Clock { Now = P0202Fixture.WholeSecondNow() };
        await using var fixture = await P0213Fixture.StartAsync(database, businessClock: clock, observeDelegation: true);
        var owner = await CreateTradingShopAsync(fixture.Hosts);
        var account = await CreateAccountAsync(owner.Client);
        await P0213Fixture.LinkAsync(account, owner, owner);
        await fixture.DrainAsync(source: true);
        await SeedIntakeReceiptsAsync(fixture, owner, clock.Now);

        await using var context = MerchantDb(fixture.Hosts.Database);
        var original = NewCase(owner, "open");
        var escalated = NewCase(owner, "escalated");
        Assert.False(escalated.Escalate(owner.UserId, clock.Now).IsError);
        var resolved = NewCase(owner, "resolved");
        resolved.Resolve(owner.UserId, clock.Now);
        context.ReconciliationIssues.AddRange(original, escalated, resolved);
        await context.SaveChangesAsync();

        var path = $"/api/licensing/shops/{owner.ShopId:D}/closure";
        var first = await ReadCountedReviewAsync(owner, path);
        original.Resolve(owner.UserId, clock.Now);
        var replacement = NewCase(owner, "replacement");
        context.ReconciliationIssues.Add(replacement);
        await context.SaveChangesAsync();
        var second = await ReadCountedReviewAsync(owner, path);
        Assert.NotEqual(first.WorkDigest, second.WorkDigest);
        await AssertStaleReviewAsync(fixture, owner, path, first);

        await RecordConsentWithoutCallingLicensingAsync(fixture, owner, path, second);
        Assert.False(replacement.Escalate(owner.UserId, clock.Now).IsError);
        await context.SaveChangesAsync();
        var fresh = await ReadCountedReviewAsync(owner, path);
        Assert.NotEqual(second.WorkDigest, fresh.WorkDigest);
        await AssertStaleReviewAsync(fixture, owner, path, second);
        using var saved = await owner.Client.PostAsJsonAsync(path, fresh);
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
        Assert.Equal(1L, await fixture.Trial.CountAsync("operational_closure_targets"));
    }

    private static async Task<ShopClosureConfirmationV1> ReadCountedReviewAsync(Merchant owner, string path)
    {
        var review = await owner.Client.GetFromJsonAsync<JsonElement>(path + "-preview");
        Assert.Equal(2, review.GetProperty("unresolvedCases").GetInt32());
        Assert.Equal(1, review.GetProperty("incompleteIntakes").GetInt32());
        Assert.Equal(0, review.GetProperty("unreadableIntakes").GetInt32());
        return ConfirmShopCommand(review);
    }

    private static async Task AssertStaleReviewAsync(P0213Fixture fixture, Merchant owner, string path, ShopClosureConfirmationV1 input)
    {
        using var stale = await owner.Client.PostAsJsonAsync(path, input);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("Closure.ReviewChanged", await P0206Fixture.CodeAsync(stale));
        Assert.Equal(0L, await fixture.Trial.CountAsync("operational_closure_targets"));
    }

    private static async Task RecordConsentWithoutCallingLicensingAsync(P0213Fixture fixture, Merchant owner, string path,
        ShopClosureConfirmationV1 input)
    {
        fixture.Transport.BeforeDispatch = (request, _) =>
            request.RequestUri?.AbsolutePath == ShopClosuresV1.ShopPath(owner.ShopId)
                ? Task.FromException(new HttpRequestException("Fixture delegation outage.")) : Task.CompletedTask;
        using var unavailable = await owner.Client.PostAsJsonAsync(path, input);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.NotNull(await ReadConsentRowsAsync(fixture, owner));
        fixture.Transport.BeforeDispatch = null;
    }

    private static ReconciliationIssue NewCase(Merchant owner, string key) =>
        ReconciliationIssue.Create(owner.ShopId, null, key, "fixture-device", ReconciliationIssueType.ArithmeticConflict,
            "Fixture.UnresolvedWork", "Fixture reconciliation case", owner.UserId);

    private static async Task SeedIntakeReceiptsAsync(P0213Fixture fixture, Merchant owner, DateTimeOffset now)
    {
        await using var context = MerchantDb(fixture.Hosts.Database);
        await context.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO public.operation_receipts
                (id, shop_id, operation_type, operation_key, request_hash, hash_version, actor_user_id, target_id,
                 status, response_version, response_body, created_at, completed_at)
            SELECT gen_random_uuid(), {{owner.ShopId}}, entry.kind, entry.key, repeat('a', 64), 1, {{owner.UserId}},
                gen_random_uuid(), 2, 1, entry.body::jsonb, {{now}}, {{now}}
            FROM (VALUES (3, 'partial', '{"rowCount":2}'), (4, '7:partial:row-a', '{}'),
                (3, 'complete📦', '{"rowCount":2}'), (4, '10:complete📦:row-a', '{}'), (4, '10:complete📦:row-b', '{}')
            ) AS entry(kind, key, body)
            """);
    }
}
