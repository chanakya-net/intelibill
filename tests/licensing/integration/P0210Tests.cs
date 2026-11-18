using System.Net;
using System.Net.Http.Json;
using Intelibill.Domain.Enums;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0210Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    [Fact]
    public async Task ExplicitOwnerAcceptanceStartsOneTrialAndSetupDoesNot()
    {
        await using var fixture = await P0210Fixture.StartAsync(database);
        var owner = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var staff = await SignInAsync(fixture.Hosts, ShopRole.Staff, owner.ShopId);
        var payer = await SignInAsync(fixture.Hosts);
        var account = await CreateAccountAsync(payer.Client);
        await fixture.LinkAsync(account, payer.UserId, owner);
        var path = Path(owner.ShopId);

        var pending = await ReadAsync(await ShopStatusAsync(owner.Client, owner.ShopId));
        Assert.Equal("PendingActivation", pending.GetProperty("state").GetString());
        var preview = await owner.Client.GetFromJsonAsync<ShopTrialPreviewV1>(path);
        Assert.True(preview!.Eligible);
        Assert.Equal(P0210Fixture.Start.AddDays(14), preview.ProspectiveEnd);
        Assert.Equal(2, preview.ActiveMembers);
        Assert.Equal(0L, await fixture.CountAsync("trial_history"));

        var request = fixture.Accept();
        using var denied = await staff.Client.PostAsJsonAsync(path, request);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var payerDenied = await payer.Client.PostAsJsonAsync(path, request);
        Assert.Equal(HttpStatusCode.Forbidden, payerDenied.StatusCode);
        using var unconfirmed = await owner.Client.PostAsJsonAsync(path, request with { TermsAccepted = false });
        Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.StatusCode);
        Assert.Equal(0L, await fixture.CountAsync("trial_history"));

        var applied = await StartAndWaitAsync(owner, request);
        Assert.Equal("Applied", applied.Status);
        Assert.Equal(P0210Fixture.Start, applied.AcceptedAt);
        Assert.Equal(preview.ProspectiveEnd, applied.EndsAt);
        Assert.Equal(1L, await fixture.CountAsync("trial_history"));
        Assert.Equal(1L, await fixture.CountAsync("subscription_coverage_terms"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrUnapprovedPublishedPolicyGrantsNothing(bool publish)
    {
        await using var fixture = await P0210Fixture.StartAsync(database, publish: publish, approve: false);
        var owner = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var account = await CreateAccountAsync(owner.Client);
        await fixture.LinkAsync(account, owner.UserId, owner);

        var preview = await owner.Client.GetFromJsonAsync<ShopTrialPreviewV1>(Path(owner.ShopId));
        Assert.False(preview!.Eligible);
        Assert.Null(preview.Policy);
        var refused = await StartAndWaitAsync(owner, fixture.Accept());
        Assert.Equal("NeedsReview", refused.Status);
        Assert.Equal("PolicyUnavailable", refused.Reason);
        Assert.Equal(0L, await fixture.CountAsync("trial_history"));
        Assert.Equal(0L, await fixture.CountAsync("subscription_coverage_terms"));
    }

    internal static Uri Path(Guid shop) => new($"/api/licensing/shops/{shop:D}/trial", UriKind.Relative);

    internal static async Task<ShopTrialOperationV1> StartAndWaitAsync(Merchant owner, ShopTrialAcceptanceV1 request)
    {
        using var post = await owner.Client.PostAsJsonAsync(Path(owner.ShopId), request);
        Assert.True(post.IsSuccessStatusCode, await post.Content.ReadAsStringAsync());
        return await WaitAsync(owner, request.OperationKey);
    }

    internal static async Task<ShopTrialOperationV1> WaitAsync(Merchant owner, Guid operation)
    {
        var path = new Uri($"{Path(owner.ShopId)}/operations/{operation:D}", UriKind.Relative);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (true)
        {
            var result = await owner.Client.GetFromJsonAsync<ShopTrialOperationV1>(path, timeout.Token);
            if (result is { Status: not "Pending" })
                return result;
            await Task.Delay(TimeSpan.FromMilliseconds(200), timeout.Token);
        }
    }
}
