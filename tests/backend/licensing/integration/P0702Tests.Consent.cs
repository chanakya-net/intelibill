using System.Net;
using System.Net.Http.Json;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0702Tests
{
    [Fact]
    public async Task StandaloneApprovalAndProofEmailCommitTogether()
    {
        var clock = new P0201Clock { Now = P0210Fixture.Start };
        await using var fixture = await P0213Fixture.StartAsync(
            database, licensingExtras: mailpit.BillingSettings(runInBackground: true), businessClock: clock);
        var owner = await SignInAsync(fixture.Hosts);
        var account = await CreateAccountAsync(owner.Client);
        var address = P0202Fixture.UniqueEmail();
        var request = new ApproveBillingContactRequestV1(Guid.NewGuid(), null, address, "hi-IN");
        var path = $"/api/billing/accounts/{account:D}/notice-contacts";
        using var first = await owner.Client.PostAsJsonAsync(path, request);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var contact = (await first.Content.ReadFromJsonAsync<BillingContactV1>())!;
        using var replay = await owner.Client.PostAsJsonAsync(path, request);
        Assert.Equal(contact.ContactId, (await replay.Content.ReadFromJsonAsync<BillingContactV1>())!.ContactId);
        Assert.False(contact.Eligible);
        Assert.Equal(1L, await fixture.SourceScalarAsync($"""
            SELECT count(*) FROM notification_email_deliveries WHERE account_id = '{account:D}'
                AND proof_contact_id = '{contact.ContactId:D}'
            """));
        var body = Assert.Single(await WaitForCaptureAsync(address));
        Assert.Contains("सत्याप", body, StringComparison.Ordinal);
        Assert.DoesNotContain("/billing/accounts", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StandaloneApprovalFailureLeavesNoConsentOrUndeliverableProof()
    {
        await using var fixture = await P0213Fixture.StartAsync(database, licensingExtras: mailpit.BillingSettings());
        var owner = await SignInAsync(fixture.Hosts);
        var account = await CreateAccountAsync(owner.Client);
        var path = $"/api/billing/accounts/{account:D}/notice-contacts";
        var address = P0202Fixture.UniqueEmail();
        var request = new ApproveBillingContactRequestV1(Guid.NewGuid(), null, address, "bn-IN");
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, """
            CREATE FUNCTION fixture_abort_proof() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION USING ERRCODE = '40001', MESSAGE = 'Fixture approval rollback'; END $$;
            CREATE TRIGGER fixture_abort_proof BEFORE INSERT ON notification_email_deliveries
                FOR EACH ROW EXECUTE FUNCTION fixture_abort_proof()
            """);
        using var failed = await owner.Client.PostAsJsonAsync(path, request);
        Assert.False(failed.IsSuccessStatusCode);
        Assert.Equal(0L, await fixture.SourceScalarAsync("""
            SELECT (SELECT count(*) FROM billing_notice_contacts) + (SELECT count(*) FROM billing_notice_contact_operations)
                + (SELECT count(*) FROM billing_notice_contact_events) + (SELECT count(*) FROM notification_email_deliveries)
            """));
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, """
            DROP TRIGGER fixture_abort_proof ON notification_email_deliveries;
            DROP FUNCTION fixture_abort_proof()
            """);
        using var retry = await owner.Client.PostAsJsonAsync(path, request);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.True(await DeliverAsync(fixture));
        Assert.False(await DeliverAsync(fixture));
        Assert.Single(await mailpit.ReadAsync(address));
    }
}
