using System.Security.Cryptography;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0607aTests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0607Tests
{
    [Fact]
    public async Task UpgradePreservesPriorProposalAndPrivateDocumentAndRetainsApprovalOnDowngrade()
    {
        using var rsa = RSA.Create(2048);
        await using var s = await StartAsync(database, rsa);
        var doc = await AttachOriginalAsync(s);
        var merchantSessionAt = s.F.Hosts.Fixture.Clock.Now;
        await using var context = s.F.Hosts.Fixture.OpenMigrationContext();
        var migrator = context.GetService<IMigrator>();
        const string previous = "20261126120000_BillingDocumentMemberRead";
        await migrator.MigrateAsync(previous);
        var id = await InsertPreviousProposalAsync(s);
        var terms = (string)(await s.F.ScalarAsync($"SELECT terms_digest FROM coverage_transfer_proposals WHERE transfer_id='{id}'"))!;
        await migrator.MigrateAsync();
        await LicensingPostgreSqlFixture.ExecuteAsync(s.F.Hosts.LicensingOwner, P0302Fixture.RuntimeGrant);
        Assert.Equal(1L, await s.F.CountAsync("coverage_transfer_proposals", $"transfer_id='{id}' AND funding_digest IS NULL"));
        Assert.Equal(terms, (await ReadAsync(s.Source.Payer.Client, s.Source.Account, id)).GetProperty("termsDigest").GetString());
        await AcceptAsync(s.Source.Payer.Client, s.Source.Account, id, "source-billing-owner");
        await AcceptAsync(s.Destination.Payer.Client, s.Destination.Account, id, "destination-billing-owner");
        await ShopAcceptAsync(s, id);
        var (manager, _) = await s.F.Checkout.Trial.Catalogue.ManagerAsync("upgrade@example.test");
        using var session = manager;
        var operation = await ApproveAsync(s, id, manager);
        await DrainAsync(s);
        await AssertStateAsync(manager, AdminPath(s, id), operation, "Scheduled");
        var refused = await Record.ExceptionAsync(() => migrator.MigrateAsync(previous));
        Assert.Equal("55000", Assert.IsType<PostgresException>(refused?.GetBaseException()).SqlState);
        Assert.Equal(1L, await s.F.CountAsync("coverage_transfer_approvals"));
        s.F.Hosts.Fixture.Clock.Now = merchantSessionAt;
        await VerifyDocumentsAsync(s, doc);
    }

    // Write the previous schema and its original V1 terms against an actual paid order and issued permit.
    // The historical schema has no funding_digest column.
    private static async Task<Guid> InsertPreviousProposalAsync(Scenario s)
    {
        var id = Guid.NewGuid();
        var affiliation = (Guid)(await s.F.ScalarAsync($"SELECT request_id FROM shop_affiliations WHERE shop_id='{s.North.ShopId}'"))!;
        var evidence = (string)(await s.F.ScalarAsync($"SELECT public.coverage_transfer_evidence_digest('{s.Source.Account}','{s.Destination.Account}')"))!;
        var permit = (string)(await s.F.ScalarAsync($"""
            SELECT public.coverage_transfer_permit_digest('{s.Source.Account}','{s.North.ShopId}','{affiliation}','{RequestedAt:O}')
            """))!;
        var end = P0601Tests.February29;
        var c = new RequestCoverageTransferV1(Guid.NewGuid(), s.Destination.Account, s.North.ShopId, CoverageTransfersV1.ReviewedAllocation,
            s.Permit.ValidUntil, "Transfer agreed by owners", affiliation, end, 1, evidence, true, true, true, true, true);
        var terms = CoverageTransfersV1.TermsDigest(id, 1, s.Source.Account, s.Destination.Account, s.North.ShopId,
            affiliation, s.Term, c.Treatment, c.CutoverAt, end, evidence, permit, s.Permit.ValidUntil);
        var hash = CheckoutOrdersV1.Digest("coverage-transfer-request/1", s.Source.Account, s.Term, c);
        await LicensingPostgreSqlFixture.ExecuteAsync(s.F.Hosts.LicensingOwner, $"""
            BEGIN;
            INSERT INTO coverage_transfer_proposals(transfer_id,proposal_revision,source_account_id,source_account_name,
                destination_account_id,source_term_id,shop_id,shop_name,source_affiliation_id,treatment,cutover_at,source_paid_through_at,
                observed_permit_count,observed_permit_deadline,last_contact_at,observed_at,permit_digest,evidence_digest,terms_digest,
                request_reason,requester_id,requester_member_id,requester_revision,operation_key,request_hash,created_at,expires_at,state)
            SELECT '{id}',1,a.id,a.name,'{s.Destination.Account}','{s.Term}','{s.North.ShopId}','North','{affiliation}',
                '{c.Treatment}','{s.Permit.ValidUntil:O}','{end:O}',1,'{s.Permit.ValidUntil:O}','{RequestedAt:O}','{RequestedAt:O}',
                '{permit}','{evidence}','{terms}','{c.Reason}',m.merchant_user_id,m.id,m.revision,'{c.OperationKey}','{hash}',
                '{RequestedAt:O}','{s.Permit.ValidUntil:O}','Open'
            FROM billing_accounts a JOIN billing_account_members m ON m.account_id=a.id
            WHERE a.id='{s.Source.Account}' AND m.merchant_user_id='{s.Source.Payer.UserId}';
            SELECT public.coverage_transfer_freeze_evidence('{id}');
            COMMIT
            """);
        return id;
    }
}
