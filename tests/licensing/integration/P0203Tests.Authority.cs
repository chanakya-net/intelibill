using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0203Tests
{
    [Fact]
    public async Task IssueIsOwnerOnlyAndRetriesTheOriginalNonEnumeratingOutcome()
    {
        await using var host = await StartAsync();
        var owner = await host.SignInAsync();
        var named = await host.SignInAsync();
        var unrelated = await host.SignInAsync();
        var disabled = await host.SignInAsync();
        var account = await CreateAccountAsync(owner.Client);

        using var refused = await IssueAsync(unrelated.Client, account, named.Email);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        using var stale = await owner.Client.PostAsJsonAsync(Path($"/api/billing/accounts/{account:D}/invitations"),
            new { operationKey = Guid.NewGuid(), expectedOwnerRevision = 2, recipientKind = "email", recipient = named.Email });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        await SetMerchantUserAsync(disabled.Actor, "is_login_enabled = false");
        await SetMerchantUserAsync(unrelated.Actor, "is_email_verified = false");
        await AssertGenericIssueAsync(owner.Client, account, disabled.Email);
        await AssertGenericIssueAsync(owner.Client, account, unrelated.Email);
        await AssertGenericIssueAsync(owner.Client, account, P0202Fixture.UniqueEmail());

        var key = await AssertGenericIssueAsync(owner.Client, account, named.Email);
        await AssertGenericIssueAsync(owner.Client, account, named.Email, key);
        await AssertGenericIssueAsync(owner.Client, account, named.Email);
        using var changed = await IssueAsync(owner.Client, account, P0202Fixture.UniqueEmail(), key);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        Assert.Single(await InboxAsync(named.Client));
        Assert.Equal(1L, await P0202Fixture.CountAsync(database, host.Fixture, "SELECT count(*) FROM billing_account_invitations"));
        Assert.Equal(5L, await P0202Fixture.CountAsync(database, host.Fixture, "SELECT count(*) FROM billing_invitation_issue_receipts"));
    }

    [Fact]
    public async Task PhoneOnlyRecipientStillNeedsLiveVerificationToAccept()
    {
        await using var host = await StartAsync();
        var owner = await host.SignInAsync();
        var named = await host.SignInAsync();
        var account = await CreateAccountAsync(owner.Client);
        var phone = $"+1555{Random.Shared.Next(1000000, 9999999)}";
        await SetMerchantUserAsync(named.Actor, $"""
            email = NULL, is_email_verified = false, phone_number = '{phone}', is_phone_verified = true, phone_verified_at = now()
            """);
        using var issue = await owner.Client.PostAsJsonAsync(Path($"/api/billing/accounts/{account:D}/invitations"),
            new { operationKey = Guid.NewGuid(), expectedOwnerRevision = 1, recipientKind = "phone", recipient = phone });
        Assert.Equal(HttpStatusCode.Accepted, issue.StatusCode);
        var invitation = Assert.Single(await InboxAsync(named.Client)).GetProperty("invitationId").GetGuid();

        await SetMerchantUserAsync(named.Actor, "is_phone_verified = false, phone_verified_at = NULL");
        using var denied = await AcceptAsync(named.Client, invitation);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        await SetMerchantUserAsync(named.Actor, "is_phone_verified = true, phone_verified_at = now()");
        using var accepted = await AcceptAsync(named.Client, invitation);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        using var redelegate = await IssueAsync(named.Client, account, P0202Fixture.UniqueEmail());
        Assert.Equal(HttpStatusCode.Forbidden, redelegate.StatusCode);
        using var transfer = await named.Client.PostAsJsonAsync(Path($"/api/billing/accounts/{account:D}/transfer"),
            new { target = named.Actor });
        Assert.Equal(HttpStatusCode.NotFound, transfer.StatusCode);
    }

    [Fact]
    public async Task RestrictedRuntimeAndScopedFunctions_DoNotExposeForeignFacts()
    {
        await using var host = await StartAsync();
        var owner = await host.SignInAsync();
        var named = await host.SignInAsync();
        var stranger = await host.SignInAsync();
        var account = await CreateAccountAsync(owner.Client);
        var another = await CreateAccountAsync(stranger.Client);
        await AssertGenericIssueAsync(owner.Client, account, named.Email);
        var id = Assert.Single(await InboxAsync(named.Client)).GetProperty("invitationId").GetGuid();

        using var denied = await stranger.Client.GetAsync(Path($"/api/billing/accounts/{account:D}/members"));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(0L, await P0202Fixture.RuntimeScalarAsync(host.Fixture, account, stranger.Actor,
            "SELECT count(*) FROM public.billing_roster('00000000-0000-0000-0000-000000000000', NULL)"));
        Assert.Equal(false, await P0202Fixture.RuntimeScalarAsync(host.Fixture, another, named.Actor,
            $"SELECT public.billing_inviter_is_current('{id:D}')"));
        Assert.Equal(0L, await P0202Fixture.RuntimeScalarAsync(host.Fixture, another, named.Actor,
            "SELECT count(*) FROM public.billing_account_invitations"));
        Assert.Equal(0L, await P0202Fixture.RuntimeScalarAsync(host.Fixture, null, null,
            "SELECT count(*) FROM public.billing_account_invitations"));
        Assert.Equal(0L, await P0202Fixture.RuntimeScalarAsync(host.Fixture, account, named.Actor,
            "SELECT count(*) FROM public.billing_invitation_issue_receipts"));

        await using var migration = host.Fixture.OpenMigrationContext();
        Assert.False(migration.Database.HasPendingModelChanges());
        var migrator = migration.GetService<IMigrator>();
        var failure = await Assert.ThrowsAnyAsync<Exception>(() =>
            migrator.MigrateAsync("20261115142000_BillingAccountsAndPlanLifecycle"));
        Assert.Equal("55000", Assert.IsType<PostgresException>(failure is PostgresException ? failure : failure.InnerException).SqlState);
        Assert.Single(await InboxAsync(named.Client));
    }

    private Task SetMerchantUserAsync(Guid user, string set) => LicensingPostgreSqlFixture.ExecuteAsync(
        database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase), $"UPDATE users SET {set} WHERE id = '{user:D}'");

    private static async Task<Guid> AssertGenericIssueAsync(HttpClient client, Guid account, string email, Guid? key = null)
    {
        var operation = key ?? Guid.NewGuid();
        using var response = await IssueAsync(client, account, email, operation);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await JsonAsync(response);
        Assert.Single(body.EnumerateObject());
        Assert.Equal(operation, body.GetProperty("operationKey").GetGuid());
        return operation;
    }
}
