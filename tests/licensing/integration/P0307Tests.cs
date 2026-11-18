using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0307Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    [Theory]
    [InlineData(-1, "INR", "Underpayment")]
    [InlineData(1, "INR", "Overpayment")]
    [InlineData(0, "USD", "CurrencyMismatch")]
    public async Task CapturedMismatchIsRetainedWithoutAccess(int delta, string currency, string classification)
    {
        await using var rig = await P0304Tests.Rig.StartAsync(database);
        var attempt = await rig.StartAttemptAsync();
        rig.Fake.AddPayment(rig.Fake.OrderId(), "captured", amount: rig.Quote.AmountPaise + delta, currency: currency);

        var status = await rig.VerifyAsync(attempt, null, null);
        Assert.False(status.GetProperty("canStart").GetBoolean());
        Assert.Equal(0L, await rig.Fixture.CountAsync("payment_fulfilments"));
        Assert.Equal(0L, await rig.Fixture.CountAsync("subscription_term_facts"));
        Assert.Equal(0L, await rig.Fixture.CountAsync("verified_payments"));

        using var finance = await P0306Tests.SignInBillingAsync(rig.Fixture);
        using var response = await finance.SendAsync(HttpMethod.Get, ReviewPath(rig.Quote.AccountId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cases = await response.Content.ReadFromJsonAsync<JsonElement>();
        var review = Assert.Single(cases.EnumerateArray());
        Assert.Equal(classification, review.GetProperty("classification").GetString());
        Assert.Equal(rig.Quote.AmountPaise + delta, review.GetProperty("observedAmountPaise").GetInt64());
        Assert.Equal(currency, review.GetProperty("observedCurrency").GetString());
        AssertStatus(status, "PendingVerification", "NotFulfilled", "NotApplicable");
    }

    private static string ReviewPath(Guid accountId) => $"/api/admin/accounts/{accountId:D}/payment-reviews";
}
