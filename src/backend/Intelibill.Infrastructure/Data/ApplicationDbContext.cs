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

    public DbSet<BatchExpiryRevision> BatchExpiryRevisions => Set<BatchExpiryRevision>();
    public DbSet<ShopExpiryState> ShopExpiryStates => Set<ShopExpiryState>();
    public DbSet<ExpiryPolicyRevision> ExpiryPolicyRevisions => Set<ExpiryPolicyRevision>();

    public DbSet<User> Users => Set<User>();
    public DbSet<Shop> Shops => Set<Shop>();
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
    public DbSet<ShopMembership> ShopMemberships => Set<ShopMembership>();
    public DbSet<ShopInvitation> ShopInvitations => Set<ShopInvitation>();
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
    public DbSet<SaleCancellation> SaleCancellations => Set<SaleCancellation>();
    public DbSet<SaleCancellationLine> SaleCancellationLines => Set<SaleCancellationLine>();
    public DbSet<SaleItem> SaleItems => Set<SaleItem>();
    public DbSet<ReconciliationIssue> ReconciliationIssues => Set<ReconciliationIssue>();
    public DbSet<SaleReturn> SaleReturns => Set<SaleReturn>();
    public DbSet<SaleReturnItem> SaleReturnItems => Set<SaleReturnItem>();
    public DbSet<SaleReturnSettlement> SaleReturnSettlements => Set<SaleReturnSettlement>();
    public DbSet<InvoiceSequence> InvoiceSequences => Set<InvoiceSequence>();
    public DbSet<OfflineSubmission> OfflineSubmissions => Set<OfflineSubmission>();
    public DbSet<ReconciliationCaseEvent> ReconciliationCaseEvents => Set<ReconciliationCaseEvent>();
    public DbSet<InvoiceLease> InvoiceLeases => Set<InvoiceLease>();
    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<RealtimeConnectionLease> RealtimeConnectionLeases => Set<RealtimeConnectionLease>();
    public DbSet<ItemBarcodeSequence> ItemBarcodeSequences => Set<ItemBarcodeSequence>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<ServiceCodeSequence> ServiceCodeSequences => Set<ServiceCodeSequence>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();
    public DbSet<DiscountRule> DiscountRules => Set<DiscountRule>();
    public DbSet<HsnCache> HsnCaches => Set<HsnCache>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<PurchaseOrderSequence> PurchaseOrderSequences => Set<PurchaseOrderSequence>();
    public DbSet<PurchaseOrderReceipt> PurchaseOrderReceipts => Set<PurchaseOrderReceipt>();
    public DbSet<PurchaseOrderReceiptLine> PurchaseOrderReceiptLines => Set<PurchaseOrderReceiptLine>();
    public DbSet<PurchaseOrderReceiptSequence> PurchaseOrderReceiptSequences => Set<PurchaseOrderReceiptSequence>();
    public DbSet<CreditNote> CreditNotes => Set<CreditNote>();
    public DbSet<CreditNoteRedemption> CreditNoteRedemptions => Set<CreditNoteRedemption>();
    public DbSet<OperationReceipt> OperationReceipts => Set<OperationReceipt>();
    public DbSet<StockCount> StockCounts => Set<StockCount>();
    public DbSet<PurchaseReceiptValue> PurchaseReceiptValues => Set<PurchaseReceiptValue>();
    public DbSet<PurchaseReceiptCostAdjustment> PurchaseReceiptCostAdjustments => Set<PurchaseReceiptCostAdjustment>();
    public DbSet<BatchPriceRevision> BatchPriceRevisions => Set<BatchPriceRevision>();
    public DbSet<PaymentReceipt> PaymentReceipts => Set<PaymentReceipt>();
    public DbSet<PaymentAllocation> PaymentAllocations => Set<PaymentAllocation>();
    public DbSet<PaymentReversal> PaymentReversals => Set<PaymentReversal>();
    public DbSet<PostingPeriod> PostingPeriods => Set<PostingPeriod>();
    public DbSet<PostingPeriodEvent> PostingPeriodEvents => Set<PostingPeriodEvent>();

    public DbSet<SupplierReturn> SupplierReturns => Set<SupplierReturn>();
    public DbSet<SupplierReturnLine> SupplierReturnLines => Set<SupplierReturnLine>();
    public DbSet<SupplierReturnCredit> SupplierReturnCredits => Set<SupplierReturnCredit>();
    public DbSet<SupplierReturnCreditLine> SupplierReturnCreditLines => Set<SupplierReturnCreditLine>();
    public DbSet<PurchaseReceiptReversal> PurchaseReceiptReversals => Set<PurchaseReceiptReversal>();

    public DbSet<Till> Tills => Set<Till>();
    public DbSet<TillSession> TillSessions => Set<TillSession>();
    public DbSet<TillSourceRecord> TillSourceRecords => Set<TillSourceRecord>();
    public DbSet<TillMovement> TillMovements => Set<TillMovement>();
    public DbSet<TillClose> TillCloses => Set<TillClose>();
    public DbSet<TillCloseReview> TillCloseReviews => Set<TillCloseReview>();
    public DbSet<TillLatePosting> TillLatePostings => Set<TillLatePosting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        if (Database.IsNpgsql())
        {
            // PostgreSQL's xmin changes on every write of the row, so every update of either row is
            // checked against the version it was read at, and a stale write is refused.
            modelBuilder.Entity<Inventory>()
                .Property(i => i.Version)
                .IsRowVersion()
                .HasColumnName("xmin")
                .HasColumnType("xid");

            modelBuilder.Entity<InventoryBatch>()
                .Property(b => b.Version)
                .IsRowVersion()
                .HasColumnName("xmin")
                .HasColumnType("xid");

            modelBuilder.Entity<PurchaseReceiptValue>()
                .Property(r => r.Version)
                .IsRowVersion()
                .HasColumnName("xmin")
                .HasColumnType("xid");

            modelBuilder.Entity<TillSession>().Property(s => s.Version).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");

            modelBuilder.Entity<Sale>().Property(s => s.Version).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");

            modelBuilder.Entity<SupplierReturn>().Property(r => r.Version).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");

            modelBuilder.Entity<PostingPeriod>()
                .Property(p => p.Version)
                .IsRowVersion()
                .HasColumnName("xmin")
                .HasColumnType("xid");

            modelBuilder.Entity<ReconciliationIssue>()
                .Property(i => i.Version)
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
