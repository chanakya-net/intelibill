using System.Text.Json;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0507Tests
{
    [Fact]
    public async Task RemainingPermissionIsHalfOpenAndSurvivesALaterSuspension()
    {
        await using var run = await Run.StartAsync(database);
        var first = await run.IssuePermitAsync(Device);
        run.MerchantClock.Now = run.MerchantClock.Now.AddMinutes(10);
        // A later snapshot on the same lease is a second permit for the same device.
        var second = await Run.CompleteSnapshotAsync(run.Owner, Device, first.LeaseId, first.GrantId);
        Assert.NotEqual(first.Id, second.Id);
        await run.DeliverAsync();
        var end = first.ValidUntil > second.ValidUntil ? first.ValidUntil : second.ValidUntil;
        using var security = await P0417Tests.SignInAsync(run.Fixture, "security-administrator");

        run.Clock.Now = end.AddMicroseconds(-1);
        var before = Assert.Single((await ReadAsync(security, run.Account)).GetProperty("devices").EnumerateArray());
        Assert.Equal((true, end, second.NotBefore), (before.GetProperty("outstanding").GetBoolean(),
            before.GetProperty("knownPermitEnd").GetDateTimeOffset(), before.GetProperty("lastPermitIssuedAt").GetDateTimeOffset()));
        Assert.Equal(2, before.GetProperty("outstandingPermits").GetInt32());
        Assert.Equal(OfflineDeviceObservationsV1.Limit(first.Reason), before.GetProperty("limit").GetString());

        // The end itself is outside the window; the ended instant stays visible.
        run.Clock.Now = end;
        var at = Assert.Single((await ReadAsync(security, run.Account)).GetProperty("devices").EnumerateArray());
        Assert.Equal((false, end, 0), (at.GetProperty("outstanding").GetBoolean(),
            at.GetProperty("knownPermitEnd").GetDateTimeOffset(), at.GetProperty("outstandingPermits").GetInt32()));

        // A suspension saved and applied after issuance changes nothing that was observed: the permit the
        // device already holds is not revoked by it, and the provider read does not pretend otherwise.
        run.Clock.Now = first.NotBefore.AddHours(1);
        await P0404Tests.SuspendAsync(security, $"/api/admin/accounts/{run.Account:D}/restrictions", "Account", null);
        await run.Fixture.DrainAsync(source: true);
        await run.DeliverAsync();
        Assert.Equal("Security", await run.Fixture.MerchantScalarAsync(
            $"SELECT restriction FROM account_license_restrictions WHERE account_id = '{run.Account:D}'"));
        var suspended = Assert.Single((await ReadAsync(security, run.Account)).GetProperty("devices").EnumerateArray());
        Assert.Equal((true, end, 2), (suspended.GetProperty("outstanding").GetBoolean(),
            suspended.GetProperty("knownPermitEnd").GetDateTimeOffset(), suspended.GetProperty("outstandingPermits").GetInt32()));
        var applied = (long)(await run.Fixture.MerchantScalarAsync(
            $"SELECT revision FROM account_license_restrictions WHERE account_id = '{run.Account:D}'"))!;
        Assert.True(suspended.GetProperty("accountRevision").GetInt64() < applied,
            "The permit predates the suspension; its revision shows that.");

        // After the suspension the merchant issues no new permit, so nothing extends the known end.
        var refused = await Run.StreamAsync(run.Owner, Device, first.LeaseId, first.GrantId);
        Assert.Equal("refused", refused.GetProperty("status").GetString());
        Assert.Equal(2, (await run.MerchantObservationsAsync()).Length);
    }
}
