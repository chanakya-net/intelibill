using System.Net;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0308Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0309Tests
{
    [Fact]
    public async Task ManualRetryKeepsOriginalReceiptAndStartsAfterSecurityRelease()
    {
        await using var fixture = await StartAsync(database, March31, P0306Tests.Settings);
        var package = await fixture.PackageAsync(1);
        var quote = await AcceptAsync(package, await FirstQuoteAsync(package, "Month"));
        var command = P0306Tests.Receipt(quote, March31);
        using var billing = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, $"""
            UPDATE public.entitlement_account_heads SET revision = revision + 1, restriction = 'Security'
            WHERE account_id = '{package.Account:D}'
            """);
        using var confirmed = await P0212Fixture.PostAsync(billing, P0306Tests.Path(quote), command);
        Assert.True(confirmed.StatusCode == HttpStatusCode.Created, await confirmed.Content.ReadAsStringAsync());
        var receipt = await confirmed.Content.ReadAsStringAsync();
        Assert.Contains("RestorationPending", receipt, StringComparison.Ordinal);
        P0302Fixture.AssertStatus(await P0302Fixture.StatusAsync(package.Payer.Client, quote),
            "Verified", "NeedsReview", "NotApplicable");

        fixture.At(March31.AddDays(3));
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, $"""
            UPDATE public.entitlement_account_heads SET revision = revision + 1, released_revision = revision,
                restriction = 'Unrestricted' WHERE account_id = '{package.Account:D}'
            """);
        using var replay = await P0212Fixture.PostAsync(billing, P0306Tests.Path(quote), command);
        Assert.True(replay.StatusCode == HttpStatusCode.OK, await replay.Content.ReadAsStringAsync());
        Assert.Equal(receipt, await replay.Content.ReadAsStringAsync());
        var status = await P0302Fixture.StatusAsync(package.Payer.Client, quote);
        P0302Fixture.AssertStatus(status, "Verified", "Fulfilled", "Applying");
        Assert.Equal(March31.AddDays(3), status.GetProperty("termStartsAt").GetDateTimeOffset());
        Assert.Equal(1L, await fixture.CountAsync("manual_payment_receipts"));
        Assert.Equal(1L, await fixture.CountAsync("payment_fulfilments"));
        Assert.Equal(0L, await fixture.CountAsync("verified_payments"));
    }
}
