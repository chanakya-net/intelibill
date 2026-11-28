using System.Net;
using System.Net.Http.Json;
using Intelibill.Licensing.Application.Notifications;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Shared.Contracts.V1;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0701Tests
{
    [Fact]
    public async Task ContactApprovalOmitsMailboxProofAndRequiresIndependentHolderVerification()
    {
        var jan31 = Utc("2028-01-31T04:30:00Z");
        await using var paid = await P0308Fixture.StartAsync(
            database,
            jan31,
            settings: MailSettingsWithSimulator());
        var package = await paid.PackageAsync(1, ["Contact Store"]);
        await PurchaseAsync(package, "Month");
        await P0608Tests.DrainAsync(paid);

        // 1. Existing merchant user contact: approve -> verify-only -> opt-in enables Email only & denies reads.
        var contactUser = await SignInAsync(paid.Hosts);
        var approveOp = Guid.NewGuid();
        var (userContact, approveRaw) = await ApproveContactRawAsync(
            package.Payer.Client, package.Account, new ApproveBillingContactRequestV1(approveOp, contactUser.UserId, contactUser.Email, "hi-IN"));
        Assert.DoesNotContain("verificationProofToken", approveRaw);
        Assert.DoesNotContain("proofToken", approveRaw);
        Assert.False(userContact.Eligible);

        await RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        Assert.Empty(await ReadJobsForRecipientAsync(paid.Hosts.LicensingOwner, package.Account, LicenseNoticesV1.ScopeAccount, NotificationPlanner.UserRecipientKey(contactUser.UserId)));

        using (var verifyOnly = await contactUser.Client.PostAsJsonAsync(
            $"/api/billing/notice-contacts/{userContact.ContactId:D}/consent",
            new ConfirmBillingContactConsentRequestV1(Guid.NewGuid(), contactUser.Email, null, OptedIn: false, "hi-IN")))
            Assert.False((await verifyOnly.Content.ReadFromJsonAsync<BillingContactV1>())!.Eligible);

        await RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        Assert.Empty(await ReadJobsForRecipientAsync(paid.Hosts.LicensingOwner, package.Account, LicenseNoticesV1.ScopeAccount, NotificationPlanner.UserRecipientKey(contactUser.UserId)));

        using (var optIn = await contactUser.Client.PostAsJsonAsync(
            $"/api/billing/notice-contacts/{userContact.ContactId:D}/consent",
            new ConfirmBillingContactConsentRequestV1(Guid.NewGuid(), contactUser.Email, null, OptedIn: true, "hi-IN")))
            Assert.True((await optIn.Content.ReadFromJsonAsync<BillingContactV1>())!.Eligible);

        // 2. Standalone address-only contact: raw approve & list omit secret; isolated mailbox sink delivers proof.
        const string standaloneEmail = "accounts.payable@north-example.test";
        var (standalone, standaloneRaw) = await ApproveContactRawAsync(
            package.Payer.Client, package.Account, new ApproveBillingContactRequestV1(Guid.NewGuid(), null, standaloneEmail, "ta-IN"));
        Assert.DoesNotContain("verificationProofToken", standaloneRaw);
        using var listResp = await package.Payer.Client.GetAsync($"/api/billing/accounts/{package.Account:D}/notice-contacts");
        var listRaw = await listResp.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        Assert.DoesNotContain("verificationProofToken", listRaw);
        Assert.DoesNotContain("proofToken", listRaw);

        await AssertStandaloneMailboxProofLifecycleAsync(paid, package.Account, standalone.ContactId, standaloneEmail, mailpit);

        await RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        var userJobs = await ReadJobsForRecipientAsync(paid.Hosts.LicensingOwner, package.Account, LicenseNoticesV1.ScopeAccount, NotificationPlanner.UserRecipientKey(contactUser.UserId));
        Assert.Equal(5, userJobs.Count);
        Assert.All(userJobs, j => { Assert.Equal([LicenseNoticesV1.ChannelEmail], j.Channels); Assert.Equal("hi-IN", j.Locale); });
        Assert.Equal(HttpStatusCode.Forbidden, (await contactUser.Client.GetAsync($"/api/billing/accounts/{package.Account:D}/notices")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await contactUser.Client.GetAsync($"/api/licensing/shops/{package.Shops[0].ShopId:D}/notices")).StatusCode);
    }

    [Fact]
    public async Task ContactConsentAndRevocationAreReplaySafeAndPreserveOptOut()
    {
        var jan31 = Utc("2028-01-31T04:30:00Z");
        await using var paid = await P0308Fixture.StartAsync(database, jan31);
        var package = await paid.PackageAsync(1, ["Replay Store"]);
        var contactUser = await SignInAsync(paid.Hosts);

        // Approve key binds normalized locale: exact replay ok, altered locale -> 409 ContactOperationMismatch.
        var approveOp = Guid.NewGuid();
        var (approved, _) = await ApproveContactRawAsync(
            package.Payer.Client, package.Account, new ApproveBillingContactRequestV1(approveOp, contactUser.UserId, contactUser.Email, "hi-IN"));
        using (var sameApprove = await package.Payer.Client.PostAsJsonAsync(
            $"/api/billing/accounts/{package.Account:D}/notice-contacts",
            new ApproveBillingContactRequestV1(approveOp, contactUser.UserId, contactUser.Email, "hi-IN")))
            Assert.Equal(HttpStatusCode.Created, sameApprove.StatusCode);
        using (var alteredApprove = await package.Payer.Client.PostAsJsonAsync(
            $"/api/billing/accounts/{package.Account:D}/notice-contacts",
            new ApproveBillingContactRequestV1(approveOp, contactUser.UserId, contactUser.Email, "ta-IN")))
            Assert.Equal(HttpStatusCode.Conflict, alteredApprove.StatusCode);

        // Opt-in -> opt-out -> redelivery of earlier opt-in preserves opt-out, revision, and append-only events.
        await AssertOptInOptOutReplayPreservesOptOutAsync(paid, contactUser, approved.ContactId);

        // Newly planned paid term excludes the opted-out contact.
        await PurchaseAsync(package, "Month");
        await P0608Tests.DrainAsync(paid);
        await RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        Assert.Empty(await ReadJobsForRecipientAsync(paid.Hosts.LicensingOwner, package.Account, LicenseNoticesV1.ScopeAccount, NotificationPlanner.UserRecipientKey(contactUser.UserId)));

        // Revoke binds operation key: exact replay keeps revision, reusing key on a second contact -> 409.
        var revokeOp = Guid.NewGuid();
        var revokeUrl = $"/api/billing/accounts/{package.Account:D}/notice-contacts/{approved.ContactId:D}/revoke";
        using (var revoked = await package.Payer.Client.PostAsJsonAsync(revokeUrl, new RevokeBillingContactRequestV1(revokeOp)))
            Assert.Equal(4, (await revoked.Content.ReadFromJsonAsync<BillingContactV1>())!.Revision);
        using (var revokeReplay = await package.Payer.Client.PostAsJsonAsync(revokeUrl, new RevokeBillingContactRequestV1(revokeOp)))
            Assert.Equal(4, (await revokeReplay.Content.ReadFromJsonAsync<BillingContactV1>())!.Revision);
        var (secondContact, _) = await ApproveContactRawAsync(
            package.Payer.Client, package.Account, new ApproveBillingContactRequestV1(Guid.NewGuid(), null, "other@replay.test", "en-IN"));
        using var reusedRevokeKey = await package.Payer.Client.PostAsJsonAsync(
            $"/api/billing/accounts/{package.Account:D}/notice-contacts/{secondContact.ContactId:D}/revoke",
            new RevokeBillingContactRequestV1(revokeOp));
        Assert.Equal(HttpStatusCode.Conflict, reusedRevokeKey.StatusCode);
    }

    [Fact]
    public async Task SequentialMemberAndContactAliasOverlapPreservesOneEmailAndAuthorizedInAppView()
    {
        var jan31 = Utc("2028-01-31T04:30:00Z");
        await using var paid = await P0308Fixture.StartAsync(
            database,
            jan31,
            settings: MailSettingsWithSimulator());
        var package = await paid.PackageAsync(1, ["Alias Store"]);
        var (_, termId) = await PurchaseAsync(package, "Month");
        await P0608Tests.DrainAsync(paid);

        // Direction 1: verified identity-linked contact planned FIRST -> then promoted to Billing Manager!
        var promoted = await SignInAsync(paid.Hosts);
        var (contact1, _) = await ApproveContactRawAsync(
            package.Payer.Client, package.Account, new ApproveBillingContactRequestV1(Guid.NewGuid(), promoted.UserId, promoted.Email, "en-IN"));
        using (var optIn = await promoted.Client.PostAsJsonAsync(
            $"/api/billing/notice-contacts/{contact1.ContactId:D}/consent",
            new ConfirmBillingContactConsentRequestV1(Guid.NewGuid(), promoted.Email, null, true, "en-IN")))
            Assert.Equal(HttpStatusCode.OK, optIn.StatusCode);
        await RunPlannerPassAsync(paid.Hosts.Licensing.Services);

        await InviteBillingManagerAsync(paid.Hosts.LicensingOwner, package.Payer.Client, package.Account, promoted);
        await RunPlannerPassAsync(paid.Hosts.Licensing.Services);

        await AssertLogicalNoticesAsync(paid, package.Account, termId, promoted);
        Assert.Equal(5L, await CountEmailJobsForDestinationAsync(paid.Hosts.LicensingOwner, package.Account, termId, promoted.Email));
        paid.At(Utc("2028-02-22T04:30:00Z"));
        using (var promotedRead = await promoted.Client.GetAsync($"/api/billing/accounts/{package.Account:D}/notices"))
        {
            Assert.Equal(HttpStatusCode.OK, promotedRead.StatusCode);
            var item = Assert.Single((await promotedRead.Content.ReadFromJsonAsync<AccountLicenseNoticesV1>())!.Notices);
            Assert.Equal(["Email", "InApp"], item.AllowedChannels.Order().ToArray());
        }
        await DeliverDueAsync(paid);
        var delivered = Assert.Single(await mailpit.ReadAsync(promoted.Email));
        Assert.Contains($"/billing/accounts/{package.Account:D}/status", delivered, StringComparison.Ordinal);

        // Direction 2 (reverse): Billing Manager planned FIRST -> standalone contact added -> membership REMOVED!
        paid.At(jan31);
        await AssertDemotedManagerRetainsContactEmailWithoutDuplicateAsync(paid, package, termId, mailpit);
    }

    private static async Task AssertOptInOptOutReplayPreservesOptOutAsync(
        P0308Fixture paid,
        P0209Fixture.Merchant contactUser,
        Guid contactId)
    {
        var optInOp = Guid.NewGuid();
        var optOutOp = Guid.NewGuid();
        var consentUrl = $"/api/billing/notice-contacts/{contactId:D}/consent";
        using (var firstOptIn = await contactUser.Client.PostAsJsonAsync(
            consentUrl, new ConfirmBillingContactConsentRequestV1(optInOp, contactUser.Email, null, true, "hi-IN")))
            Assert.True((await firstOptIn.Content.ReadFromJsonAsync<BillingContactV1>())!.Eligible);

        using (var optOut = await contactUser.Client.PostAsJsonAsync(
            consentUrl, new ConfirmBillingContactConsentRequestV1(optOutOp, contactUser.Email, null, false, "hi-IN")))
        {
            var body = (await optOut.Content.ReadFromJsonAsync<BillingContactV1>())!;
            Assert.False(body.Eligible);
            Assert.Equal(3, body.Revision);
        }

        var eventsBeforeRetry = await ReadContactEventsAsync(paid.Hosts.LicensingOwner, contactId);
        Assert.Equal([(1, "Approved"), (2, "OptedIn"), (2, "Verified"), (3, "OptedOut")], eventsBeforeRetry);

        var retries = await Task.WhenAll(
            contactUser.Client.PostAsJsonAsync(consentUrl, new ConfirmBillingContactConsentRequestV1(optInOp, contactUser.Email, null, true, "hi-IN")),
            contactUser.Client.PostAsJsonAsync(consentUrl, new ConfirmBillingContactConsentRequestV1(optInOp, contactUser.Email, null, true, "hi-IN")));
        foreach (var retry in retries)
        {
            Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
            var replayBody = (await retry.Content.ReadFromJsonAsync<BillingContactV1>())!;
            Assert.False(replayBody.Eligible);
            Assert.Equal(3, replayBody.Revision);
        }

        Assert.Equal(eventsBeforeRetry, await ReadContactEventsAsync(paid.Hosts.LicensingOwner, contactId));
        using var alteredLocale = await contactUser.Client.PostAsJsonAsync(
            consentUrl, new ConfirmBillingContactConsentRequestV1(optInOp, contactUser.Email, null, true, "ta-IN"));
        Assert.Equal(HttpStatusCode.Conflict, alteredLocale.StatusCode);
    }

    private Dictionary<string, string?> MailSettingsWithSimulator()
    {
        var options = mailpit.BillingSettings();
        foreach (var setting in P0302Fixture.SimulatorSettings)
            options.Add(setting.Key, setting.Value);
        return options;
    }

    private static async Task AssertDemotedManagerRetainsContactEmailWithoutDuplicateAsync(
        P0308Fixture paid,
        P0308Fixture.Package package,
        Guid termId,
        MailpitFixture mailpit)
    {
        var demoted = await SignInAsync(paid.Hosts);
        var demotedMemberId = await InviteBillingManagerAsync(paid.Hosts.LicensingOwner, package.Payer.Client, package.Account, demoted);
        await RunPlannerPassAsync(paid.Hosts.Licensing.Services);

        var (contact2, _) = await ApproveContactRawAsync(
            package.Payer.Client, package.Account, new ApproveBillingContactRequestV1(Guid.NewGuid(), null, "  " + demoted.Email.ToUpperInvariant() + "  ", "en-IN"));
        var proof2 = await mailpit.ReadProofAsync(paid.Hosts.Licensing.Services, demoted.Email, contact2.ContactId);
        await using (var scope = paid.Hosts.Licensing.Services.CreateAsyncScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<BillingContactConsentHandler>();
            var confirmed = await handler.ConfirmStandaloneProofAsync(
                package.Account, contact2.ContactId,
                new ConfirmBillingContactConsentRequestV1(Guid.NewGuid(), demoted.Email, proof2, true, "en-IN"),
                CancellationToken.None);
            Assert.False(confirmed.IsError);
        }

        await LicensingPostgreSqlFixture.ExecuteAsync(
            paid.Hosts.LicensingOwner,
            $"UPDATE billing_account_members SET status = {(int)BillingAccountMemberStatus.Removed}, revision = revision + 1 WHERE id = '{demotedMemberId:D}'");
        await RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        Assert.Equal(5L, await CountEmailJobsForDestinationAsync(paid.Hosts.LicensingOwner, package.Account, termId, demoted.Email));
        await AssertLogicalNoticesAsync(paid, package.Account, termId, demoted);
        Assert.Equal(HttpStatusCode.Forbidden, (await demoted.Client.GetAsync($"/api/billing/accounts/{package.Account:D}/notices")).StatusCode);

        paid.At(Utc("2028-02-22T04:30:00Z"));
        await DeliverDueAsync(paid);
        var reminder = Assert.Single(await mailpit.ReadAsync(demoted.Email), body => !body.Contains("/billing-contact/verify"));
        Assert.DoesNotContain("/billing/accounts", reminder, StringComparison.Ordinal);
        paid.At(Utc("2028-01-31T04:30:00Z"));
        var (_, renewStatus) = await RenewAsync(package, termId, "Month");
        var successorTerm = renewStatus.GetProperty("termId").GetGuid();
        await P0608Tests.DrainAsync(paid);
        await RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        Assert.Equal(5L, await CountEmailJobsForDestinationAsync(paid.Hosts.LicensingOwner, package.Account, successorTerm, demoted.Email));
    }

    private static async Task DeliverDueAsync(P0308Fixture paid)
    {
        await using var scope = paid.Hosts.Licensing.Services.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<IBillingEmailDispatcher>();
        while (await sender.DeliverOneAsync(CancellationToken.None)) { }
    }

    private static async Task<(BillingContactV1 Contact, string Raw)> ApproveContactRawAsync(
        HttpClient ownerClient,
        Guid accountId,
        ApproveBillingContactRequestV1 request)
    {
        using var resp = await ownerClient.PostAsJsonAsync($"/api/billing/accounts/{accountId:D}/notice-contacts", request);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var raw = await resp.Content.ReadAsStringAsync();
        return (System.Text.Json.JsonSerializer.Deserialize<BillingContactV1>(raw, WebJson)!, raw);
    }

    private static async Task AssertStandaloneMailboxProofLifecycleAsync(
        P0308Fixture paid,
        Guid accountId,
        Guid contactId,
        string standaloneEmail,
        MailpitFixture mailpit)
    {
        var delivered = await mailpit.ReadProofAsync(paid.Hosts.Licensing.Services, standaloneEmail, contactId);
        await using var scope = paid.Hosts.Licensing.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<BillingContactConsentHandler>();
        var bad = await handler.ConfirmStandaloneProofAsync(
            accountId, contactId,
            new ConfirmBillingContactConsentRequestV1(Guid.NewGuid(), standaloneEmail, "invalid-token", true, "ta-IN"),
            CancellationToken.None);
        Assert.True(bad.IsError);

        var good = await handler.ConfirmStandaloneProofAsync(
            accountId, contactId,
            new ConfirmBillingContactConsentRequestV1(Guid.NewGuid(), standaloneEmail, delivered, true, "ta-IN"),
            CancellationToken.None);
        Assert.False(good.IsError);
        Assert.True(good.Value.Eligible);

        var replayNewOp = await handler.ConfirmStandaloneProofAsync(
            accountId, contactId,
            new ConfirmBillingContactConsentRequestV1(Guid.NewGuid(), standaloneEmail, delivered, true, "ta-IN"),
            CancellationToken.None);
        Assert.True(replayNewOp.IsError);
    }

    private static async Task<(int Revision, string Kind)[]> ReadContactEventsAsync(
        NpgsqlConnectionStringBuilder settings,
        Guid contactId)
    {
        var list = new List<(int, string)>();
        await using var conn = new NpgsqlConnection(settings.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT revision, event_kind FROM public.billing_notice_contact_events WHERE contact_id = $1 ORDER BY revision ASC, event_kind ASC",
            conn);
        cmd.Parameters.AddWithValue(contactId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add((reader.GetInt32(0), reader.GetString(1)));
        return [.. list];
    }

    private static async Task<long> CountEmailJobsForDestinationAsync(
        NpgsqlConnectionStringBuilder settings,
        Guid accountId,
        Guid sourceId,
        string email)
    {
        await using var conn = new NpgsqlConnection(settings.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT count(*)
            FROM public.notification_jobs
            WHERE account_id = $1
              AND scope_kind = 'Account'
              AND source_id = $2
              AND destination_hash = $3
              AND 'Email' = ANY(allowed_channels)
            """, conn);
        cmd.Parameters.AddWithValue(accountId);
        cmd.Parameters.AddWithValue(sourceId);
        cmd.Parameters.AddWithValue(NotificationPlanner.HashEmail(email));
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

}
