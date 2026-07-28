using Intelibill.Api.Startup;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Intelibill.Api.Unit.Tests.Startup;

public sealed class ApplicationModelWarmupServiceTests
{
    [Fact]
    public async Task StartAsync_BuildsTheApplicationModelWithoutSaving()
    {
        var modelBuilds = 0;
        var saves = 0;
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var services = new ServiceCollection();
        services.AddScoped<ApplicationDbContext>(_ =>
            new TrackingApplicationDbContext(
                options,
                () => modelBuilds++,
                () => saves++));
        services.AddApplicationModelWarmup();

        await using var provider = services.BuildServiceProvider();
        var warmup = Assert.Single(
            provider.GetServices<IHostedService>(),
            service => service is ApplicationModelWarmupService);

        await warmup.StartAsync(CancellationToken.None);

        Assert.Equal(1, modelBuilds);
        Assert.Equal(0, saves);
    }

    private sealed class TrackingApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        Action onModelCreating,
        Action onSave) : ApplicationDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            onModelCreating();
            base.OnModelCreating(modelBuilder);
        }

        public override int SaveChanges()
        {
            onSave();
            return base.SaveChanges();
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            onSave();
            return base.SaveChangesAsync(cancellationToken);
        }
    }
}
