using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Intelibill.Integration.Tests;

public sealed class ApiWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public ApiWebApplicationFactory()
    {
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:BaseUrl"] = "https://inventory.test",
                ["Database:Host"] = "localhost",
                ["Database:Port"] = "5432",
                ["Database:Database"] = "integration",
                ["Database:Username"] = "integration",
                ["Database:Password"] = "integration",
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

            services.AddSingleton(_connection);
            services.AddDbContext<ApplicationDbContext>((sp, options) =>
            {
                var connection = sp.GetRequiredService<SqliteConnection>();
                options.UseSqlite(connection)
                    .UseSnakeCaseNamingConvention()
                    .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
            });

            // SQLite cannot translate DateTimeOffset.UtcNow in WHERE clauses.
            // Replace the repository with a SQLite-compatible version that evaluates
            // the expiry filter client-side.
            services.RemoveAll<IRefreshTokenRepository>();
            services.AddScoped<IRefreshTokenRepository, SqliteRefreshTokenRepository>();
            services.RemoveAll<ISupplierLedgerEntryRepository>();
            services.AddScoped<ISupplierLedgerEntryRepository, SqliteSupplierLedgerEntryRepository>();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }
}

/// <summary>
/// SQLite-compatible refresh token repository that evaluates the DateTimeOffset
/// expiry filter on the client side, since SQLite cannot translate DateTimeOffset.UtcNow
/// to SQL in WHERE clauses.
/// </summary>
internal sealed class SqliteRefreshTokenRepository(ApplicationDbContext context)
    : RepositoryBase<RefreshToken>(context), IRefreshTokenRepository
{
    public async Task<RefreshToken?> GetActiveByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        var tokens = await DbSet
            .Include(rt => rt.User)
            .ThenInclude(u => u.ShopMemberships)
            .ThenInclude(sm => sm.Shop)
            .Where(rt => rt.Token == token && !rt.IsRevoked)
            .ToListAsync(cancellationToken);

        return tokens.FirstOrDefault(rt => rt.ExpiresAt > DateTimeOffset.UtcNow);
    }

    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var tokens = await DbSet
            .Where(rt => rt.UserId == userId && !rt.IsRevoked)
            .ToListAsync(cancellationToken);

        foreach (var t in tokens)
            t.Revoke();
    }
}

/// <summary>
/// SQLite-compatible supplier ledger entry repository that evaluates DateTimeOffset
/// ordering on the client side, since SQLite cannot translate DateTimeOffset in ORDER BY clauses.
/// </summary>
internal sealed class SqliteSupplierLedgerEntryRepository(ApplicationDbContext context)
    : RepositoryBase<SupplierLedgerEntry>(context), ISupplierLedgerEntryRepository
{
    public async Task<IReadOnlyList<SupplierLedgerEntry>> GetBySupplierAsync(Guid shopId, Guid supplierId, CancellationToken cancellationToken = default)
    {
        var entries = await DbSet
            .Where(e => e.ShopId == shopId && e.SupplierId == supplierId)
            .ToListAsync(cancellationToken);

        return entries
            .OrderByDescending(e => e.EntryDate)
            .ThenByDescending(e => e.CreatedAt)
            .ToList();
    }

    public async Task<IReadOnlyList<SupplierLedgerEntry>> GetByBatchAsync(Guid shopId, Guid batchId, CancellationToken cancellationToken = default)
    {
        var entries = await DbSet
            .Where(e => e.ShopId == shopId && e.BatchId == batchId)
            .ToListAsync(cancellationToken);

        return entries
            .OrderByDescending(e => e.EntryDate)
            .ThenByDescending(e => e.CreatedAt)
            .ToList();
    }

    public async Task<decimal> GetSupplierBalanceAsync(Guid shopId, Guid supplierId, CancellationToken cancellationToken = default)
    {
        var goodsReceived = await DbSet
            .Where(e => e.ShopId == shopId && e.SupplierId == supplierId && e.EntryType == Intelibill.Domain.Enums.SupplierLedgerEntryType.GoodsReceived)
            .SumAsync(e => e.Amount, cancellationToken);

        var paymentMade = await DbSet
            .Where(e => e.ShopId == shopId && e.SupplierId == supplierId && e.EntryType == Intelibill.Domain.Enums.SupplierLedgerEntryType.PaymentMade)
            .SumAsync(e => e.Amount, cancellationToken);

        return goodsReceived - paymentMade;
    }
}
