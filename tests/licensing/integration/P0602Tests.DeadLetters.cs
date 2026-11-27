using System.Net.Http.Json;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0602Tests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeadLetteredCommandRecoversMerchantDecision(bool consumed)
    {
        await using var fixture = await StartAsync(database, January31);
        var (package, term, path, request) = await PreviewAsync(fixture);
        var order = await ConvertAsync(package, path, request);
        var delivery = fixture.Checkout.Delivery;
        await delivery.ScanAsync(true);
        await ExchangeAsync(delivery);
        await PayAsync(package, order);
        if (consumed) await ExchangeAsync(delivery);

        await ExhaustDeliveriesAsync(delivery, consumed);
        var evt = (Guid)(await fixture.ScalarAsync($"SELECT coalesce(consume_event,reserve_event) FROM capacity_increases WHERE order_id='{order.OrderId}'"))!;
        var job = $"SELECT status FROM entitlement_outbox WHERE event_id='{evt}'";
        Assert.Equal("DeadLetter", await delivery.SourceScalarAsync(job));
        if (consumed) Assert.Equal("DeadLetter", await delivery.MerchantScalarAsync(job));
        Assert.Equal(consumed ? 2L : 0L, await delivery.MerchantScalarAsync(
            $"SELECT count(*) FROM capacity_reservation_receipts WHERE order_id='{order.OrderId}'"));
        fixture.At(February14.AddMinutes(6));
        delivery.Transport.LeaseClock.Now += TimeSpan.FromMinutes(6);
        await delivery.ScanAsync(true);
        await ExchangeAsync(delivery);
        var filter = $"order_id='{order.OrderId}'";
        var decision = consumed ? "state='Applied' AND reason IS NULL" : "state='NeedsReview' AND reason='ReservationExpired'";
        Assert.Equal(1L, await fixture.CountAsync("capacity_increases", $"{filter} AND {decision}"));
        Assert.Equal(1L, await fixture.CountAsync("verified_payments", filter));
        Assert.Equal(1L, await fixture.CountAsync("payment_confirmations", filter));
        var status = await AccountTermAsync(package, term);
        Assert.Equal(consumed ? 4 : 3, status.GetProperty("purchasedCapacity").GetInt32());
        Assert.Equal(February29, status.GetProperty("paidThrough").GetDateTimeOffset());
        var currentPlan = (Guid)(await fixture.ScalarAsync($"SELECT target_plan_version_id FROM capacity_change_quotes WHERE quote_id='{order.OrderId}'"))!;
        var renewal = await RenewalQuoteAsync(package with { Plan = currentPlan }, term, "Month");
        Assert.Equal(term, renewal.TermId);
        if (consumed)
            Assert.Equal("Fulfilled", (await PayAsync(package, await AcceptAsync(package, renewal))).GetProperty("fulfilment").GetString());
    }

    private static async Task ExhaustDeliveriesAsync(P0213Fixture delivery, bool consumed)
    {
        // Lose the reserve before dispatch, or lose every consume acknowledgement after the merchant committed.
        delivery.Transport.BeforeDispatch = (r, _) =>
            r.RequestUri!.AbsolutePath == (consumed ? CapacityReservationV1.ReceiptPath : CapacityReservationV1.ReservePath)
                ? throw new IOException("Peer unavailable") : Task.CompletedTask;
        for (var i = 0; i < 12; i++)
        {
            Assert.True(await delivery.DeliverAsync(true));
            if (consumed) Assert.True(await delivery.DeliverAsync(false));
            delivery.Transport.LeaseClock.Now += TimeSpan.FromMinutes(6);
        }
        delivery.Transport.BeforeDispatch = null;
    }

    private static async Task ExchangeAsync(P0213Fixture delivery)
    {
        await delivery.DrainAsync(true);
        await delivery.DrainAsync(false);
    }

    private static async Task<CheckoutQuoteV1> ConvertAsync(Package package, string path, object request)
    {
        using var response = await package.Payer.Client.PostAsJsonAsync(path, request);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await AcceptAsync(package, (await response.Content.ReadFromJsonAsync<CheckoutQuoteV1>())!);
    }
}
