using System.Net;
using System.Net.Http.Json;
using Intelibill.Licensing.Application.Payments;
using Intelibill.Shared.Contracts.V1;
using Xunit.Abstractions;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0306Tests(LicensingPostgreSqlFixture database, ITestOutputHelper output) : IClassFixture<LicensingPostgreSqlFixture>
{
    [Fact]
    public async Task FullManualReceiptFulfilsAllNamedShopsOnce()
    {
        var now = DateTimeOffset.Parse("2027-03-10T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        await using var fixture = await P0302Fixture.StartAsync(database, new P0201Clock { Now = now }, Settings);
        var purchase = await fixture.AcceptedQuoteAsync();
        using var operatorSession = await SignInBillingAsync(fixture);
        var path = Path(purchase.Quote);
        using var response = await operatorSession.SendAsync(HttpMethod.Get, path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var view = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(3, view.GetProperty("order").GetProperty("shops").GetArrayLength());

        var command = Receipt(purchase.Quote, now);
        using var preview = await P0212Fixture.PostAsync(operatorSession, path + "/preview", command);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        using var created = await P0212Fixture.PostAsync(operatorSession, path, command);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var replay = await P0212Fixture.PostAsync(operatorSession, path, command);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(await created.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        Assert.Equal(1L, await fixture.CountAsync("manual_payment_receipts"));
        Assert.Equal(0L, await fixture.CountAsync("payment_attempts"));
        Assert.Equal(1L, await fixture.CountAsync("payment_fulfilments"));
        Assert.Equal(3L, await fixture.CountAsync("payment_fulfilment_shops"));
        P0302Fixture.AssertStatus(await P0302Fixture.StatusAsync(purchase.Payer.Client, purchase.Quote),
            "Verified", "Fulfilled", "Applying");
        await fixture.Delivery.DrainAsync(source: true);
        await fixture.Delivery.DrainAsync(source: false);
        P0302Fixture.AssertStatus(await P0302Fixture.StatusAsync(purchase.Payer.Client, purchase.Quote),
            "Verified", "Fulfilled", "Applied");
    }

    internal static readonly Dictionary<string, string?> Settings = new(P0302Fixture.SimulatorSettings)
    {
        ["ManualPayments:Enabled"] = "true",
        ["ManualPayments:ReceivingAccountId"] = "fixture-bank",
        ["ManualPayments:Label"] = "Local fixture receiving account",
        ["ManualPayments:Environment"] = "Test",
        ["ManualPayments:ConfigurationVersion"] = "fixture/1",
        ["ManualPayments:EvidencePolicyVersion"] = "fixture/1",
        ["ManualPayments:ReferencePattern"] = "BANK-[A-F0-9]{32}",
        ["ManualPayments:EvidencePattern"] = "STATEMENT-[A-Z0-9-]+",
        ["ManualPayments:ReferenceKey"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
    };

    internal static string Path(CheckoutQuoteV1 quote) => $"/api/admin/accounts/{quote.AccountId:D}/orders/{quote.OrderId:D}/manual-payments";

    internal static ConfirmManualPayment Receipt(CheckoutQuoteV1 quote, DateTimeOffset now) =>
        new(Guid.NewGuid(), quote.Revision, quote.SnapshotHash, "fixture-bank", "fixture/1",
            $"BANK-{Guid.NewGuid():N}", "STATEMENT-LOCAL-TEST", now, quote.AmountPaise, quote.Currency,
            "Reconciled against local fixture statement", true);

    internal static async Task<P0110CookieBrowser> SignInBillingAsync(P0302Fixture fixture)
    {
        var seed = await EnrollBillingAsync(fixture, BillingEmail);
        return await fixture.Trial.Catalogue.SignInOperatorAsync(seed, BillingEmail);
    }

    internal const string BillingEmail = "manual.billing@example.test";

    internal static async Task<string> EnrollBillingAsync(P0302Fixture fixture, string email)
    {
        using var owner = await fixture.Trial.Catalogue.SignInOwnerAsync();
        var seed = await fixture.Trial.Catalogue.EnrollOperatorAsync(owner, email);
        await fixture.Trial.Catalogue.GrantAsync(email, "billing");
        return seed;
    }
}
