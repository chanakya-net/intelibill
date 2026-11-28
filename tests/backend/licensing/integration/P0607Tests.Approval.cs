using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using static Intelibill.Licensing.Integration.Tests.P0607aTests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0607Tests
{
    [Fact]
    public async Task ApproveIsScopedIdempotentAndInitiallyPending()
    {
        using var key = TestRsaKeys.Create(TestRsaKeyPurpose.General);
        await using var s = await StartAsync(database, key);
        var id = await ConsentedAsync(s, s.Permit.ValidUntil);
        var (manager, _) = await s.F.Checkout.Trial.Catalogue.ManagerAsync("transfer.manager@example.test");
        using var session = manager;
        // The two fixture hosts normally use independent identity clocks. A signed live exchange needs one instant.
        s.F.Hosts.Fixture.Clock.Now = s.F.Checkout.Trial.Catalogue.Clock.GetUtcNow();
        var path = $"/api/admin/accounts/{s.Source.Account}/coverage-transfers/{id}";
        var csrf = await P0206Fixture.ProofAsync(manager);
        using var review = await manager.SendAsync(HttpMethod.Post, path + "/preview", new { }, csrf);
        review.EnsureSuccessStatusCode();
        var terms = await P0206Fixture.BodyAsync(review);
        var operation = Guid.NewGuid();
        var cmd = Approval(terms, operation);
        using var approval = await manager.SendAsync(HttpMethod.Post, path + "/approve", cmd, csrf);
        Assert.True(approval.StatusCode == HttpStatusCode.Created, await approval.Content.ReadAsStringAsync());
        var response = await P0206Fixture.BodyAsync(approval);
        Assert.Equal("Preparing", response.GetProperty("state").GetString());
        Assert.Equal(operation, response.GetProperty("operationKey").GetGuid());
        using var replay = await manager.SendAsync(HttpMethod.Post, path + "/approve", cmd, csrf);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        using var recovery = await manager.SendAsync(HttpMethod.Get, path + $"/operations/{operation}");
        Assert.Equal(response.GetRawText(), (await P0206Fixture.BodyAsync(recovery)).GetRawText());
        using var changed = await manager.SendAsync(HttpMethod.Post, path + "/approve",
            Approval(terms, operation, "Changed reason"), csrf);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        using var wrong = await manager.SendAsync(HttpMethod.Get,
            $"/api/admin/accounts/{s.Destination.Account}/coverage-transfers/{id}/operations/{operation}");
        Assert.Equal(HttpStatusCode.NotFound, wrong.StatusCode);
        Assert.Equal(1L, await s.F.CountAsync("coverage_transfer_approvals"));
        s.F.Checkout.Trial.Catalogue.Clock.Advance(TimeSpan.FromMinutes(5));
        using var stale = await manager.SendAsync(HttpMethod.Get, path + $"/operations/{operation}");
        Assert.Equal(HttpStatusCode.Forbidden, stale.StatusCode);
        Assert.Equal("Coverage.ExceptionRecentMfaRequired", (await P0206Fixture.BodyAsync(stale)).GetProperty("code").GetString());
    }

    private static object Approval(JsonElement review, Guid key, string reason = "Confirmed with all three owners") => new
    {
        operationKey = key, reason,
        expectedReviewDigest = review.GetProperty("reviewDigest").GetString(),
        treatment = review.GetProperty("treatment").GetString(),
        cutoverAt = review.GetProperty("cutoverAt").GetDateTimeOffset(),
        paidThroughAt = review.GetProperty("sourcePaidThroughAt").GetDateTimeOffset(),
        acknowledgedAllocation = true, acknowledgedPermitsAndUnknownQueues = true, acknowledgedHistoricalBillsStay = true,
    };
}
