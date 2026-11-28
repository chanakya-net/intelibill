using Intelibill.Licensing.Application.Notifications;
using Microsoft.Extensions.DependencyInjection;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0703Tests(LicensingPostgreSqlFixture database, MailpitFixture mailpit)
    : IClassFixture<LicensingPostgreSqlFixture>, IClassFixture<MailpitFixture>
{
    internal static readonly DateTimeOffset Start = P0601Tests.January31;
    internal static readonly DateTimeOffset Due = Start.AddMonths(1).AddDays(-7);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SupersededReminderNeverReachesSmtp(bool renew)
    {
        await using var paid = await P0308Fixture.StartAsync(database, Start, Settings(mailpit));
        var stale = await paid.PackageAsync(1);
        var control = await paid.PackageAsync(1);
        var (_, term) = await PurchaseAsync(stale, "Month");
        await PurchaseAsync(control, "Month");
        await P0608Tests.DrainAsync(paid);
        await P0701Tests.RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        Assert.Equal("Pending", await StateAsync(paid, stale));
        paid.At(Due);
        if (renew)
            P0302Fixture.AssertStatus((await RenewAsync(stale, term, "Month")).Status, "Verified", "Fulfilled", "Applying");
        else
            await P0417Tests.CloseShopAsync(stale.Shops[0], stale.Account);
        await DrainMailAsync(paid);
        Assert.Equal("SmtpAccepted", await StateAsync(paid, control));
        Assert.Single(await mailpit.ReadAsync(control.Payer.Email));
        var state = await StateAsync(paid, stale);
        var mail = await mailpit.ReadAsync(stale.Payer.Email);
        Assert.True(state is "Suppressed" && mail.Length == 0, $"Delivery {state}, mailbox captures {mail.Length}.");
        Assert.Equal(renew ? "SourceNoLongerCurrent" : "ScopeClosed", await paid.ScalarAsync($"""
            SELECT reason FROM notification_email_events WHERE account_id = '{stale.Account:D}' AND state = 'Suppressed'
            """));
    }

    internal static Dictionary<string, string?> Settings(MailpitFixture mail)
    {
        var settings = mail.BillingSettings();
        foreach (var pair in P0302Fixture.SimulatorSettings)
            settings[pair.Key] = pair.Value;
        settings["Notifications:PlanInBackground"] = "false";
        return settings;
    }

    internal static Task<object?> StateAsync(P0308Fixture paid, Package package, string threshold = "PaidMinus7Days") =>
        paid.ScalarAsync($"""
            SELECT d.state FROM notification_email_deliveries d JOIN notification_jobs j ON d.job_id = j.id
            WHERE j.account_id = '{package.Account:D}' AND j.scope_kind = 'Account' AND j.threshold = '{threshold}'
                AND j.recipient_user_id = '{package.Payer.UserId:D}' ORDER BY j.due_at LIMIT 1
            """);

    internal static async Task DrainMailAsync(P0308Fixture paid)
    {
        for (var pass = 0; pass < 100; pass++)
            if (!await DeliverAsync(paid))
                return;
        Assert.Fail("Dispatcher did not drain the fixture queue.");
    }

    private static async Task<bool> DeliverAsync(P0308Fixture paid, CancellationToken ct = default)
    {
        await using var scope = paid.Hosts.Licensing.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IBillingEmailDispatcher>().DeliverOneAsync(ct);
    }
}
