using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Intelibill.Domain.Enums;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0210Tests
{
    [Fact]
    public async Task ReplayAndIdentityChangesKeepTheOriginalTrial()
    {
        var transport = new LoseFirstDeliveryResponse();
        await using var fixture = await P0210Fixture.StartAsync(database, transport: inner =>
        {
            transport.InnerHandler = inner;
            return transport;
        });
        var owner = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var account = await CreateAccountAsync(owner.Client);
        await fixture.LinkAsync(account, owner.UserId, owner);
        var operation = fixture.Accept();
        var applied = await StartAndWaitAsync(owner, operation);
        Assert.Equal("Applied", applied.Status);
        Assert.True(transport.Dropped);
        Assert.Equal(1L, await fixture.CountAsync("shop_trial_inbox"));
        Assert.Equal(2L, await fixture.CountAsync("shop_trial_delivery_uses"));
        using var replay = await owner.Client.PostAsJsonAsync(Path(owner.ShopId), operation);
        Assert.Equal(applied, await replay.Content.ReadFromJsonAsync<ShopTrialOperationV1>());
        using var changed = await owner.Client.PostAsJsonAsync(Path(owner.ShopId), operation with { PolicyRevision = operation.PolicyRevision + 1 });
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);

        var successor = await SignInAsync(fixture.Hosts, ShopRole.Owner, owner.ShopId);
        await RevokeOwnerAsync(owner);
        using var revoked = await owner.Client.PostAsJsonAsync(Path(owner.ShopId), operation);
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        var freshIdentity = await ChangeVerifiedEmailAsync(successor);
        var newAccount = await CreateAccountAsync(freshIdentity.Client, "New Evaluation Account");
        Assert.NotEqual(account, newAccount);
        fixture.Hosts.StatusClock.Now = applied.EndsAt;
        var repeat = await StartAndWaitAsync(freshIdentity, fixture.Accept());
        Assert.Equal("AlreadyUsed", repeat.Reason);
        Assert.Equal(1L, await fixture.CountAsync("trial_history"));
        Assert.Equal(account, await LicensingPostgreSqlFixture.ReadScalarAsync(fixture.LicensingOwner,
            $"SELECT account_id FROM public.trial_history WHERE shop_id = '{owner.ShopId:D}'"));
    }

    private async Task<Merchant> ChangeVerifiedEmailAsync(Merchant merchant)
    {
        var email = P0202Fixture.UniqueEmail();
        await using var db = MerchantDb(database);
        var user = await db.Users.SingleAsync(row => row.Id == merchant.UserId);
        user.VerifyEmail(email);
        await db.SaveChangesAsync();
        merchant.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await P0201Tests.LoginAsync(merchant.Client, email));
        return merchant with { Email = email };
    }

    private Task RevokeOwnerAsync(Merchant owner) => LicensingPostgreSqlFixture.ExecuteAsync(
        database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase), $"""
        UPDATE public.shop_memberships SET role = 'Manager', authority_version = authority_version + 1
        WHERE shop_id = '{owner.ShopId:D}' AND user_id = '{owner.UserId:D}'
        """);

    private sealed class LoseFirstDeliveryResponse : DelegatingHandler
    {
        private int _deliveries;
        internal bool Dropped { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = await base.SendAsync(request, ct);
            if (request.RequestUri?.AbsolutePath == ShopTrialsV1.DeliveryPath && response.IsSuccessStatusCode &&
                Interlocked.Increment(ref _deliveries) == 1)
            {
                response.Dispose();
                Dropped = true;
                throw new HttpRequestException("Local fixture dropped one committed acknowledgement.");
            }
            return response;
        }
    }
}
