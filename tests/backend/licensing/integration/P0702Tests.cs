using System.Diagnostics;
using Intelibill.Domain.Enums;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0702Tests(LicensingPostgreSqlFixture database, MailpitFixture mailpit)
    : IClassFixture<LicensingPostgreSqlFixture>, IClassFixture<MailpitFixture>
{
    [Fact]
    public async Task DueVerifiedOwnerReceivesLocalizedMail()
    {
        var clock = new P0201Clock { Now = P0210Fixture.Start };
        var settings = mailpit.BillingSettings(runInBackground: true);
        await using var fixture = await P0213Fixture.StartAsync(database, licensingExtras: settings, businessClock: clock);
        var owner = await SignInAsync(fixture.Hosts);
        var shop = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var staff = await SignInAsync(fixture.Hosts, ShopRole.Staff, shop.ShopId);
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.MerchantDb,
            $"UPDATE users SET language = 'hi-IN' WHERE id = '{owner.UserId:D}'");
        var account = await CreateAccountAsync(owner.Client);
        await P0213Fixture.LinkAsync(account, owner, shop);
        Assert.Equal("Applied", (await P0210Tests.StartAndWaitAsync(shop, fixture.Trial.Accept())).Status);
        await P0701Tests.RunPlannerPassAsync(fixture.Hosts.Licensing.Services);
        var due = clock.Now.AddDays(11);
        Assert.Equal(1L, await fixture.SourceScalarAsync($"""
            SELECT count(*) FROM public.effective_notification_jobs
            WHERE account_id = '{account:D}' AND recipient_user_id = '{owner.UserId:D}'
              AND threshold = 'TrialMinus3Days' AND due_at = '{due:O}' AND locale = 'hi-IN'
              AND 'Email' = ANY(allowed_channels)
            """));
        Assert.False(await DeliverAsync(fixture));
        Assert.Empty(await mailpit.ReadAsync(owner.Email));

        clock.Now = due;
        var captures = await WaitForCaptureAsync(owner.Email);
        Assert.Contains("ट्रायल", Assert.Single(captures), StringComparison.Ordinal);
        var stop = Stopwatch.StartNew();
        var query = $"""
            SELECT d.state FROM notification_email_deliveries d JOIN notification_jobs j ON j.id = d.job_id
            WHERE j.account_id = '{account:D}' AND j.recipient_user_id = '{owner.UserId:D}' AND j.due_at = '{due:O}'
            """;
        while (await fixture.SourceScalarAsync(query) is "Sending" && stop.Elapsed < TimeSpan.FromSeconds(5))
            await Task.Delay(50);
        Assert.Equal("SmtpAccepted", await fixture.SourceScalarAsync(query));
        Assert.Empty(await mailpit.ReadAsync(staff.Email));
    }

    private async Task<string[]> WaitForCaptureAsync(string recipient)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < TimeSpan.FromSeconds(10))
        {
            var captures = await mailpit.ReadAsync(recipient);
            if (captures.Length > 0)
                return captures;
            await Task.Delay(100);
        }
        return await mailpit.ReadAsync(recipient);
    }
}
