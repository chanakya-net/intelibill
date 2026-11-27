using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0308Tests
{
    [Fact]
    public async Task RenewalFulfilmentEvidenceIsRetainedByTheDatabase()
    {
        await using var fixture = await StartAsync(database, March31);
        var package = await fixture.PackageAsync(1);
        var (_, first) = await PurchaseAsync(package, "Month");
        fixture.At(Utc("2027-04-20T06:00:00Z"));
        var pending = await AcceptAsync(package, await RenewalQuoteAsync(package, first, "Month"));
        Assert.Equal(1L, await fixture.CountAsync("payment_fulfilments", "predecessor_term_id IS NULL"));

        var paid = await PayAsync(package, pending);
        P0302Fixture.AssertStatus(paid, "Verified", "Fulfilled", "Applying");
        Assert.Equal(1L, await fixture.CountAsync("payment_fulfilments", $"predecessor_term_id = '{first:D}'"));
        foreach (var statement in new[]
        {
            "UPDATE public.payment_fulfilments SET grace_days_included = 0",
            "DELETE FROM public.payment_fulfilments WHERE predecessor_term_id IS NOT NULL",
        })
            Assert.Equal("55000", (await Assert.ThrowsAsync<PostgresException>(() =>
                LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, statement))).SqlState);
    }
}
