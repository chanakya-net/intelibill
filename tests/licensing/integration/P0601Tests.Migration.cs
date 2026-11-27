using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0601Tests
{
    /// <summary>
    /// A retained capacity quote sits in a forced-RLS table whose rows refuse update, delete and truncate, and taking
    /// it leaves the purchased term and its payment evidence untouched.
    /// </summary>
    [Fact]
    public async Task CapacityQuoteEvidenceIsImmutableAndPreservesPurchasedEvidence()
    {
        await using var fixture = await StartAsync(database, January31);
        var package = await fixture.PackageAsync(2, ShopNames);
        var (_, term) = await PurchaseAsync(package, "Month");
        var before = await EvidenceAsync(fixture, term);
        Assert.Equal(true, await fixture.ScalarAsync(
            "SELECT relforcerowsecurity FROM pg_class WHERE relname = 'capacity_change_quotes' AND relnamespace = 'public'::regnamespace"));

        fixture.At(February14);
        var quote = await QuoteAsync(package, term, Request(package.Plan, 4, 3, February29));
        Assert.Equal("Payable", quote.GetProperty("outcome").GetString());
        foreach (var statement in new[]
        {
            "UPDATE public.capacity_change_quotes SET amount_paise = 1",
            "DELETE FROM public.capacity_change_quote_shops",
            "TRUNCATE public.capacity_change_quotes CASCADE",
        })
            Assert.Equal("55000", (await Assert.ThrowsAsync<PostgresException>(() =>
                LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, statement))).SqlState);
        Assert.Equal(1L, await fixture.CountAsync("capacity_change_quotes", $"term_id = '{term:D}' AND amount_paise = 7759"));
        Assert.Equal(before, await EvidenceAsync(fixture, term));
    }
}
