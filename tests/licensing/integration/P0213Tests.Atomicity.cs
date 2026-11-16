using System.Net;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0213Tests
{
    [Fact]
    public async Task BusinessFactsAndMerchantProjectionRollBackWithTheirOutboxes()
    {
        await using var fixture = await P0213Fixture.StartAsync(database);
        var owner = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var account = await CreateAccountAsync(owner.Client);
        await P0213Fixture.LinkAsync(account, owner, owner);
        await fixture.DrainAsync(source: true);
        await fixture.DrainAsync(source: false);

        var consent = new ShopTrialConsentV1(Guid.NewGuid(), owner.ShopId, owner.UserId, Guid.NewGuid(),
            0, 1, P0210Fixture.Start, fixture.Trial.Accept());
        await RefuseSourceCommitAsync(fixture, consent);
        using var retry = fixture.Trial.Delivery(consent);
        await ExpectStatusAsync(fixture.LicensingApi, retry, HttpStatusCode.OK);
        Assert.Equal(1L, await fixture.Trial.CountAsync("trial_history"));

        var snapshot = await fixture.LatestSnapshotAsync(owner.ShopId);
        await RefuseMerchantCommitAsync(fixture, snapshot);
        var acknowledgement = await SendReceiptAsync(fixture, snapshot);
        Assert.Equal("Applied", acknowledgement.Disposition);
        Assert.Equal(2L, await EvidenceCountAsync(fixture, owner.ShopId));
        await AssertAcknowledgementBindingAsync(fixture, acknowledgement);
    }

    private static async Task RefuseSourceCommitAsync(P0213Fixture fixture, ShopTrialConsentV1 consent)
    {
        var before = await fixture.Trial.CountAsync("entitlement_outbox");
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, """
            ALTER TABLE public.entitlement_snapshots ADD CONSTRAINT fixture_refuse CHECK (false) NOT VALID
            """);
        try
        {
            using var wouldCommit = fixture.Trial.Delivery(consent);
            await ExpectStatusAsync(fixture.LicensingApi, wouldCommit, HttpStatusCode.ServiceUnavailable);
            Assert.Equal(0L, await fixture.Trial.CountAsync("trial_history"));
            Assert.Equal(before, await fixture.Trial.CountAsync("entitlement_outbox"));
        }
        finally
        {
            await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner,
                "ALTER TABLE public.entitlement_snapshots DROP CONSTRAINT fixture_refuse");
        }
    }

    private static async Task RefuseMerchantCommitAsync(P0213Fixture fixture, EntitlementSnapshotV1 snapshot)
    {
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.MerchantDb, """
            ALTER TABLE public.entitlement_outbox ADD CONSTRAINT fixture_refuse CHECK (false) NOT VALID
            """);
        try
        {
            using var wouldCommit = await fixture.Transport.SignedAsync(snapshot, fixture.AuthClock);
            await ExpectStatusAsync(fixture.MerchantApi, wouldCommit, HttpStatusCode.ServiceUnavailable);
            Assert.Equal(1L, await EvidenceCountAsync(fixture, snapshot.ShopId));
            Assert.Equal(0L, await fixture.MerchantScalarAsync($"""
                SELECT count(*) FROM public.entitlement_snapshot_inbox WHERE event_id = '{snapshot.EventId:D}'
                """));
            Assert.Equal("PendingActivation", await fixture.MerchantScalarAsync($"""
                SELECT state FROM public.shop_license_states WHERE shop_id = '{snapshot.ShopId:D}'
                """));
        }
        finally
        {
            await LicensingPostgreSqlFixture.ExecuteAsync(fixture.MerchantDb,
                "ALTER TABLE public.entitlement_outbox DROP CONSTRAINT fixture_refuse");
        }
    }

    private static async Task AssertAcknowledgementBindingAsync(P0213Fixture fixture, EntitlementAcknowledgementV1 actual)
    {
        var wrongDigest = new string('0', 64);
        var wrong = new[]
        {
            actual with { Digest = wrongDigest },
            actual with { ShopRevision = actual.ShopRevision + 1, AppliedShopRevision = actual.AppliedShopRevision + 1 },
            actual with { EventId = Guid.NewGuid() },
        };
        foreach (var body in wrong)
        {
            using var signed = await fixture.Transport.SignedAsync(JsonSerializer.SerializeToUtf8Bytes(body, EntitlementSnapshotsV1.Json),
                fixture.AuthClock, merchant: true, path: EntitlementSnapshotsV1.AcknowledgementPath);
            await ExpectStatusAsync(fixture.LicensingApi, signed, HttpStatusCode.Conflict);
        }
        Assert.Equal("Pending", await OutboxAsync(fixture, actual.EventId));
        Assert.True(await fixture.DeliverAsync(source: false));
        Assert.Equal("Applied", await OutboxAsync(fixture, actual.EventId));
    }
}
