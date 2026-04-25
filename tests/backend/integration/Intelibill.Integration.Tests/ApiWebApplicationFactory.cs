using Intelibill.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;

namespace Intelibill.Integration.Tests;

public sealed class ApiWebApplicationFactory(PostgreSqlTestFixture? fixture = null) : WebApplicationFactory<Program>, IAsyncLifetime
{
    private PostgreSqlContainer? _localContainer;
    private PostgreSqlContainer DbContainer => fixture?.DbContainer ?? _localContainer ?? throw new InvalidOperationException("Container not initialized");

    public async Task InitializeAsync()
    {
        if (fixture == null)
        {
            _localContainer = new PostgreSqlBuilder()
                .WithImage("postgres:17-alpine")
                .WithDatabase("integration")
                .WithUsername("integration")
                .WithPassword("integration")
                .Build();
            await _localContainer.StartAsync();

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(_localContainer.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;

            using var context = new ApplicationDbContext(options);
            await context.Database.MigrateAsync();
        }
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        if (_localContainer != null)
        {
            await _localContainer.StopAsync();
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:BaseUrl"] = "https://inventory.test",
                ["ConnectionStrings:DefaultConnection"] = DbContainer.GetConnectionString(),
                ["Jwt:Secret"] = "integration-secret-key-must-be-at-least-32-chars!",
                ["Jwt:Issuer"] = "inventory.ai.integration",
                ["Jwt:Audience"] = "inventory.ai.integration",
                ["Jwt:AccessTokenExpiryMinutes"] = "15",
                ["Jwt:RefreshTokenExpiryDays"] = "7",
                ["ExternalAuth:Google:Enabled"] = "false",
                ["ExternalAuth:Facebook:Enabled"] = "false"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<ApplicationDbContext>();

            services.AddDbContext<ApplicationDbContext>((_, options) =>
            {
                options.UseNpgsql(DbContainer.GetConnectionString())
                    .UseSnakeCaseNamingConvention()
                    .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
            });

            services.RemoveAll<IDistributedCache>();
            services.AddSingleton<IDistributedCache, NoOpDistributedCache>();
        });
    }
}

internal sealed class NoOpDistributedCache : IDistributedCache
{
    public byte[]? Get(string key) => null;
    public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult<byte[]?>(null);
    public void Refresh(string key) { }
    public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
    public void Remove(string key) { }
    public Task RemoveAsync(string key, CancellationToken token = default) => Task.CompletedTask;
    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) { }
    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => Task.CompletedTask;
}
