using System.Net;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0608Tests
{
    [Fact]
    public async Task RefundRequestEvidenceIsImmutableAndPreservesPaymentEvidence()
    {
        await using var fixture = await StartAsync(database, RequestedInstant);
        var package = await fixture.PackageAsync(1, ["Migration Shop"]);
        var (purchase, term) = await PurchaseAsync(package, "Month");
        await DrainAsync(fixture);
        var confirmation = await ConfirmationIdAsync(package.Payer.Client, package.Account, purchase.OrderId);
        var before = await SnapshotAsync(fixture, package, term);
        Assert.Equal(true, await fixture.ScalarAsync(
            "SELECT relforcerowsecurity FROM pg_class WHERE relname = 'refund_requests' AND relnamespace = 'public'::regnamespace"));

        using var created = await PostRefundAsync(package.Payer.Client, package.Account,
            RequestBody(purchase.OrderId, confirmation, 3500L, "Retained refund request"));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        foreach (var statement in new[]
        {
            "UPDATE public.refund_requests SET amount_paise = 1",
            "DELETE FROM public.refund_requests",
            "TRUNCATE public.refund_requests CASCADE",
        })
        {
            var refused = await Assert.ThrowsAsync<PostgresException>(() =>
                LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, statement));
            Assert.Equal("55000", refused.SqlState);
        }

        Assert.Equal(1L, await fixture.CountAsync("refund_requests", $"confirmation_id = '{confirmation:D}' AND amount_paise = 3500"));
        Assert.Equal(before, await SnapshotAsync(fixture, package, term));
    }
}
