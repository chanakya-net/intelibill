using System.Net;

namespace Intelibill.Integration.Tests;

public sealed partial class P0411LicensingTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Final_seat_serializes_with_a_lower_allowance_revision(bool deliveryFirst)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var merchant = await MerchantAsync(client, allowance: 3, active: 1);
        var invitee = await EnrollAsync(client, "final-seat");
        var offer = await InviteAsync(host, client, merchant, invitee.Email);
        var binding = merchant.Binding;

        await using var holder = await RowHold.BeginAsync(Migrator,
            $"SELECT public.take_merchant_licence_fence('{binding.Account:D}'::uuid, '{binding.Shop:D}'::uuid, false)");
        var delivery = deliveryFirst ? LowerAsync() : null;
        var admission = deliveryFirst ? null : AcceptAsync(client, invitee, offer);
        await WaitForBlockedByAsync(delivery ?? (Task)admission!, holder);
        delivery ??= LowerAsync();
        admission ??= AcceptAsync(client, invitee, offer);
        await WaitForLockWaitersAsync(2);
        Assert.Equal(2, await CountActiveAsync(merchant.Shop));
        await holder.ReleaseAsync();

        await delivery;
        using var response = await admission;
        if (deliveryFirst)
        {
            await AssertProblemAsync(response, HttpStatusCode.Conflict, CapacityCode);
            Assert.Null(await AcceptedAtAsync(offer.Id));
        }
        else
        {
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
            Assert.NotNull(await AcceptedAtAsync(offer.Id));
        }

        Assert.Equal(deliveryFirst ? 2 : 3, await CountActiveAsync(merchant.Shop));

        Task LowerAsync() => ApplyAsync(binding, _clock.Now.AddDays(30), revision: 2, allowance: 2);
    }
}
