using Intelibill.Application.Common.Interfaces;
using Intelibill.Domain.Common;
using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace Intelibill.Infrastructure.Data;

public class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options,
    ICurrentSessionContext? currentSessionContext = null) : DbContext(options)
{
    private readonly ICurrentSessionContext? _currentSessionContext = currentSessionContext;

    public DbSet<User> Users => Set<User>();
    public DbSet<Shop> Shops => Set<Shop>();
    public DbSet<ShopMembership> ShopMemberships => Set<ShopMembership>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<Inventory> Inventory => Set<Inventory>();
    public DbSet<InventoryBatch> InventoryBatches => Set<InventoryBatch>();
    public DbSet<StockTransaction> StockTransactions => Set<StockTransaction>();
    public DbSet<SupplierLedgerEntry> SupplierLedgerEntries => Set<SupplierLedgerEntry>();
    public DbSet<UserExternalLogin> UserExternalLogins => Set<UserExternalLogin>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        base.OnModelCreating(modelBuilder);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateAuditFields();
        return await base.SaveChangesAsync(cancellationToken);
    }

    private void UpdateAuditFields()
    {
        var currentUserId = _currentSessionContext?.UserId;

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Added && entry.Metadata.FindProperty("CreatedBy") is not null)
            {
                if (currentUserId.HasValue)
                {
                    entry.Property("CreatedBy").CurrentValue = currentUserId.Value;
                }
            }

            if (entry.State == EntityState.Modified)
            {
                entry.Property(nameof(BaseEntity.UpdatedAt)).CurrentValue = DateTimeOffset.UtcNow;

                if (entry.Metadata.FindProperty("UpdatedBy") is not null)
                {
                    if (currentUserId.HasValue)
                    {
                        entry.Property("UpdatedBy").CurrentValue = currentUserId.Value;
                    }
                }
            }
        }
    }
}
