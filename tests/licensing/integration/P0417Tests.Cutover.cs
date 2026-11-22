using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0417Tests
{
    [Fact]
    public async Task ReopeningCutoverFencesLegacyPublishersBeforeReviewsAndPreservesLaterSuspension()
    {
        var clock = new P0201Clock { Now = P0202Fixture.WholeSecondNow() };
        await using var fixture = await P0213Fixture.StartAsync(database, businessClock: clock, reopeningCutover: false);
        var (owner, account, route) = await CloseAsync(fixture, clock.Now);
        using var manager = await SignInAsync(fixture);
        using var unavailable = await P0212Fixture.PostAsync(manager, route + "/preview", new { });
        Assert.Equal("Reopening.PublisherCutoverRequired", await P0206Fixture.CodeAsync(unavailable));
        using var refused = await P0212Fixture.PostAsync(manager, route,
            new ReopenRequest("Review", true, "CUTOVER-FIXTURE", owner.UserId, clock.Now, "Owner requested reopening",
                1, new string('a', 64), Guid.NewGuid()));
        Assert.Equal("Reopening.PublisherCutoverRequired", await P0206Fixture.CodeAsync(refused));
        await LegacyPublishAsync(fixture, account, owner, clock.Now, accepted: true);
        Assert.Equal("Closed", (await fixture.LatestSnapshotAsync(owner.ShopId)).Shop?.State);
        await DrainBothAsync(fixture);
        await ActivateAsync(fixture, owner);

        await AssertRuntimeCannotEnableCutoverAsync(fixture);
        await P0213Fixture.EnableReopeningAsync(fixture.Hosts.LicensingOwner);
        await LegacyPublishAsync(fixture, account, owner, clock.Now, accepted: false);
        using var reopened = await P0212Fixture.PostAsync(manager, route, Command(await PreviewAsync(manager, route), owner));
        Assert.True(reopened.StatusCode == HttpStatusCode.Created, await reopened.Content.ReadAsStringAsync());
        await DrainBothAsync(fixture);
        await AssertMerchantStatusAsync(owner, received: true, enforced: true);

        await LegacyPublishAsync(fixture, account, owner, clock.Now, accepted: false);
        await AssertLaterSuspensionAsync(fixture, owner, account);
    }

    private static async Task AssertRuntimeCannotEnableCutoverAsync(P0213Fixture fixture)
    {
        var denied = await Assert.ThrowsAsync<PostgresException>(() => LicensingPostgreSqlFixture.ExecuteAsync(
            fixture.Hosts.Fixture.Runtime, "UPDATE public.reopening_publisher_cutover SET enabled = true"));
        Assert.Equal("42501", denied.SqlState);
    }

    private static async Task AssertLaterSuspensionAsync(P0213Fixture fixture, Merchant owner, Guid account)
    {
        using var security = await SignInAsync(fixture, "security-administrator");
        await P0404Tests.SuspendAsync(security, $"/api/admin/accounts/{account:D}/restrictions", "Shop", owner.ShopId);
        Assert.Equal("Security", (await fixture.LatestSnapshotAsync(owner.ShopId)).Shop?.Suspension?.Mode);
        await DrainBothAsync(fixture);
        using var blocked = await owner.Client.GetAsync("/api/sales");
        Assert.Equal("Licence.SecuritySuspended", await P0206Fixture.CodeAsync(blocked));
    }

    private static async Task LegacyPublishAsync(P0213Fixture fixture, Guid account, Merchant owner, DateTimeOffset now, bool accepted)
    {
        var before = await fixture.LatestSnapshotAsync(owner.ShopId);
        var db = fixture.Hosts.Fixture.Runtime;
        if (Environment.GetEnvironmentVariable("P0417_LEGACY_PROBE_DLL") is { Length: > 0 } probe)
        {
            // The dedicated cutover run uses the actual baseline assembly against these same databases.
            await RunLegacyPublisherAsync(probe, db.ConnectionString, account, owner.ShopId, now, accepted);
        }
        else
        {
            var error = await Record.ExceptionAsync(() => TryLegacyInsertAsync(db, account, owner.ShopId, before.EventId));
            if (accepted) Assert.Null(error);
            else Assert.Equal("55000", Assert.IsType<PostgresException>(error).SqlState);
        }
        var after = await fixture.LatestSnapshotAsync(owner.ShopId);
        if (!accepted) Assert.Equal(before, after);
    }

    private static async Task RunLegacyPublisherAsync(string probe, string connection, Guid account, Guid shop, DateTimeOffset now, bool accepted)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        start.ArgumentList.Add(probe);
        using var process = Process.Start(start)!;
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new
        {
            connection, account, shop, now = now.ToString("O", CultureInfo.InvariantCulture),
        }));
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync(timeout.Token);
        var result = await stdout;
        Assert.True(process.ExitCode == (accepted ? 0 : 42), result + await stderr);
        Assert.Contains(accepted ? "legacy publication committed" : "legacy publication rejected: 55000", result, StringComparison.Ordinal);
    }

    private static Task TryLegacyInsertAsync(NpgsqlConnectionStringBuilder db, Guid account, Guid shop, Guid prior) =>
        LicensingPostgreSqlFixture.ExecuteAsync(db, $"""
            BEGIN;
            SELECT set_config('app.billing_account_id', '{account:D}', true);
            INSERT INTO public.entitlement_messages (event_id, account_id, shop_id, kind, payload, digest)
                SELECT gen_random_uuid(), account_id, shop_id, kind, payload, digest
                FROM public.entitlement_messages WHERE event_id = '{prior:D}'
                RETURNING set_config('fixture.legacy_event', event_id::text, true);
            INSERT INTO public.entitlement_snapshots
                (event_id, account_id, shop_id, affiliation_request_id, account_revision, shop_revision)
                SELECT current_setting('fixture.legacy_event')::uuid, account_id, '{shop:D}', affiliation_request_id,
                    account_revision, shop_revision + 1
                FROM public.entitlement_snapshots WHERE event_id = '{prior:D}';
            ROLLBACK;
            """);
}
