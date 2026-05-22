using Intelibill.Application.Common.Interfaces;
using Intelibill.Domain.Common;
using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System.Reflection;
using Wolverine;

namespace Intelibill.Infrastructure.Data;

public class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options,
    ICurrentSessionContext? currentSessionContext = null,
    IMessageBus? messageBus = null) : DbContext(options)
{
    private readonly ICurrentSessionContext? _currentSessionContext = currentSessionContext;
    private readonly IMessageBus? _messageBus = messageBus;

    public DbSet<User> Users => Set<User>();
    public DbSet<Shop> Shops => Set<Shop>();
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
    public DbSet<ShopMembership> ShopMemberships => Set<ShopMembership>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerLedgerEntry> CustomerLedgerEntries => Set<CustomerLedgerEntry>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<Inventory> Inventory => Set<Inventory>();
    public DbSet<InventoryBatch> InventoryBatches => Set<InventoryBatch>();
    public DbSet<InventoryAdjustment> InventoryAdjustments => Set<InventoryAdjustment>();
    public DbSet<StockTransaction> StockTransactions => Set<StockTransaction>();
    public DbSet<SupplierLedgerEntry> SupplierLedgerEntries => Set<SupplierLedgerEntry>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleItem> SaleItems => Set<SaleItem>();
    public DbSet<ReconciliationIssue> ReconciliationIssues => Set<ReconciliationIssue>();
    public DbSet<SaleReturn> SaleReturns => Set<SaleReturn>();
    public DbSet<SaleReturnItem> SaleReturnItems => Set<SaleReturnItem>();
    public DbSet<InvoiceSequence> InvoiceSequences => Set<InvoiceSequence>();
    public DbSet<InvoiceLease> InvoiceLeases => Set<InvoiceLease>();
    public DbSet<UserExternalLogin> UserExternalLogins => Set<UserExternalLogin>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();
    public DbSet<DiscountRule> DiscountRules => Set<DiscountRule>();
    public DbSet<HsnCache> HsnCaches => Set<HsnCache>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        if (Database.IsNpgsql())
        {
            modelBuilder.Entity<Inventory>()
                .Property<uint>("xmin")
                .IsRowVersion()
                .HasColumnName("xmin")
                .HasColumnType("xid");
        }

        base.OnModelCreating(modelBuilder);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateAuditFields();

        // These events are published only after DB commit succeeds.
        // Use them for secondary work like sending receipts, welcome emails, or notifications.
        // Do not use them for core invariants that must commit atomically with primary write.
        var domainEvents = ChangeTracker
            .Entries<BaseEntity>()
            .SelectMany(e => e.Entity.DomainEvents)
            .ToList();

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            entry.Entity.ClearDomainEvents();
        }

        var result = await base.SaveChangesAsync(cancellationToken);

        if (_messageBus is not null)
        {
            foreach (var domainEvent in domainEvents)
            {
                await _messageBus.PublishAsync(domainEvent);
            }
        }

        return result;
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
