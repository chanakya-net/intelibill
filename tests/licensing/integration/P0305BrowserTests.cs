using System.Net;
using Xunit.Abstractions;

namespace Intelibill.Licensing.Integration.Tests;

public sealed class P0305BrowserTests(LicensingPostgreSqlFixture database, ITestOutputHelper output)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    private const string Configuration = "../../tests/licensing/browser/playwright.p0305.config.ts";
    private static readonly string[] Phases = ["webhook-reload"];

    [P0305BrowserFact]
    [Trait("Category", "Browser")]
    public async Task WebhookVerifiedPaymentAppearsAfterReloadWithoutBrowserVerification()
    {
        await using var session = await P0304BrowserTests.Session.StartAsync(database, evidenceVariable: "P0305_EVIDENCE_DIR",
            extraSettings: new Dictionary<string, string?>
            {
                ["Payments:Razorpay:WebhookSecret"] = P0305Tests.Secret,
                ["Payments:Razorpay:WebhookAccountId"] = P0305Tests.Account
            });
        var (fixture, fake, run) = (session.Fixture, session.Fake, session.Run);
        P0304BrowserTests.Flags.Clear(run.Evidence, "webhook-opened", "webhook-authorized", "webhook-pending",
            "webhook-verified", "webhook-applying", "webhook-applied");
        var browser = P0304BrowserTests.RunAsync(output, run, session.Origin, Phases[0], Configuration, session.Timeout);

        await P0304BrowserTests.Flags.WaitAsync(run.Evidence, "webhook-opened", browser, session.Timeout);
        var order = fake.OrderId();
        var payment = fake.AddPayment(order, "authorized");
        P0304BrowserTests.Flags.Raise(run.Evidence, "webhook-authorized");

        await P0304BrowserTests.Flags.WaitAsync(run.Evidence, "webhook-pending", browser, session.Timeout);
        Assert.Equal(0L, await fixture.CountAsync("verified_payments"));
        fake.Transition(payment, "captured");
        using var client = fixture.Hosts.Licensing.CreateClient();
        using var receipt = await P0305Tests.SendAsync(client,
            P0305Tests.Body(run.Quote.AmountPaise, fixture.CheckoutClock.Now, order, payment), "browser_capture");
        Assert.Equal(HttpStatusCode.NoContent, receipt.StatusCode);
        await P0305Tests.DrainAsync(fixture.Hosts.Licensing.Services, fixture.CheckoutClock);
        await P0302Tests.AssertSingleFulfilmentAsync(fixture, run.Quote, 1);
        P0304BrowserTests.Flags.Raise(run.Evidence, "webhook-verified");

        await P0304BrowserTests.Flags.WaitAsync(run.Evidence, "webhook-applying", browser, session.Timeout);
        await fixture.Delivery.DrainAsync(source: true);
        await fixture.Delivery.DrainAsync(source: false);
        P0304BrowserTests.Flags.Raise(run.Evidence, "webhook-applied");

        await browser;
        Assert.Equal(1, fake.Posts);
        await P0304BrowserTests.RecordAsync(fixture, run, fake, "webhook-build-and-postgres.json", Phases,
            session.Timeout, Configuration);
    }
}

internal sealed class P0305BrowserFactAttribute : FactAttribute
{
    public P0305BrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("P0305_RUN_BROWSER") != "1")
            Skip = "Set P0305_RUN_BROWSER=1 after building the shipping merchant application.";
    }
}
