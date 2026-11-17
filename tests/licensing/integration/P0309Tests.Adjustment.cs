using System.Globalization;
using System.Net;
using System.Text.Json;
using Intelibill.Licensing.Application.Payments;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0308Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0309Tests
{
    [Fact]
    public async Task DelayedOnTimePaymentKeepsAcceptedTermAndSeparateRestorationAdjustment()
    {
        await using var fixture = await StartAsync(database, March31);
        var package = await fixture.PackageAsync(1);
        var (_, first) = await PurchaseAsync(package, "Month");
        fixture.At(April30.AddDays(3));
        var quote = await AcceptAsync(package, await RenewalQuoteAsync(package, first, "Month"));
        var attempt = await StartPaymentAsync(package, quote);
        await P0302Fixture.CaptureAsync(package.Payer.Client, quote, attempt);

        fixture.At(May31.AddDays(10));
        var paid = await P0302Fixture.VerifyAsync(package.Payer.Client, quote, attempt);
        P0302Fixture.AssertStatus(paid, "Verified", "Fulfilled", "Applying");
        Assert.Equal(April30, paid.GetProperty("termStartsAt").GetDateTimeOffset());
        Assert.Equal(May31, paid.GetProperty("termEndsAt").GetDateTimeOffset());
        Assert.False(paid.TryGetProperty("serviceRestoration", out _));
        var term = paid.GetProperty("termId").GetGuid();
        var original = await TermRowAsync(fixture, term);

        var adjustment = await ApproveAdjustmentAsync(fixture, package, quote.OrderId, May31.AddDays(10));
        var after = await P0302Fixture.StatusAsync(package.Payer.Client, quote);
        var disclosed = after.GetProperty("serviceRestoration");
        Assert.Equal(1, disclosed.GetProperty("count").GetInt32());
        Assert.Equal(adjustment.GetProperty("grantId").GetGuid(), disclosed.GetProperty("grantId").GetGuid());
        Assert.Equal(May31.AddDays(11), disclosed.GetProperty("endsAt").GetDateTimeOffset());
        Assert.Equal(original, await TermRowAsync(fixture, term));
        Assert.Equal(1L, await fixture.CountAsync("payment_fulfilments", $"order_id = '{quote.OrderId:D}'"));
        Assert.Equal(1L, await fixture.CountAsync("service_restoration_adjustments", $"order_id = '{quote.OrderId:D}'"));
    }

    [Theory]
    [InlineData("Closed", PaymentRestorationState.Closed)]
    [InlineData("Unknown", PaymentRestorationState.EvidenceUnavailable)]
    public void SuppliedClosureOrUnknownEvidenceDoesNotEstablishReadiness(string restriction, PaymentRestorationState state)
    {
        var source = new PaymentRestorationEvidence(March31, new AccountRestrictionSnapshotV1(2, March31, restriction, null));
        Assert.Equal(state, source.State);
        Assert.False(source.Resolves(new(null, Guid.NewGuid(), March31, March31, 1, "Trading")));
    }

    internal static async Task<JsonElement> ApproveAdjustmentAsync(P0308Fixture fixture, Package package, Guid order, DateTimeOffset at)
    {
        var manager = fixture.Checkout.Trial.CatalogueManager!;
        var draft = new
        {
            shopIds = new[] { package.Payer.ShopId }, startsAt = at.ToString("O", CultureInfo.InvariantCulture),
            endsAt = at.AddDays(1).ToString("O", CultureInfo.InvariantCulture), memberAllowance = 4,
            reason = "Verified service interruption", paymentOrderId = order,
        };
        var preview = await P0212Fixture.PreviewAsync(manager, package.Account, draft);
        Assert.Equal(order, preview.GetProperty("paymentOrderId").GetGuid());
        var command = new
        {
            operationKey = Guid.NewGuid(), draft.shopIds, draft.startsAt, draft.endsAt, draft.memberAllowance, draft.reason,
            draft.paymentOrderId, expectedVersion = preview.GetProperty("expectedVersion").GetInt64(),
            sourceHash = preview.GetProperty("sourceHash").GetString(), confirmed = true,
        };
        var path = P0212Fixture.Base(package.Account);
        using var response = await P0212Fixture.PostAsync(manager, path, command);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await P0206Fixture.BodyAsync(response);
        using var replay = await P0212Fixture.PostAsync(manager, path, command);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(body.GetRawText(), (await P0206Fixture.BodyAsync(replay)).GetRawText());
        return body;
    }
}
