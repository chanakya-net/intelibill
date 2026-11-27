using Intelibill.Licensing.Application.Payments;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using static Intelibill.Licensing.Integration.Tests.P0609Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0610Tests
{
    [Theory]
    [InlineData("Processed", "Confirmed")]
    [InlineData("Failed", "Failed")]
    public async Task LateReadSettlesOnlyTheOriginalReservation(string control, string expected)
    {
        await using var fixture = await P0609Tests.StartAsync(database, Instant);
        var (package, request, _, _) = await RequestedAsync(fixture, 6000);
        using var billing = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        var path = RequestPath(package.Account, request);
        var at = Instant.AddMinutes(2);
        var review = await PreviewAsync(billing, package.Account, request, 4500, "EndAt", at);
        await ReadAsync(await ApproveAsync(billing, package.Account, request, Command(review, 4500, "EndAt", at)),
            HttpStatusCode.Created);
        using var setup = await P0212Fixture.PostAsync(billing, path + "/simulator", new { outcome = "Timeout" });
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        await PumpRefundsAsync(fixture);
        var first = await MerchantOutcomeAsync(package);
        Assert.Equal("Unknown", first.GetProperty("status").GetString());
        Assert.Equal(0L, await fixture.CountAsync("refund_applications"));

        fixture.At(at.AddMinutes(1));
        using var update = await P0212Fixture.PostAsync(billing, path + "/simulator", new { outcome = control });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        await Task.WhenAll(PumpRefundsAsync(fixture), PumpRefundsAsync(fixture));
        await PumpRefundsAsync(fixture);
        var final = await MerchantOutcomeAsync(package);
        Assert.Equal(expected, final.GetProperty("status").GetString());
        Assert.Equal(6000L, final.GetProperty("amountPaise").GetInt64());
        Assert.Equal(4500L, final.GetProperty("approvedAmountPaise").GetInt64());
        Assert.Equal(1L, await fixture.CountAsync("development_gateway_refunds"));
        Assert.Equal(1L, await fixture.CountAsync("refund_sends"));
        Assert.Equal(expected == "Confirmed" ? 1L : 0L, await fixture.CountAsync("refund_applications"));
        var detail = Assert.Single((await ReadAsync(await billing.SendAsync(HttpMethod.Get, AdminPath(package.Account)),
            HttpStatusCode.OK)).GetProperty("items").EnumerateArray());
        Assert.StartsWith("dev_refund_", detail.GetProperty("refundReference").GetString(), StringComparison.Ordinal);
        Assert.Equal("OriginalProviderRead", detail.GetProperty("observationReason").GetString());
        Assert.Equal(at.AddMinutes(1), detail.GetProperty("lastCheckedAt").GetDateTimeOffset());
        Assert.False(final.TryGetProperty("refundReference", out _));
        var term = await P0308Fixture.ShopTermAsync(package, package.Shops[0]);
        Assert.Equal(expected == "Confirmed" ? "Expired" : "Active", term.GetProperty("state").GetString());
    }

    private static async Task<System.Text.Json.JsonElement> MerchantOutcomeAsync(P0308Fixture.Package package) =>
        Assert.Single((await ReadAsync(await package.Payer.Client.GetAsync(P0608Tests.RefundsPath(package.Account)),
            HttpStatusCode.OK)).GetProperty("items").EnumerateArray());

    internal static async Task PumpRefundsAsync(P0308Fixture fixture)
    {
        await using var scope = fixture.Hosts.Licensing.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IRefundReconciliation>().RunOneAsync(default);
    }
}
