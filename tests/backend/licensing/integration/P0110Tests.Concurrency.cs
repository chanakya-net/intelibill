using System.Net;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0110Tests
{
    [Fact]
    public async Task ConcurrentRefreshesOfOneCredentialAcrossReplicasLeaveNoLiveSuccessor()
    {
        await using var signed = await SignInAsync();
        await using var replica = new LicensingApiFactory(signed.Runtime, clock: signed.Clock, settings: signed.Settings);
        using var second = signed.Browser.Fork(RawClient(replica));
        var secondCsrf = await ReadCsrfAsync(second);

        var responses = await Task.WhenAll(RefreshAsync(signed.Browser, signed.Csrf), RefreshAsync(second, secondCsrf));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Unauthorized);
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }

        var winner = responses[0].StatusCode == HttpStatusCode.OK ? signed.Browser : second;
        await AssertStatusAsync(winner, Session, HttpStatusCode.Unauthorized);
        using var successor = await RefreshAsync(winner, winner == second ? secondCsrf : signed.Csrf);
        Assert.Equal(HttpStatusCode.Unauthorized, successor.StatusCode);
        Assert.Equal(1L, await CountAsync(signed, "admin_sessions"));
        Assert.Equal(0L, await CountAsync(signed, "admin_sessions WHERE revoked_at IS NULL"));
        Assert.Equal(2L, await CountAsync(signed, "admin_refresh_credentials"));
        Assert.Equal(1L, await CountAsync(signed, "admin_refresh_credentials WHERE parent_id IS NOT NULL"));
        Assert.Equal(1L, await CountAsync(signed, $"provider_audit_events WHERE action = {ReuseRevokedAction}"));
    }

    [Fact]
    public async Task ManyConcurrentPresentationsProduceAtMostOneSuccessor()
    {
        await using var signed = await SignInAsync();
        await using var replica = new LicensingApiFactory(signed.Runtime, clock: signed.Clock, settings: signed.Settings);
        var holders = new List<(P0110CookieBrowser Browser, string Csrf)>();
        try
        {
            for (var index = 0; index < 6; index++)
            {
                var holder = signed.Browser.Fork(RawClient(index % 2 == 0 ? signed.Api : replica));
                holders.Add((holder, await ReadCsrfAsync(holder)));
            }

            var responses = await Task.WhenAll(holders.Select(holder => RefreshAsync(holder.Browser, holder.Csrf)));
            var statuses = responses.Select(response => response.StatusCode).ToArray();
            foreach (var response in responses) response.Dispose();
            Assert.Equal(1, statuses.Count(status => status == HttpStatusCode.OK));
            Assert.All(statuses, status => Assert.Contains(status, new[] { HttpStatusCode.OK, HttpStatusCode.Unauthorized }));
        }
        finally
        {
            foreach (var holder in holders) holder.Browser.Dispose();
        }

        Assert.Equal(2L, await CountAsync(signed, "admin_refresh_credentials"));
        Assert.Equal(0L, await CountAsync(signed, "admin_sessions WHERE revoked_at IS NULL"));
    }
}
