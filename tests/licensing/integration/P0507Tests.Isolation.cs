using System.Net;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0507Tests
{
    [Fact]
    public async Task UnrelatedAccountShopAndDeviceEvidenceIsDenied()
    {
        await using var run = await Run.StartAsync(database);
        var (other, otherAccount) = await Run.OpenShopAsync(run.Fixture, run.Clock.Now);
        await run.IssuePermitAsync(Device);
        await Run.IssuePermitAsync(other, Device);
        await run.DeliverAsync();
        var mine = (await run.MerchantObservationsAsync())[0];

        // Evidence is filed only under the shop's own current binding.
        var forged = mine with { EventId = Guid.NewGuid(), Permit = mine.Permit! with { PermitId = Guid.NewGuid() } };
        foreach (var unrelated in new[]
        {
            forged with { AccountId = otherAccount },
            forged with { AffiliationRequestId = Guid.NewGuid() },
            forged with { ShopId = Guid.NewGuid() },
            forged with { ShopId = other.ShopId },
        })
        {
            using var refused = await run.PostAsync(unrelated);
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        }
        var body = JsonSerializer.SerializeToUtf8Bytes(forged, EntitlementSnapshotsV1.Json);
        using var unsigned = await run.Fixture.LicensingApi.SendAsync(P0213Transport.Request(body, OfflineDeviceObservationsV1.Path, null));
        using var wrongPurpose = await run.Fixture.LicensingApi.SendAsync(await run.Fixture.Transport.SignedAsync(body,
            run.Fixture.AuthClock, merchant: true, path: OfflineDeviceObservationsV1.Path, purpose: SignedServiceAssertion.LicensingDelivery));
        using var wrongKey = await run.Fixture.LicensingApi.SendAsync(await run.Fixture.Transport.SignedAsync(body,
            run.Fixture.AuthClock, merchant: false, path: OfflineDeviceObservationsV1.Path));
        Assert.Equal((HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized),
            (unsigned.StatusCode, wrongPurpose.StatusCode, wrongKey.StatusCode));
        Assert.Equal(2L, await run.SourceCountAsync("offline_device_observations"));
        Assert.Equal(2L, await run.SourceCountAsync("entitlement_merchant_inbox", "kind = 'DeviceObservation'"));

        // Each account sees its own shop's device and nothing of the other's, even for the same device name.
        using var support = await P0417Tests.SignInAsync(run.Fixture, "support");
        var own = Assert.Single((await ReadAsync(support, run.Account)).GetProperty("devices").EnumerateArray());
        var theirs = Assert.Single((await ReadAsync(support, otherAccount)).GetProperty("devices").EnumerateArray());
        Assert.Equal((run.Owner.ShopId, other.ShopId), (own.GetProperty("shopId").GetGuid(), theirs.GetProperty("shopId").GetGuid()));
        Assert.NotEqual(own.GetProperty("deviceRef").GetString(), theirs.GetProperty("deviceRef").GetString());
        Assert.Single((await ReadAsync(support, run.Account, run.Owner.ShopId)).GetProperty("devices").EnumerateArray());
        using var foreignShop = await support.SendAsync(HttpMethod.Get, Route(run.Account, other.ShopId));
        Assert.Equal((HttpStatusCode.NotFound, "Status.ShopNotFound"), (foreignShop.StatusCode, await P0206Fixture.CodeAsync(foreignShop)));
        Assert.Empty((await ReadAsync(support, Guid.NewGuid())).GetProperty("devices").EnumerateArray());

        await AssertCapabilitiesAsync(run);
        await AssertStorageIsolationAsync(run);
    }

    private static async Task AssertCapabilitiesAsync(Run run)
    {
        using var platform = await run.Fixture.Trial.Catalogue.SignInOwnerAsync();
        using var billing = await P0417Tests.SignInAsync(run.Fixture, "billing", platform);
        foreach (var denied in new[] { billing, platform })
        {
            using var response = await denied.SendAsync(HttpMethod.Get, Route(run.Account));
            Assert.Equal((HttpStatusCode.Forbidden, "Status.AccessDenied"), (response.StatusCode, await P0206Fixture.CodeAsync(response)));
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        }
        foreach (var capability in new[] { "licensing-manager", "security-administrator" })
        {
            using var allowed = await P0417Tests.SignInAsync(run.Fixture, capability, platform);
            Assert.Single((await ReadAsync(allowed, run.Account)).GetProperty("devices").EnumerateArray());
            if (capability == "security-administrator")
            {
                // Security Administrator reaches device permits without gaining commercial status or closure reads.
                foreach (var deniedPath in new[] { $"/api/admin/accounts/{run.Account:D}/status",
                    $"/api/admin/accounts/{run.Account:D}/closures" })
                {
                    using var deniedStatus = await allowed.SendAsync(HttpMethod.Get, deniedPath);
                    Assert.Equal((HttpStatusCode.Forbidden, "Status.AccessDenied"),
                        (deniedStatus.StatusCode, await P0206Fixture.CodeAsync(deniedStatus)));
                }
            }
        }

        // No provider session at all is refused before anything is read.
        using var anonymous = new P0110CookieBrowser(run.Fixture.Trial.Catalogue.Api.CreateClient());
        using var unauthorized = await anonymous.SendAsync(HttpMethod.Get, Route(run.Account));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
    }

    private static async Task AssertStorageIsolationAsync(Run run)
    {
        // Without an account scope the runtime role reads nothing; the evidence cannot be rewritten by anyone.
        Assert.Equal(0L, await LicensingPostgreSqlFixture.ReadScalarAsync(run.Fixture.Hosts.Fixture.Runtime,
            "SELECT count(*) FROM public.offline_device_observations"));
        foreach (var statement in new[] { "UPDATE offline_device_observations SET valid_until = valid_until + interval '1 day'",
            "DELETE FROM offline_device_observations", "TRUNCATE offline_device_observations" })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() =>
                LicensingPostgreSqlFixture.ExecuteAsync(run.Fixture.Hosts.LicensingOwner, statement));
            Assert.Equal("55000", error.SqlState);
        }
    }
}
