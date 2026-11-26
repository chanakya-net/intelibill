using System.Security.Cryptography;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0607aTests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0607Tests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LostAndReorderedDeliveryRecoversOriginalOperation(bool loseActivationReceipt)
    {
        using var rsa = RSA.Create(2048);
        await using var s = await StartAsync(database, rsa);
        var delivery = s.F.Checkout.Delivery;
        var old = await delivery.LatestSnapshotAsync(s.North.ShopId);
        var id = await ConsentedAsync(s, s.Permit.ValidUntil);
        var (manager, _) = await s.F.Checkout.Trial.Catalogue.ManagerAsync("recover@example.test");
        using var session = manager;
        var operation = await ApproveAsync(s, id, manager);
        await DrainAsync(s);
        s.F.At(s.Permit.ValidUntil);
        s.F.Hosts.Fixture.Clock.Now = s.Permit.ValidUntil;
        delivery.Transport.LeaseClock.Now = s.Permit.ValidUntil.AddSeconds(1);
        await delivery.ScanAsync(true);
        var consume = (Guid)(await s.F.ScalarAsync("SELECT event_id FROM entitlement_messages WHERE kind='TransferConsume'"))!;
        var missing = loseActivationReceipt ? id : consume;
        var url = loseActivationReceipt ? EntitlementSnapshotsV1.AcknowledgementPath : CoverageTransferApprovalsV1.ReceiptPath;
        delivery.Transport.BeforeDispatch = async (r, ct) =>
        {
            if (r.RequestUri!.AbsolutePath != url) return;
            using var json = JsonDocument.Parse(await r.Content!.ReadAsByteArrayAsync(ct));
            if (json.RootElement.GetProperty("eventId").GetGuid() == missing) throw new IOException("Receipt withheld by fixture");
        };
        var job = $"SELECT status FROM entitlement_outbox WHERE event_id='{missing}'";
        for (var retry = 0; retry < 15 && !Equals("DeadLetter", await delivery.MerchantScalarAsync(job)); retry++)
        {
            await DrainAsync(s);
            delivery.Transport.LeaseClock.Now += TimeSpan.FromMinutes(6);
        }
        await AssertStateAsync(manager, AdminPath(s, id), operation, loseActivationReceipt ? "PendingActivation" : "Consuming");
        Assert.Equal("DeadLetter", await delivery.MerchantScalarAsync(job));
        delivery.Transport.BeforeDispatch = null;
        await DrainAsync(s);
        await DrainAsync(s);
        await AssertStateAsync(manager, AdminPath(s, id), operation, "Applied");
        // Redeliver the exact prior source event from its own outbox. It cannot rebind the shop.
        await LicensingPostgreSqlFixture.ExecuteAsync(s.F.Hosts.LicensingOwner,
            $"UPDATE entitlement_outbox SET status='Pending',attempts=0,next_attempt_at='2020-01-01' WHERE event_id='{old.EventId}'");
        await DrainAsync(s);
        Assert.Equal(s.Destination.Account, await BindingAsync(s));
        Assert.Equal(1L, await s.F.CountAsync("coverage_transfer_bindings"));
    }
}
