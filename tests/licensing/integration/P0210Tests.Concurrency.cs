using System.Net;
using System.Net.Http.Json;
using Intelibill.Domain.Enums;
using Intelibill.Shared.Contracts.V1;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0210Tests
{
    [Fact]
    public async Task ConcurrentBranchStartsCannotOverbookTheAccount()
    {
        await using var fixture = await P0210Fixture.StartAsync(database);
        var first = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var second = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var account = await CreateAccountAsync(first.Client);
        await fixture.LinkAsync(account, first.UserId, first);
        await fixture.LinkAsync(account, first.UserId, second);
        using var left = fixture.Hosts.Licensing.CreateClient();
        using var right = fixture.Hosts.Licensing.CreateClient();
        using var leftRequest = fixture.Delivery(Consent(fixture, first));
        using var rightRequest = fixture.Delivery(Consent(fixture, second));

        await using var fence = new NpgsqlConnection(fixture.LicensingOwner.ConnectionString);
        await fence.OpenAsync();
        await using var tx = await fence.BeginTransactionAsync();
        await using var takeLock = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended($1, 0))", fence, tx);
        takeLock.Parameters.AddWithValue("b" + account.ToString("D"));
        await takeLock.ExecuteNonQueryAsync();
        var requests = new[] { left.SendAsync(leftRequest), right.SendAsync(rightRequest) };
        await WaitForIndependentLockWaitersAsync(fixture, fence.ProcessID);
        await tx.CommitAsync();
        var responses = await Task.WhenAll(requests);
        var results = await Task.WhenAll(responses.Select(ReadDeliveryAsync));
        Assert.Single(results, result => result.Status == "Applied");
        Assert.Single(results, result => result.Status == "NeedsReview" && result.Reason == "AccountCap");
        Assert.Equal(1L, await fixture.CountAsync("trial_history"));
        Assert.Equal(1L, await fixture.CountAsync("subscription_coverage_terms"));

        var refused = results[0].Status == "NeedsReview" ? first : second;
        fixture.Hosts.StatusClock.Now = P0210Fixture.Start.AddDays(14);
        using var delayed = fixture.Delivery(Consent(fixture, refused) with { AcceptedAt = P0210Fixture.Start.AddDays(1) });
        Assert.Equal("AccountCap", (await ReadDeliveryAsync(await left.SendAsync(delayed))).Reason);
        using var next = fixture.Delivery(Consent(fixture, refused) with { AcceptedAt = fixture.Hosts.StatusClock.Now });
        Assert.Equal("Applied", (await ReadDeliveryAsync(await left.SendAsync(next))).Status);
        Assert.Equal(2L, await fixture.CountAsync("trial_history"));
    }

    private static async Task WaitForIndependentLockWaitersAsync(P0210Fixture fixture, int blocker)
    {
        await using var observer = new NpgsqlConnection(fixture.LicensingOwner.ConnectionString);
        await observer.OpenAsync();
        await using var count = new NpgsqlCommand("""
            SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event = 'advisory'
                AND ($1 = ANY(pg_blocking_pids(pid)) OR EXISTS (
                    SELECT FROM unnest(pg_blocking_pids(pid)) AS previous WHERE $1 = ANY(pg_blocking_pids(previous))))
            """, observer);
        count.Parameters.AddWithValue(blocker);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while ((long)(await count.ExecuteScalarAsync(timeout.Token))! != 2L)
            await Task.Delay(15, timeout.Token);
    }

    private static ShopTrialConsentV1 Consent(P0210Fixture fixture, Merchant owner) =>
        new(Guid.NewGuid(), owner.ShopId, owner.UserId, Guid.NewGuid(), 0, 1, P0210Fixture.Start, fixture.Accept());

    private static async Task<ShopTrialDeliveryResultV1> ReadDeliveryAsync(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<ShopTrialDeliveryResultV1>())!;
        }
    }
}
