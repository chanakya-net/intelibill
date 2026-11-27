using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0312Tests
{
    private sealed record Confirmation(PaymentConfirmationV1 Receipt, byte[] Bytes);

    private static async Task<Confirmation[]> ConfirmationsAsync(Purchase purchase)
    {
        var path = PaymentPath(purchase.Quote) + "/confirmations";
        var rows = (await purchase.Payer.Client.GetFromJsonAsync<PaymentConfirmationV1[]>(path))!;
        var result = new List<Confirmation>();
        foreach (var row in rows)
        {
            using var response = await purchase.Payer.Client.GetAsync($"{path}/{row.ConfirmationId:D}/document");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
            Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
            var bytes = await response.Content.ReadAsByteArrayAsync();
            // The stored facts are read separately by the caller when checking their original provider/term.
            var text = System.Text.Encoding.UTF8.GetString(bytes);
            Assert.Contains("does not prove applied access", text, StringComparison.Ordinal);
            Assert.Contains("not a tax invoice", text, StringComparison.Ordinal);
            result.Add(new(row, bytes));
        }
        return [.. result];
    }

    private static async Task<PaymentConfirmationSnapshotV1> FactsAsync(P0302Fixture fixture, Guid confirmation)
    {
        var json = (string)(await LicensingPostgreSqlFixture.ReadScalarAsync(fixture.Hosts.LicensingOwner,
            $"SELECT facts::text FROM public.payment_confirmations WHERE confirmation_id = '{confirmation:D}'"))!;
        return JsonSerializer.Deserialize<PaymentConfirmationSnapshotV1>(json, JsonSerializerOptions.Web)!;
    }

    private static async Task AssertSameConfirmationsAsync(Purchase purchase, Confirmation[] original)
    {
        var current = await ConfirmationsAsync(purchase);
        Assert.Equal(original.Length, current.Length);
        foreach (var before in original)
        {
            var after = Assert.Single(current, row => row.Receipt.ConfirmationId == before.Receipt.ConfirmationId);
            Assert.Equal(before.Receipt, after.Receipt);
            Assert.Equal(before.Bytes, after.Bytes);
        }
    }

    private static async Task AssertPackageAsync(P0302Fixture fixture, Purchase purchase)
    {
        var quote = purchase.Quote;
        var order = $"order_id = '{quote.OrderId:D}'";
        var term = (await StatusAsync(purchase.Payer.Client, quote)).GetProperty("termId").GetGuid();
        Assert.Equal(1L, await fixture.CountAsync("payment_fulfilments", order));
        Assert.Equal(1L, await fixture.CountAsync("subscription_term_facts",
            $"term_id = '{term:D}' AND basis = 'Paid' AND price_paise = {quote.AmountPaise} AND plan_version_id = '{quote.PlanVersionId:D}'"));
        Assert.Equal((long)purchase.Shops.Length, await fixture.CountAsync("payment_fulfilment_shops", order));
        Assert.Equal((long)purchase.Shops.Length, await fixture.CountAsync("payment_fulfilment_deliveries", order));
        foreach (var shop in purchase.Shops)
            Assert.Equal(1L, await fixture.CountAsync("payment_fulfilment_shops", $"{order} AND shop_id = '{shop.ShopId:D}' AND term_id = '{term:D}'"));
        Assert.Equal(1L, await fixture.CountAsync("payment_attempts", order));
        Assert.Equal(1L, await fixture.CountAsync("development_gateway_sessions",
            $"attempt_id IN (SELECT attempt_id FROM payment_attempts WHERE {order})"));
        Assert.Equal(1L, await fixture.CountAsync("verified_payments", $"{order} AND amount_paise = {quote.AmountPaise} AND currency = '{quote.Currency}'"));
    }

    private static async Task<EntitlementSnapshotV1[]> SnapshotsAsync(P0302Fixture fixture, Purchase purchase)
    {
        var snapshots = new List<EntitlementSnapshotV1>();
        foreach (var shop in purchase.Shops)
            snapshots.Add(await fixture.Delivery.LatestSnapshotAsync(shop.ShopId));
        return [.. snapshots];
    }

    internal static async Task AssertDeliveryAsync(P0302Fixture fixture, Purchase purchase, int applied)
    {
        var status = await StatusAsync(purchase.Payer.Client, purchase.Quote);
        AssertStatus(status, "Verified", "Fulfilled", applied == purchase.Shops.Length ? "Applied" : "Applying");
        Assert.Equal(applied, status.GetProperty("shopsApplied").GetInt32());
        var acknowledgements = 0;
        foreach (var snapshot in await SnapshotsAsync(fixture, purchase))
        {
            Assert.Equal(status.GetProperty("termId").GetGuid(), snapshot.Shop!.TermId);
            Assert.Equal(snapshot.Shop.Revision, await fixture.Delivery.MerchantScalarAsync(
                $"SELECT revision FROM shop_license_states WHERE shop_id = '{snapshot.ShopId:D}'"));
            Assert.Equal(1L, await fixture.Delivery.MerchantScalarAsync(
                $"SELECT count(*) FROM license_grant_evidence WHERE event_id = '{snapshot.EventId:D}'"));
            var payload = await fixture.Delivery.SourceScalarAsync($"""
                SELECT convert_from(payload, 'UTF8') FROM entitlement_merchant_inbox
                WHERE event_id = '{snapshot.EventId:D}' AND kind = 'Acknowledgement'
                """);
            if (payload is not string json) continue;
            var ack = JsonSerializer.Deserialize<EntitlementAcknowledgementV1>(json, EntitlementSnapshotsV1.Json)!;
            Assert.Equal("Applied", ack.Outcome);
            Assert.Equal(snapshot.EventId, ack.EventId);
            Assert.Equal((snapshot.AccountId, snapshot.ShopId), (ack.AccountId, ack.ShopId));
            Assert.Equal(snapshot.Shop.Revision, ack.ShopRevision);
            Assert.Equal(snapshot.Shop.Revision, ack.AppliedShopRevision);
            Assert.True(ack.AppliedAccountRevision >= snapshot.Account!.Revision);
            acknowledgements++;
        }
        Assert.Equal(applied, acknowledgements);
    }

    private static async Task ObserveAsync(P0302Fixture fixture, Purchase purchase, string checkpoint, object? detail = null)
    {
        var order = $"order_id = '{purchase.Quote.OrderId:D}'";
        var counts = new Dictionary<string, long>();
        foreach (var table in new[] { "payment_attempts", "verified_payments", "manual_payment_receipts",
            "payment_fulfilments", "payment_fulfilment_shops", "payment_fulfilment_deliveries", "payment_confirmations", "unmatched_payments" })
            counts[table] = await fixture.CountAsync(table, order);
        counts["providerSessions"] = await fixture.CountAsync("development_gateway_sessions", $"attempt_id IN (SELECT attempt_id FROM payment_attempts WHERE {order})");
        counts["providerCaptures"] = await fixture.CountAsync("development_gateway_payments", $"provider_order_reference IN (SELECT provider_order_reference FROM development_gateway_sessions WHERE attempt_id IN (SELECT attempt_id FROM payment_attempts WHERE {order}))");
        var confirmations = await ConfirmationsAsync(purchase);
        var receipts = new List<object>();
        foreach (var row in confirmations)
            receipts.Add(new { facts = await FactsAsync(fixture, row.Receipt.ConfirmationId), sha256 = Convert.ToHexStringLower(SHA256.HashData(row.Bytes)) });
        var delivery = new List<object>();
        foreach (var snapshot in await SnapshotsAsync(fixture, purchase))
            delivery.Add(new { snapshot.EventId, snapshot.ShopId, snapshot.Shop,
                merchantRevision = await fixture.Delivery.MerchantScalarAsync($"SELECT revision FROM shop_license_states WHERE shop_id = '{snapshot.ShopId:D}'"),
                signedAcknowledgement = await fixture.Delivery.SourceScalarAsync($"SELECT convert_from(payload, 'UTF8') FROM entitlement_merchant_inbox WHERE event_id = '{snapshot.EventId:D}' AND kind = 'Acknowledgement'") });
        if (Environment.GetEnvironmentVariable("P0312_EVIDENCE_DIR") is not { Length: > 0 } directory) return;
        await File.WriteAllTextAsync(Path.Combine(directory, checkpoint + ".json"), JsonSerializer.Serialize(new
        {
            checkpoint, provenance = "development simulator; real licensing/merchant PostgreSQL; signed transport",
            sourceSha = Environment.GetEnvironmentVariable("P0312_SOURCE_SHA"), purchase.Quote, counts, receipts, delivery,
            status = await StatusAsync(purchase.Payer.Client, purchase.Quote), detail,
        }, JsonSerializerOptions.Web));
    }
}
