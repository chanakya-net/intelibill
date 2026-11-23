using System.Net;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0507Tests
{
    [Fact]
    public async Task ReplayedReorderedAndConcurrentObservationsConvergeOnce()
    {
        await using var run = await Run.StartAsync(database);
        var issued = await run.IssuePermitAsync(Device);
        // The permit and its observation commit together: one message per stored permit, none without.
        Assert.Equal(1L, (long)(await run.Fixture.MerchantScalarAsync($"""
            SELECT count(*) FROM offline_license_permits p JOIN entitlement_messages m ON m.shop_id = p.shop_id
                AND m.kind = 'DeviceObservation' AND convert_from(m.payload, 'UTF8')::jsonb -> 'permit' ->> 'permitId' = p.id::text
            WHERE p.shop_id = '{run.Owner.ShopId:D}'
            """))!);
        var template = (await run.MerchantObservationsAsync())[0];
        var device = OfflineDeviceObservationsV1.DeviceRef(run.Owner.ShopId, "p0507 second till");
        var older = Window(template, device, issued.NotBefore.AddHours(1), issued.NotBefore.AddHours(20));
        var newer = Window(template, device, issued.NotBefore.AddHours(3), issued.NotBefore.AddHours(30));

        // The newer permit arrives first; a replayed assertion and a re-signed copy change nothing.
        var body = JsonSerializer.SerializeToUtf8Bytes(newer, EntitlementSnapshotsV1.Json);
        using var signed = await run.Fixture.Transport.SignedAsync(body, run.Fixture.AuthClock, merchant: true,
            path: OfflineDeviceObservationsV1.Path);
        var token = signed.Headers.Authorization!.Parameter;
        using var first = await run.Fixture.LicensingApi.SendAsync(signed);
        using var replayed = await run.Fixture.LicensingApi.SendAsync(P0213Transport.Request(body, OfflineDeviceObservationsV1.Path, token));
        using var resigned = await run.PostAsync(newer);
        Assert.Equal((HttpStatusCode.NoContent, HttpStatusCode.Unauthorized, HttpStatusCode.NoContent),
            (first.StatusCode, replayed.StatusCode, resigned.StatusCode));

        // The same event with another body, and the same permit under another event, both conflict.
        using var changed = await run.PostAsync(newer with { Permit = newer.Permit! with { ValidUntil = newer.Permit.ValidUntil.AddDays(30) } });
        using var renamed = await run.PostAsync(newer with { EventId = Guid.NewGuid() });
        Assert.Equal((HttpStatusCode.Conflict, HttpStatusCode.Conflict), (changed.StatusCode, renamed.StatusCode));
        using var late = await run.PostAsync(older);
        Assert.Equal(HttpStatusCode.NoContent, late.StatusCode);
        Assert.Equal(2L, await run.SourceCountAsync("offline_device_observations", $"device_ref = '{device}'"));

        using var admin = await P0417Tests.SignInAsync(run.Fixture, "support");
        run.Clock.Now = issued.NotBefore.AddHours(25);
        var seen = Assert.Single((await ReadAsync(admin, run.Account)).GetProperty("devices").EnumerateArray(),
            row => row.GetProperty("deviceRef").GetString() == device);
        Assert.Equal((newer.Permit!.ValidUntil, newer.ObservedAt, 1, true), (seen.GetProperty("knownPermitEnd").GetDateTimeOffset(),
            seen.GetProperty("lastPermitIssuedAt").GetDateTimeOffset(), seen.GetProperty("outstandingPermits").GetInt32(),
            seen.GetProperty("outstanding").GetBoolean()));

        // Deliveries racing each other and a provider read all finish; every observation is kept once.
        var racing = Enumerable.Range(0, 6).Select(index => index % 2 == 0
            ? Window(template, device, issued.NotBefore.AddHours(4 + index), issued.NotBefore.AddHours(31 + index))
            : template with { EventId = Guid.NewGuid(), DeviceRef = device, Observation = OfflineDeviceObservationsV1.OfflineSync,
                ObservedAt = issued.NotBefore.AddHours(4 + index), Permit = null }).ToArray();
        var posts = racing.Select(async observation =>
        {
            using var response = await run.PostAsync(observation);
            return response.StatusCode;
        }).ToArray();
        var reads = Enumerable.Range(0, 3).Select(_ => ReadAsync(admin, run.Account)).ToArray();
        Assert.All(await Task.WhenAll(posts), status => Assert.Equal(HttpStatusCode.NoContent, status));
        await Task.WhenAll(reads);
        var settled = Assert.Single((await ReadAsync(admin, run.Account)).GetProperty("devices").EnumerateArray(),
            row => row.GetProperty("deviceRef").GetString() == device);
        Assert.Equal((issued.NotBefore.AddHours(35), issued.NotBefore.AddHours(9), issued.NotBefore.AddHours(9)),
            (settled.GetProperty("knownPermitEnd").GetDateTimeOffset(), settled.GetProperty("lastSynchronizedAt").GetDateTimeOffset(),
             settled.GetProperty("lastContactAt").GetDateTimeOffset()));
        Assert.Equal(8L, await run.SourceCountAsync("offline_device_observations", $"device_ref = '{device}'"));
    }

    private static OfflineDeviceObservationV1 Window(OfflineDeviceObservationV1 template, string device, DateTimeOffset from,
        DateTimeOffset until) => template with
        {
            EventId = Guid.NewGuid(), DeviceRef = device, ObservedAt = from,
            Permit = template.Permit! with { PermitId = Guid.NewGuid(), NotBefore = from, ValidUntil = until },
        };
}
