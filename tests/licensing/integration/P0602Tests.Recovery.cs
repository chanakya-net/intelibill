using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0602Tests
{
    [Fact]
    public async Task LostMerchantReplyAndLateProofRecoverOriginalDecision()
    {
        await using var fixture = await StartAsync(database, January31);
        var (package, term, path, request) = await PreviewAsync(fixture);
        using var response = await package.Payer.Client.PostAsJsonAsync(path, request);
        response.EnsureSuccessStatusCode();
        var order = await AcceptAsync(package, (await response.Content.ReadFromJsonAsync<CheckoutQuoteV1>())!);
        var delivery = fixture.Checkout.Delivery;
        await delivery.ScanAsync(true);
        await delivery.DrainAsync(true);
        await delivery.DrainAsync(false);
        await PayAsync(package, order);
        await delivery.DrainAsync(true);
        await delivery.DrainAsync(false);
        delivery.Transport.AfterCommit = (r, reply, _) =>
        {
            Assert.Equal(CapacityReservationV1.ConsumePath, r.RequestUri!.AbsolutePath);
            Assert.Equal(HttpStatusCode.NoContent, reply.StatusCode);
            throw new IOException("Fixture discarded response after merchant commit");
        };
        Assert.True(await delivery.DeliverAsync(true));
        delivery.Transport.AfterCommit = null;
        fixture.At(February14.AddMinutes(6));
        for (var i = 1; i < 12; i++)
        {
            delivery.Transport.LeaseClock.Now += TimeSpan.FromMinutes(6);
            Assert.True(await delivery.DeliverAsync(true));
        }
        var review = await P0302Fixture.StatusAsync(package.Payer.Client, order);
        P0302Fixture.AssertStatus(review, "Verified", "NeedsReview", "NotApplicable");
        Assert.False(review.GetProperty("canStart").GetBoolean());
        using var second = await package.Payer.Client.PostAsJsonAsync(P0302Fixture.AttemptsPath(order), P0302Fixture.Start(order));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        await delivery.DrainAsync(false);
        var applied = await P0302Fixture.StatusAsync(package.Payer.Client, order);
        P0302Fixture.AssertStatus(applied, "Verified", "Fulfilled", "Applied");
        Assert.Equal(2L, await delivery.MerchantScalarAsync($"SELECT count(*) FROM capacity_reservation_receipts WHERE order_id='{order.OrderId}'"));
        Assert.Equal(2L, await delivery.MerchantScalarAsync($"SELECT count(*) FROM applied_shop_capacity WHERE term_id='{term}'"));
        var original = (byte[])(await delivery.SourceScalarAsync("SELECT payload FROM entitlement_messages WHERE kind='CapacityConsume'"))!;
        await ReplayAsync(delivery, original, HttpStatusCode.NoContent);
        var command = JsonSerializer.Deserialize<CapacityReservationV1>(original, EntitlementSnapshotsV1.Json)!;
        var changed = command with { Capacity = command.Capacity + 1 };
        await ReplayAsync(delivery, JsonSerializer.SerializeToUtf8Bytes(changed, EntitlementSnapshotsV1.Json), HttpStatusCode.Conflict);
        await AssertSupersededReservationAsync(delivery, command);
        Assert.Equal(1L, await fixture.CountAsync("capacity_increases", "state='Applied'"));
        Assert.Equal(1L, await fixture.CountAsync("verified_payments", $"order_id='{order.OrderId}'"));
        Assert.Equal(February29, (await AccountTermAsync(package, term)).GetProperty("paidThrough").GetDateTimeOffset());
    }

    private static async Task AssertSupersededReservationAsync(P0213Fixture delivery, CapacityReservationV1 consumed)
    {
        // Delayed, independently signed, superseded reserve. Its lease is still open but revision zero has been used.
        var stale = consumed with { Phase = CapacityReservationV1.Reserve, EventId = Guid.NewGuid(), OrderId = Guid.NewGuid(),
            ReservationDigest = null, ExpiresAt = February14.AddMinutes(8) };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(stale, EntitlementSnapshotsV1.Json);
        using var signed = await delivery.Transport.SignedAsync(bytes, delivery.AuthClock, path: CapacityReservationV1.ReservePath);
        using var reply = await delivery.MerchantApi.SendAsync(signed);
        Assert.Equal(HttpStatusCode.NoContent, reply.StatusCode);
        var receipt = (byte[])(await delivery.MerchantScalarAsync($"SELECT payload FROM entitlement_messages WHERE event_id='{stale.EventId}'"))!;
        var decision = JsonSerializer.Deserialize<CapacityReceiptV1>(receipt, EntitlementSnapshotsV1.Json)!;
        Assert.Equal("NeedsReview", decision.Outcome);
        Assert.Equal("SourceChanged", decision.Reason);
    }

    private static async Task ReplayAsync(P0213Fixture delivery, byte[] bytes, HttpStatusCode expected)
    {
        using var signed = await delivery.Transport.SignedAsync(bytes, delivery.AuthClock, path: CapacityReservationV1.ConsumePath);
        using var result = await delivery.MerchantApi.SendAsync(signed);
        Assert.Equal(expected, result.StatusCode);
    }
}
