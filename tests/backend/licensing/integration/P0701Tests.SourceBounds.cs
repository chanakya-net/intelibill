using System.Data.Common;
using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0701Tests
{
    [Fact]
    public async Task OneCurrentTermInLongHistoryBoundsBothPlannerReadsBeforeRosterExpansion()
    {
        var reads = new NoticeRosterQueries();
        var now = Utc("2028-01-31T04:30:00Z");
        await using var paid = await P0308Fixture.StartAsync(database, now,
            services: services => services.ConfigureDbContext<LicensingDbContext>(opts => opts.AddInterceptors(reads)));
        var package = await paid.PackageAsync(1, ["Ongoing"]);
        var (_, liveTerm) = await PurchaseAsync(package, "Month");
        for (var i = 1; i <= 24; i++)
        {
            var start = Utc("2024-01-01T04:30:00Z").AddMonths(i);
            await SeedTermAsync(paid.Hosts, package.Account, new TermSeed(start, start.AddMonths(1),
                AnchorLocal: start.AddMinutes(330).ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)));
        }
        await P0608Tests.DrainAsync(paid);
        reads.TermPages.Clear();
        Assert.Equal(10, (await RunPlannerPassAsync(paid.Hosts.Licensing.Services, termPageSize: 3)).Inserted);
        Assert.Equal(2, reads.TermPages.Count);
        Assert.All(reads.TermPages, ids => Assert.Equal([liveTerm], ids));
        var manager = await SignInAsync(paid.Hosts);
        await InviteBillingManagerAsync(paid.Hosts.LicensingOwner, package.Payer.Client, package.Account, manager);
        reads.TermPages.Clear();
        Assert.Equal(5, (await RunPlannerPassAsync(paid.Hosts.Licensing.Services, termPageSize: 3)).Inserted);
        Assert.Equal(2, reads.TermPages.Count);
        Assert.All(reads.TermPages, ids => Assert.Equal([liveTerm], ids));
    }

    private sealed class NoticeRosterQueries : DbCommandInterceptor
    {
        public List<Guid[]> TermPages { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("CROSS JOIN LATERAL public.term_effective_roster", StringComparison.Ordinal))
                TermPages.Add(command.Parameters.Cast<DbParameter>().Select(p => p.Value).OfType<Guid[]>().First());
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
