using System.Net;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0204Tests
{
    [Fact]
    public async Task UnknownSelfAndUnverifiedRecipientsGetUniformAcknowledgementWithoutAcceptance()
    {
        await using var host = await StartAsync();
        var owner = await host.SignInAsync();
        var unverified = await host.SignInAsync();
        var stranger = await host.SignInAsync();
        var account = await CreateAccountAsync(owner.Client);
        var merchant = database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase);
        await LicensingPostgreSqlFixture.ExecuteAsync(merchant,
            $"UPDATE users SET is_email_verified = false WHERE id = '{unverified.Actor:D}'");

        var proposals = new List<Guid>();
        foreach (var email in new[] { P0202Fixture.UniqueEmail(), owner.Email, unverified.Email })
        {
            var key = Guid.NewGuid();
            using var issued = await IssueAsync(owner.Client, account, email, key: key);
            Assert.Equal(HttpStatusCode.Accepted, issued.StatusCode);
            var acknowledgement = Assert.Single((await JsonAsync(issued)).EnumerateObject());
            Assert.Equal("operationKey", acknowledgement.Name);
            Assert.Equal(key, acknowledgement.Value.GetGuid());

            var proposal = await LicensingPostgreSqlFixture.ReadScalarAsync(database.Bootstrap(host.Fixture.Owner.Database),
                $"SELECT id FROM billing_ownership_proposals WHERE operation_key = '{key:D}' AND recipient_actor_id IS NULL");
            proposals.Add(Assert.IsType<Guid>(proposal));
        }

        // Reverify the formerly unverified user so the check reaches recipient scope and RLS.
        await LicensingPostgreSqlFixture.ExecuteAsync(merchant,
            $"UPDATE users SET is_email_verified = true WHERE id = '{unverified.Actor:D}'");
        foreach (var actor in new[] { owner.Client, unverified.Client, stranger.Client })
        {
            Assert.Empty(await InboxAsync(actor));
            foreach (var proposal in proposals)
            {
                using var refused = await AcceptAsync(actor, proposal, Guid.NewGuid());
                Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
            }
        }

        var member = Assert.Single(await P0202Fixture.ReadAccountRowsAsync(database, host.Fixture));
        Assert.Equal((account, owner.Actor, 1, 1), (member.AccountId, member.MerchantUserId, member.Role, member.Status));
    }

    [Fact]
    public async Task RepeatedTransfersPreserveRecordedCoverageAndEarlierResponsibility()
    {
        await using var host = await StartAsync();
        var first = await host.SignInAsync();
        var second = await host.SignInAsync();
        var third = await host.SignInAsync();
        var account = await CreateAccountAsync(first.Client);
        var bootstrap = database.Bootstrap(host.Fixture.Owner.Database);
        var now = host.Fixture.Clock.Now;
        await P0205Tests.SeedTermAsync(bootstrap, account, now.AddHours(-2), now.AddDays(2));
        const string coverage = "SELECT row_to_json(t)::text FROM subscription_coverage_terms t";
        var before = Assert.IsType<string>(await LicensingPostgreSqlFixture.ReadScalarAsync(bootstrap, coverage));
        var stale = await ProposeAsync(first.Client, account, third.Client, third.Email);
        var outward = await ProposeAsync(first.Client, account, second.Client, second.Email);
        using var accepted = await AcceptAsync(second.Client, outward, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var evidenceQuery = $"SELECT row_to_json(t)::text FROM billing_ownership_acceptances t WHERE transfer_id = '{outward:D}'";
        var original = Assert.IsType<string>(await LicensingPostgreSqlFixture.ReadScalarAsync(bootstrap, evidenceQuery));
        var returnProposal = await ProposeAsync(second.Client, account, first.Client, first.Email);
        using var returned = await AcceptAsync(first.Client, returnProposal, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, returned.StatusCode);
        using var refused = await AcceptAsync(third.Client, stale, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);

        Assert.Equal(before, await LicensingPostgreSqlFixture.ReadScalarAsync(bootstrap, coverage));
        Assert.Equal(original, await LicensingPostgreSqlFixture.ReadScalarAsync(bootstrap, evidenceQuery));
        Assert.Equal(1L, await CountAsync(host, $"SELECT count(*) FROM billing_account_members WHERE role = 1 AND merchant_user_id = '{first.Actor:D}' AND revision = 3"));
        Assert.Equal(2L, await CountAsync(host, "SELECT count(*) FROM billing_ownership_acceptances"));
    }

    [Fact]
    public async Task RecipientVerificationAndAuthorizationPrecedeKeyedReplay()
    {
        await using var host = await StartAsync();
        var owner = await host.SignInAsync();
        var named = await host.SignInAsync();
        var account = await CreateAccountAsync(owner.Client);
        var issueKey = Guid.NewGuid();
        using var issued = await IssueAsync(owner.Client, account, named.Email, key: issueKey);
        Assert.Equal(HttpStatusCode.Accepted, issued.StatusCode);
        var proposal = Assert.Single(await InboxAsync(named.Client)).GetProperty("transferId").GetGuid();
        var key = Guid.NewGuid();
        using var stale = await AcceptAsync(named.Client, proposal, key, version: 2);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var accepted = await AcceptAsync(named.Client, proposal, key);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        using var changed = await AcceptAsync(named.Client, proposal, key, version: 2);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        using var formerOwner = await IssueAsync(owner.Client, account, named.Email, key: issueKey);
        Assert.Equal(HttpStatusCode.Forbidden, formerOwner.StatusCode);

        using var ownIssue = await owner.Client.GetAsync($"/api/billing/ownership-transfer-operations/issue/{issueKey:D}");
        using var foreignIssue = await named.Client.GetAsync($"/api/billing/ownership-transfer-operations/issue/{issueKey:D}");
        Assert.Equal(HttpStatusCode.OK, ownIssue.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignIssue.StatusCode);
        await LicensingPostgreSqlFixture.ExecuteAsync(database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase),
            $"UPDATE users SET is_email_verified = false WHERE id = '{named.Actor:D}'");
        using var unverified = await AcceptAsync(named.Client, proposal, key);
        Assert.Equal(HttpStatusCode.Unauthorized, unverified.StatusCode);
        using var unverifiedRecovery = await named.Client.GetAsync($"/api/billing/ownership-transfer-operations/accept/{key:D}");
        Assert.Equal(HttpStatusCode.Unauthorized, unverifiedRecovery.StatusCode);
    }

    [Fact]
    public void PrivateContractPinsActionsPathsAndConsentFields()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(ProjectPaths.Root,
            "tests/contracts/licensing/billing-ownership-transfers-v1.json")));
        var root = doc.RootElement;
        var account = Guid.Parse("91b27e17-df9f-41bb-9e35-28f14bfc3250");
        var transfer = Guid.Parse("35bc6eb7-d147-4be3-8659-75ad72a47ec7");
        var routes = new (string Action, string Method, string Path)[]
        {
            (BillingOwnershipTransfersV1.Issue, "POST", BillingOwnershipTransfersV1.IssuePath(account)),
            (BillingOwnershipTransfersV1.Inbox, "GET", BillingOwnershipTransfersV1.InboxPath(null)),
            (BillingOwnershipTransfersV1.Preview, "GET", BillingOwnershipTransfersV1.PreviewPath(transfer)),
            (BillingOwnershipTransfersV1.Accept, "POST", BillingOwnershipTransfersV1.AcceptPath(transfer)),
            (BillingOwnershipTransfersV1.Recover, "GET", BillingOwnershipTransfersV1.OperationPath("accept", transfer)),
        };
        Assert.Equal(routes, root.GetProperty("routes").EnumerateArray().Select(item =>
            (item.GetProperty("action").GetString()!, item.GetProperty("method").GetString()!, item.GetProperty("path").GetString()!)));
        Assert.All(root.GetProperty("routes").EnumerateArray(), route => Assert.Equal(
            route.GetProperty("scope").GetString() == "actor",
            MerchantBillingDelegationV1.IsActorScoped(route.GetProperty("action").GetString())));
        Assert.Equal(BillingOwnershipTransfersV1.Policy, root.GetProperty("responsibilityPolicyVersion").GetString());
        using var accepted = JsonSerializer.SerializeToDocument(new AcceptBillingOwnershipTransferV1(transfer, 1,
            BillingOwnershipTransfersV1.Policy, true));
        Assert.Equal(root.GetProperty("acceptanceFields").EnumerateArray().Select(field => field.GetString()),
            accepted.RootElement.EnumerateObject().Select(field => field.Name));
    }
}
