using System.Net;
using System.Text;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0507Tests
{
    [Fact]
    public async Task ObservationCarriesOnlyAllowListedFieldsAndAnOpaqueDeviceReference()
    {
        var contract = JsonDocument.Parse(File.ReadAllText(Path.Combine(ProjectPaths.Root,
            "tests/contracts/licensing/offline-device-observation-v1.json"))).RootElement;
        Assert.Equal(OfflineDeviceObservationsV1.Path, contract.GetProperty("path").GetString());
        Assert.Equal(OfflineDeviceObservationsV1.Kind, contract.GetProperty("messageKind").GetString());
        var vector = contract.GetProperty("deviceRef");
        Assert.Equal(vector.GetProperty("expected").GetString(), OfflineDeviceObservationsV1.DeviceRef(
            vector.GetProperty("shopId").GetGuid(), vector.GetProperty("deviceId").GetString()!));
        foreach (var limit in contract.GetProperty("limits").EnumerateObject())
            Assert.Equal(limit.Value.GetString(), OfflineDeviceObservationsV1.Limit(limit.Name));

        await using var run = await Run.StartAsync(database);
        var permit = await run.IssuePermitAsync(Device);
        await run.SyncAsync(Device, invoices: 2);
        await run.DeliverAsync();

        var owner = LicensingPostgreSqlFixture.ReadScalarAsync;
        foreach (var (connection, table) in new[] { (run.Fixture.MerchantDb, "entitlement_messages"),
            (run.Fixture.Hosts.LicensingOwner, "entitlement_merchant_inbox") })
        {
            var payloads = ((string)(await owner(connection, $"""
                SELECT string_agg(convert_from(payload, 'UTF8'), E'\n' ORDER BY event_id) FROM public.{table}
                WHERE kind = 'DeviceObservation' AND convert_from(payload, 'UTF8') LIKE '%{run.Owner.ShopId:D}%'
                """))!).Split('\n');
            Assert.Equal(2, payloads.Length);
            foreach (var payload in payloads)
                AssertAllowListed(contract, payload, run, permit);
        }

        // What licensing keeps is the same closed set: no column can hold merchant content.
        var columns = (string)(await run.Fixture.SourceScalarAsync("""
            SELECT string_agg(column_name, ',' ORDER BY ordinal_position) FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'offline_device_observations'
            """))!;
        Assert.Equal("event_id,account_id,shop_id,affiliation_request_id,device_ref,observation,observed_at,permit_id," +
            "not_before,valid_until,permit_limit,account_revision,shop_revision,authority_verified_at", columns);

        // The provider read exposes exactly the versioned shape.
        using var admin = await P0417Tests.SignInAsync(run.Fixture, "support");
        var status = await ReadAsync(admin, run.Account);
        var read = contract.GetProperty("providerRead");
        Assert.Equal(Names(read.GetProperty("fields")), status.EnumerateObject().Select(member => member.Name));
        Assert.Equal(Names(read.GetProperty("deviceFields")),
            status.GetProperty("devices")[0].EnumerateObject().Select(member => member.Name));
        Assert.DoesNotContain(Device, status.GetRawText());

        // The contract is closed: one extra member is refused and nothing is stored.
        var seen = (await run.MerchantObservationsAsync())[1];
        var widened = JsonSerializer.SerializeToNode(seen with { EventId = Guid.NewGuid() }, EntitlementSnapshotsV1.Json)!.AsObject();
        widened["invoiceNumber"] = "A/2627/000001";
        using var request = await run.Fixture.Transport.SignedAsync(Encoding.UTF8.GetBytes(widened.ToJsonString()),
            run.Fixture.AuthClock, merchant: true, path: OfflineDeviceObservationsV1.Path);
        using var refused = await run.Fixture.LicensingApi.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(2L, await run.SourceCountAsync("offline_device_observations"));
    }

    private static void AssertAllowListed(JsonElement contract, string payload, Run run, Permit permit)
    {
        var body = JsonDocument.Parse(payload).RootElement;
        Assert.Equal(Names(contract.GetProperty("fields")), body.EnumerateObject().Select(member => member.Name));
        if (body.GetProperty("permit").ValueKind == JsonValueKind.Object)
            Assert.Equal(Names(contract.GetProperty("permitFields")),
                body.GetProperty("permit").EnumerateObject().Select(member => member.Name));
        foreach (var forbidden in Names(contract.GetProperty("forbiddenFields")))
            Assert.DoesNotContain($"\"{forbidden}\"", payload);
        foreach (var secret in new[] { Device, permit.LeaseId.ToString("D"), permit.GrantId.ToString("D"),
            run.Owner.UserId.ToString("D"), "Fixture stock", "Walk-in Meera", "A/2627" })
            Assert.DoesNotContain(secret, payload);
        Assert.Equal(OfflineDeviceObservationsV1.DeviceRef(run.Owner.ShopId, Device), body.GetProperty("deviceRef").GetString());
    }

    private static string[] Names(JsonElement list) => [.. list.EnumerateArray().Select(name => name.GetString()!)];
}
