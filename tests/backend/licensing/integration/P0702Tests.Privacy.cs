using System.Net;
using System.Net.Http.Json;
using Intelibill.Domain.Enums;
using Intelibill.Licensing.Application.Notifications;
using Intelibill.Shared.Contracts.V1;
using Microsoft.Extensions.DependencyInjection;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0702Tests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OnlyCurrentlyAuthorizedMailboxReceivesItsAudienceTemplate(bool contactOnSameAccount)
    {
        var clock = new P0201Clock { Now = P0210Fixture.Start };
        await using var fixture = await P0213Fixture.StartAsync(database, licensingExtras: mailpit.BillingSettings(), businessClock: clock);
        var owner = await SignInAsync(fixture.Hosts);
        var shop = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var manager = await SignInAsync(fixture.Hosts);
        var otherOwner = await SignInAsync(fixture.Hosts);
        var other = await CreateAccountAsync(otherOwner.Client);
        var account = await CreateAccountAsync(owner.Client);
        await P0213Fixture.LinkAsync(account, owner, shop);
        var membership = await P0701Tests.InviteBillingManagerAsync(fixture.Hosts.LicensingOwner, owner.Client, account, manager);
        Assert.Equal("Applied", (await P0210Tests.StartAndWaitAsync(shop, fixture.Trial.Accept())).Status);
        await P0701Tests.RunPlannerPassAsync(fixture.Hosts.Licensing.Services);
        await VerifyContactAsync(fixture, contactOnSameAccount ? owner : otherOwner, contactOnSameAccount ? account : other, manager.Email);
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, $"""
            UPDATE billing_account_members SET status = 2, revision = revision + 1 WHERE id = '{membership:D}'
            """);

        clock.Now = clock.Now.AddDays(11);
        while (await DeliverAsync(fixture)) { }
        Assert.Equal(contactOnSameAccount ? "SmtpAccepted" : "Suppressed", await fixture.SourceScalarAsync($"""
            SELECT d.state FROM notification_email_deliveries d JOIN notification_jobs j ON j.id = d.job_id
            WHERE j.account_id = '{account:D}' AND j.recipient_user_id = '{manager.UserId:D}' AND j.threshold = 'TrialMinus3Days'
            """));
        var reminders = (await mailpit.ReadAsync(manager.Email)).Where(body => !body.Contains("/billing-contact/verify")).ToArray();
        if (contactOnSameAccount)
            Assert.DoesNotContain("/billing/accounts", Assert.Single(reminders), StringComparison.Ordinal);
        else
            Assert.Empty(reminders);
        Assert.Empty(await mailpit.ReadAsync(otherOwner.Email));
    }

    [Fact]
    public async Task ChangedVerifiedAddressDoesNotRedirectAlreadyScheduledEmail()
    {
        var clock = new P0201Clock { Now = P0210Fixture.Start };
        await using var fixture = await P0213Fixture.StartAsync(database, licensingExtras: mailpit.BillingSettings(), businessClock: clock);
        var owner = await SignInAsync(fixture.Hosts);
        var shop = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var account = await CreateAccountAsync(owner.Client);
        await P0213Fixture.LinkAsync(account, owner, shop);
        Assert.Equal("Applied", (await P0210Tests.StartAndWaitAsync(shop, fixture.Trial.Accept())).Status);
        await P0701Tests.RunPlannerPassAsync(fixture.Hosts.Licensing.Services);
        var replacement = P0202Fixture.UniqueEmail();
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.MerchantDb, $"""
            UPDATE users SET email = '{replacement}', is_email_verified = true WHERE id = '{owner.UserId:D}'
            """);
        clock.Now = clock.Now.AddDays(11);
        while (await DeliverAsync(fixture)) { }
        Assert.Equal("Suppressed", await fixture.SourceScalarAsync($"""
            SELECT d.state FROM notification_email_deliveries d JOIN notification_jobs j ON j.id = d.job_id
            WHERE j.recipient_user_id = '{owner.UserId:D}' AND j.account_id = '{account:D}' AND j.threshold = 'TrialMinus3Days'
            """));
        Assert.Empty(await mailpit.ReadAsync(owner.Email));
        Assert.Empty(await mailpit.ReadAsync(replacement));
        Assert.Single(await mailpit.ReadAsync(shop.Email));
    }

    private async Task VerifyContactAsync(P0213Fixture fixture, Merchant owner, Guid account, string address)
    {
        var request = new ApproveBillingContactRequestV1(Guid.NewGuid(), null, address, "en-IN");
        using var response = await owner.Client.PostAsJsonAsync($"/api/billing/accounts/{account:D}/notice-contacts", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var contact = (await response.Content.ReadFromJsonAsync<BillingContactV1>())!;
        var proof = await mailpit.ReadProofAsync(fixture.Hosts.Licensing.Services, address, contact.ContactId);
        await using var scope = fixture.Hosts.Licensing.Services.CreateAsyncScope();
        var consent = scope.ServiceProvider.GetRequiredService<BillingContactConsentHandler>();
        var confirmed = await consent.ConfirmStandaloneProofAsync(account, contact.ContactId,
            new ConfirmBillingContactConsentRequestV1(Guid.NewGuid(), address, proof, true, "en-IN"), CancellationToken.None);
        Assert.False(confirmed.IsError);
        Assert.True(confirmed.Value.Eligible);
    }

    [Fact]
    public async Task EnqueueFailureRollsBackScheduleAndReplayCreatesOneDeliveryPerEmail()
    {
        var clock = new P0201Clock { Now = P0210Fixture.Start };
        await using var fixture = await P0213Fixture.StartAsync(database, licensingExtras: mailpit.BillingSettings(), businessClock: clock);
        var owner = await SignInAsync(fixture.Hosts);
        var shop = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var account = await CreateAccountAsync(owner.Client);
        await P0213Fixture.LinkAsync(account, owner, shop);
        Assert.Equal("Applied", (await P0210Tests.StartAndWaitAsync(shop, fixture.Trial.Accept())).Status);
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, """
            CREATE FUNCTION fixture_stop_enqueue() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION USING ERRCODE = '40001', MESSAGE = 'Fixture enqueue rollback'; END $$;
            CREATE TRIGGER fixture_stop_enqueue BEFORE INSERT ON notification_email_deliveries
                FOR EACH ROW EXECUTE FUNCTION fixture_stop_enqueue()
            """);
        var first = await P0701Tests.RunPlannerPassAsync(fixture.Hosts.Licensing.Services);
        Assert.True(first.AccountsSkippedUnavailable > 0);
        Assert.Equal(0L, await fixture.SourceScalarAsync("SELECT count(*) FROM notification_jobs"));
        Assert.Equal(0L, await fixture.SourceScalarAsync("SELECT count(*) FROM notification_email_deliveries"));
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, """
            DROP TRIGGER fixture_stop_enqueue ON notification_email_deliveries;
            DROP FUNCTION fixture_stop_enqueue()
            """);
        await Task.WhenAll(P0701Tests.RunPlannerPassAsync(fixture.Hosts.Licensing.Services),
            P0701Tests.RunPlannerPassAsync(fixture.Hosts.Licensing.Services));
        Assert.Equal(4L, await fixture.SourceScalarAsync("SELECT count(*) FROM notification_email_deliveries"));
        Assert.Equal(0L, await fixture.SourceScalarAsync("""
            SELECT count(*) FROM effective_notification_jobs j LEFT JOIN notification_email_deliveries d ON d.job_id = j.id
            WHERE ('Email' = ANY(j.allowed_channels)) <> (d.id IS NOT NULL)
            """));
    }
}
