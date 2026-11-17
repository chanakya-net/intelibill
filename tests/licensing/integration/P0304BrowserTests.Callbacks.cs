namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// R010/R014 Chromium phases. A Playwright phase and this test hand control to each other through flag files in the
/// evidence directory, so the fixture provider can change while one browser page stays loaded.
/// </summary>
public sealed partial class P0304BrowserTests
{
    private static readonly string[] ReloadPhases = ["reload-opened"];
    private static readonly string[] MalformedPhases = ["malformed-start", "malformed-callbacks", "malformed-recheck"];
    private static readonly string[] WindowHoldPhases = ["window-hold"];

    /// <summary>
    /// R010: the hosted window is opened, the provider authorizes a payment and the page reloads before the handler or
    /// ondismiss fires. Pay stays hidden before and after the server's check; the same payment's capture fulfils once.
    /// </summary>
    [P0304BrowserFact]
    [Trait("Category", "Browser")]
    public async Task ReloadAfterOpeningTheHostedWindowVerifiesTheOriginalOrderBeforeOfferingPay()
    {
        await using var session = await Session.StartAsync(database);
        var (fixture, fake, run) = (session.Fixture, session.Fake, session.Run);
        Flags.Clear(run.Evidence, "reload-window-opened", "reload-authorized", "reload-pending", "reload-captured");
        var phase = RunAsync(run, session.Origin, "reload-opened", session.Timeout);

        await Flags.WaitAsync(run.Evidence, "reload-window-opened", phase, session.Timeout);
        Assert.Equal(1, fake.Posts);
        var payment = fake.AddPayment(fake.OrderId(), "authorized");
        Flags.Raise(run.Evidence, "reload-authorized");

        await Flags.WaitAsync(run.Evidence, "reload-pending", phase, session.Timeout);
        Assert.Equal(0L, await fixture.CountAsync("verified_payments"));
        Assert.Equal(0L, await fixture.CountAsync("payment_fulfilments"));
        fake.Transition(payment, "captured");
        Flags.Raise(run.Evidence, "reload-captured");

        await phase;
        Assert.Equal(1, fake.Posts);
        await P0302Tests.AssertSingleFulfilmentAsync(fixture, run.Quote, 1);
        await RecordAsync(fixture, run, fake, "reload-build-and-postgres.json", ReloadPhases, session.Timeout);
    }

    /// <summary>
    /// R010: two pages of one browser context. Page A's hosted window stays open while page B's proof-free recheck
    /// finds the order idle; the saved hold survives, the provider then authorizes and A reloads with no callback.
    /// Neither page offers Pay while the window is live or the payment authorized; the capture fulfils once.
    /// </summary>
    [P0304BrowserFact]
    [Trait("Category", "Browser")]
    public async Task IdleRecheckInAnotherPageKeepsAnOpenWindowsHoldUntilTheOriginalOrderSettles()
    {
        await using var session = await Session.StartAsync(database);
        var (fixture, fake, run) = (session.Fixture, session.Fake, session.Run);
        Flags.Clear(run.Evidence, "window-hold-open", "window-hold-authorized", "window-hold-pending", "window-hold-captured");
        var phase = RunAsync(run, session.Origin, "window-hold", session.Timeout);

        await Flags.WaitAsync(run.Evidence, "window-hold-open", phase, session.Timeout);
        Assert.Equal(1, fake.Posts);
        var payment = fake.AddPayment(fake.OrderId(), "authorized");
        Flags.Raise(run.Evidence, "window-hold-authorized");

        await Flags.WaitAsync(run.Evidence, "window-hold-pending", phase, session.Timeout);
        Assert.Equal(0L, await fixture.CountAsync("verified_payments"));
        Assert.Equal(0L, await fixture.CountAsync("payment_fulfilments"));
        fake.Transition(payment, "captured");
        Flags.Raise(run.Evidence, "window-hold-captured");

        await phase;
        Assert.Equal(1, fake.Posts);
        await P0302Tests.AssertSingleFulfilmentAsync(fixture, run.Quote, 1);
        await RecordAsync(fixture, run, fake, "window-hold-build-and-postgres.json", WindowHoldPhases, session.Timeout);
    }

    /// <summary>
    /// R014: with the fixture order already captured, success callbacks without a signature or for another order are
    /// relayed as unverified claims and refused: no verified payment and no fulfilment. Check again then settles once.
    /// </summary>
    [P0304BrowserFact]
    [Trait("Category", "Browser")]
    public async Task MalformedSuccessCallbacksGrantNothingUntilCheckAgainSettlesTheOriginalOrder()
    {
        await using var session = await Session.StartAsync(database);
        var (fixture, fake, run) = (session.Fixture, session.Fake, session.Run);

        await RunAsync(run, session.Origin, "malformed-start", session.Timeout);
        fake.AddPayment(fake.OrderId(), "captured");
        await RunAsync(run, session.Origin, "malformed-callbacks", session.Timeout);
        Assert.Equal(0L, await fixture.CountAsync("verified_payments"));
        Assert.Equal(0L, await fixture.CountAsync("payment_fulfilments"));

        await RunAsync(run, session.Origin, "malformed-recheck", session.Timeout);
        Assert.Equal(1, fake.Posts);
        await P0302Tests.AssertSingleFulfilmentAsync(fixture, run.Quote, 1);
        await RecordAsync(fixture, run, fake, "malformed-build-and-postgres.json", MalformedPhases, session.Timeout);
    }

    private static class Flags
    {
        internal static void Clear(string evidence, params string[] names)
        {
            foreach (var name in names)
                File.Delete(Path(evidence, name));
        }

        internal static void Raise(string evidence, string name) => File.WriteAllText(Path(evidence, name), "");

        /// <summary>Waits for the browser's flag; a phase that ends first surfaces its own failure instead.</summary>
        internal static async Task WaitAsync(string evidence, string name, Task phase, CancellationToken ct)
        {
            while (!File.Exists(Path(evidence, name)))
            {
                if (phase.IsCompleted)
                {
                    await phase;
                    Assert.Fail($"The browser phase ended before raising {name}.");
                }
                await Task.Delay(200, ct);
            }
        }

        private static string Path(string evidence, string name) => System.IO.Path.Combine(evidence, name + ".flag");
    }
}
