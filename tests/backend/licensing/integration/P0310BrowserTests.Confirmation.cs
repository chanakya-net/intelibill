using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0310BrowserTests
{
    private static async Task ReceiptCheckpointAsync(P0302Fixture fixture, Purchase purchase, string scratch,
        string phase, CancellationToken ct)
    {
        var status = await StatusAsync(purchase.Payer.Client, purchase.Quote);
        AssertStatus(status, "Verified", "Fulfilled", phase == "applied" ? "Applied" : "Applying");
        Assert.Equal(phase == "applied" ? 1 : 0, status.GetProperty("shopsApplied").GetInt32());
        var row = Assert.Single((await purchase.Payer.Client.GetFromJsonAsync<PaymentConfirmationV1[]>(PaymentPath(purchase.Quote) + "/confirmations", ct))!);
        var bytes = await purchase.Payer.Client.GetByteArrayAsync(PaymentPath(purchase.Quote) + $"/confirmations/{row.ConfirmationId}/document", ct);
        var snapshot = await fixture.Delivery.LatestSnapshotAsync(purchase.Payer.ShopId);
        var signedAck = await fixture.Delivery.SourceScalarAsync($"SELECT convert_from(payload, 'UTF8') FROM entitlement_merchant_inbox WHERE event_id = '{snapshot.EventId:D}' AND kind = 'Acknowledgement'");
        Assert.Equal(phase == "applied", signedAck is string);
        var json = (string)(await fixture.Delivery.SourceScalarAsync($"SELECT facts::text FROM payment_confirmations WHERE confirmation_id = '{row.ConfirmationId:D}'"))!;
        var facts = JsonSerializer.Deserialize<PaymentConfirmationSnapshotV1>(json, JsonSerializerOptions.Web)!;
        Assert.Equal(("simulator-a", "Development", "a/1"), (facts.ProviderAccountId, facts.Environment, facts.ConfigurationVersion));
        Assert.Equal((purchase.Quote.AmountPaise, purchase.Quote.Currency), (facts.Receipt.AmountPaise, facts.Receipt.Currency));
        var verified = await fixture.CountAsync("verified_payments");
        var fulfilled = await fixture.CountAsync("payment_fulfilments");
        var confirmations = await fixture.CountAsync("payment_confirmations");
        Assert.Equal((1L, 1L, 1L), (verified, fulfilled, confirmations));
        var evidence = Environment.GetEnvironmentVariable("P0310_EVIDENCE_DIR")!;
        await File.WriteAllTextAsync(Path.Combine(evidence, $"confirmation-{phase}.json"), JsonSerializer.Serialize(new
        {
            phase, status, verified, fulfilled, confirmations, facts, sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            snapshot.EventId, snapshot.ShopId, targetRevision = snapshot.Shop!.Revision,
            merchantRevision = await fixture.Delivery.MerchantScalarAsync($"SELECT revision FROM shop_license_states WHERE shop_id = '{snapshot.ShopId:D}'"),
            signedAck, scratchMarkersOnly = Path.GetFileName(scratch),
        }), ct);
    }

    private static async Task CompareDownloadsAsync(P0302Fixture fixture, Purchase purchase, params string[] names)
    {
        var row = Assert.Single((await purchase.Payer.Client.GetFromJsonAsync<PaymentConfirmationV1[]>(PaymentPath(purchase.Quote) + "/confirmations"))!);
        var bytes = await purchase.Payer.Client.GetByteArrayAsync(PaymentPath(purchase.Quote) + $"/confirmations/{row.ConfirmationId}/document");
        Assert.Equal(1L, await fixture.CountAsync("payment_confirmations"));
        foreach (var name in names)
            Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(Environment.GetEnvironmentVariable("P0310_EVIDENCE_DIR")!, name)));
    }
}
