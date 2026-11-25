using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Intelibill.Shared.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using static Intelibill.Licensing.Integration.Tests.P0609Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0610Tests
{
    // P06-09's publisher emitted this revision on d131ed868f4f40d8d9f2b5aa340b2ca85bf3417a.
    private const string LegacyEvent = "f8cb5f1e-4c5f-4ffc-a612-f9f66ba58c0b";
    private const string LegacyDigest = "37bb7c331dbfd8724883e6b05199701d5f59c66ae0190cd51a82c10517fa7927";
    private static readonly Guid LegacyShop = new("52489830-3270-4706-9d33-a6fc5a16493f");
    private static readonly Guid LegacyAccount = new("75f50c90-7ca6-8ae7-b5ab-d1c4a839db20");

    [Fact]
    public async Task RetainedApprovalKeepsEffectiveAccessWithoutDispatch()
    {
        var checkout = await P0302Fixture.StartAsync(database, new() { Now = Instant }, merchantServices: services =>
            services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
                .PostConfigure<TimeProvider>((options, clock) => options.TokenValidationParameters.LifetimeValidator =
                    (start, end, _, _) => start <= clock.GetUtcNow().UtcDateTime && end > clock.GetUtcNow().UtcDateTime));
        await using var fixture = new P0308Fixture(checkout);
        fixture.Hosts.Fixture.Clock.Now = Instant;
        var delivery = fixture.Checkout.Delivery;
        await using var context = fixture.Hosts.Fixture.OpenMigrationContext();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20261125100918_RefundApprovals");
        var package = await RestoreLegacyPurchaseAsync(fixture);
        var boundary = Instant.AddMinutes(2);
        delivery.Transport.LeaseClock.Now = Instant.AddMinutes(1);

        var payload = await LegacyPayloadAsync(delivery);
        Assert.Equal(LegacyDigest, SignedServiceAssertion.Digest(payload));
        Assert.Equal(LegacyDigest, await delivery.SourceScalarAsync(
            $"SELECT digest FROM entitlement_messages WHERE event_id='{LegacyEvent}'"));
        await P0608Tests.DrainAsync(fixture);
        var receipt = await LegacyReceiptAsync(delivery);
        await AssertLegacyProjectionAsync(fixture, receipt, boundary);
        var ledger = await EntitlementLedgerAsync(delivery);
        await LicensingPostgreSqlFixture.ExecuteAsync(delivery.MerchantDb,
            $"UPDATE shop_license_states SET enforcement_activated_at='{Instant:O}' WHERE shop_id='{LegacyShop}'");
        await AssertTradingUntilAsync(package, boundary);

        await migrator.MigrateAsync();
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, P0302Fixture.RuntimeGrant);
        await PumpRefundsAsync(fixture);
        await P0608Tests.DrainAsync(fixture);
        using var redelivery = await delivery.Transport.SignedAsync(payload, delivery.AuthClock);
        using var response = await delivery.MerchantApi.SendAsync(redelivery);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(receipt, await response.Content.ReadAsByteArrayAsync());
        await PumpRefundsAsync(fixture);
        await P0608Tests.DrainAsync(fixture);

        Assert.Equal(1L, await fixture.CountAsync("refund_applications", "disposition='Applied'"));
        Assert.Equal(0L, await fixture.CountAsync("refund_attempts"));
        Assert.Equal(0L, await fixture.CountAsync("refund_work"));
        Assert.Equal(0L, await fixture.CountAsync("refund_sends"));
        Assert.Equal(ledger, await EntitlementLedgerAsync(delivery));
        Assert.Equal(payload, await LegacyPayloadAsync(delivery));
        await AssertLegacyProjectionAsync(fixture, receipt, boundary);
        await AssertLegacyMerchantAccessAsync(fixture, package, boundary);
    }

    private static async Task<P0308Fixture.Package> RestoreLegacyPurchaseAsync(P0308Fixture fixture)
    {
        var directory = Path.Combine(ProjectPaths.Root, "tests/contracts/licensing/p06-09-retained-refund");
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner,
            await File.ReadAllTextAsync(Path.Combine(directory, "licensing.sql")));
        // Recreate only the fixture user's credentials. Membership and consent retain their historical IDs.
        const string email = "retained-p0609@example.test";
        var user = await P0201Tests.SeedMerchantAsync(fixture.Hosts.Merchant.Services, email);
        var actor = new Guid("21cd4ed2-cbd2-4221-a1aa-181f7c7b9fa4");
        var merchantDb = fixture.Checkout.Delivery.MerchantDb;
        await LicensingPostgreSqlFixture.ExecuteAsync(merchantDb, $"UPDATE users SET id='{actor}' WHERE id='{user}'");
        await LicensingPostgreSqlFixture.ExecuteAsync(merchantDb, await File.ReadAllTextAsync(Path.Combine(directory, "merchant.sql")));
        var client = fixture.Hosts.Merchant.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await P0201Tests.LoginAsync(client, email));
        var shop = new P0209Fixture.Merchant(actor, LegacyShop, client, email);
        return new(shop, LegacyAccount, [shop], Guid.Empty);
    }

    private static async Task<byte[]> LegacyPayloadAsync(P0213Fixture delivery) => Assert.IsType<byte[]>(
        await delivery.SourceScalarAsync($"SELECT payload FROM entitlement_messages WHERE event_id='{LegacyEvent}'"));

    private static async Task<byte[]> LegacyReceiptAsync(P0213Fixture delivery) => Assert.IsType<byte[]>(
        await delivery.MerchantScalarAsync($"SELECT acknowledgement FROM entitlement_snapshot_inbox WHERE event_id='{LegacyEvent}'"));

    private static async Task<string> EntitlementLedgerAsync(P0213Fixture delivery) => Assert.IsType<string>(
        await delivery.SourceScalarAsync("""
            SELECT jsonb_agg(jsonb_build_array(s.event_id,s.account_revision,s.shop_revision,m.digest,encode(m.payload,'hex'))
                ORDER BY s.shop_revision)::text FROM entitlement_snapshots s JOIN entitlement_messages m USING(event_id)
            """));

    private static async Task AssertLegacyProjectionAsync(P0308Fixture fixture, byte[] receipt, DateTimeOffset boundary)
    {
        using var json = JsonDocument.Parse(receipt);
        var ack = json.RootElement;
        Assert.Equal(LegacyEvent, ack.GetProperty("eventId").GetString());
        Assert.Equal(LegacyDigest, ack.GetProperty("digest").GetString());
        Assert.Equal("Applied", ack.GetProperty("disposition").GetString());
        Assert.Equal(3, ack.GetProperty("appliedShopRevision").GetInt64());
        var delivery = fixture.Checkout.Delivery;
        Assert.Equal(receipt, await LegacyReceiptAsync(delivery));
        Assert.Equal(receipt, Assert.IsType<byte[]>(await delivery.SourceScalarAsync(
            $"SELECT payload FROM entitlement_merchant_inbox WHERE event_id='{LegacyEvent}' AND kind='Acknowledgement'")));
        Assert.Equal(1L, await fixture.CountAsync("applied_entitlement_shop_receipts", $"shop_id='{LegacyShop}' AND shop_revision=3"));
        Assert.Equal((3L, boundary.UtcDateTime), await ReadMerchantLicenseStateAsync(delivery.MerchantDb, LegacyShop));
        Assert.Equal(3L, await fixture.CountAsync("entitlement_snapshots"));
        Assert.Equal(1L, await fixture.CountAsync("refund_approvals", """
            reservation_id='6fe182c9-3d37-46a7-89b2-72258100223f' AND receipt_revision=1
            AND confirmation_id='6ef5db21-8da3-42d4-b142-059871029e0f' AND amount_paise=4500
            """));
    }

    private static async Task AssertLegacyMerchantAccessAsync(P0308Fixture fixture, P0308Fixture.Package package,
        DateTimeOffset boundary)
    {
        var item = await MerchantOutcomeAsync(package);
        Assert.Equal("Historical", item.GetProperty("providerState").GetString());
        Assert.Equal("Applied", item.GetProperty("accessDisposition").GetString());
        Assert.Equal(4500L, item.GetProperty("approvedAmountPaise").GetInt64());
        Assert.Equal(6000L, item.GetProperty("amountPaise").GetInt64());
        fixture.At(boundary.AddTicks(-10));
        fixture.Hosts.Fixture.Clock.Now = boundary.AddTicks(-10);
        var before = await P0308Fixture.ShopTermAsync(package, package.Payer);
        Assert.Equal("Active", before.GetProperty("state").GetString());
        Assert.Equal(boundary, before.GetProperty("accessEndsAt").GetDateTimeOffset());
        await AssertTradingUntilAsync(package, boundary);
        fixture.At(boundary);
        fixture.Hosts.Fixture.Clock.Now = boundary;
        Assert.Equal("Expired", (await P0308Fixture.ShopTermAsync(package, package.Payer)).GetProperty("state").GetString());
        await AssertCodeAsync(await package.Payer.Client.GetAsync(P0418Tests.AuthorityPath(LegacyShop)), HttpStatusCode.Forbidden, "Licence.Expired");
    }

    private static async Task AssertTradingUntilAsync(P0308Fixture.Package package, DateTimeOffset boundary)
    {
        var allowed = await ReadAsync(await package.Payer.Client.GetAsync(P0418Tests.AuthorityPath(LegacyShop)), HttpStatusCode.OK);
        Assert.True(allowed.GetProperty("enforced").GetBoolean());
        Assert.Equal(boundary, allowed.GetProperty("validUntil").GetDateTimeOffset());
    }
}
