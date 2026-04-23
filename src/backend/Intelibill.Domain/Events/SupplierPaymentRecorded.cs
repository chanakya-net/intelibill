using Intelibill.Domain.Common;

namespace Intelibill.Domain.Events;

public sealed record SupplierPaymentRecorded(
    Guid ShopId,
    Guid SupplierId,
    decimal Amount,
    DateOnly EntryDate,
    Guid SupplierLedgerEntryId,
    Guid CreatedBy,
    string? Notes) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
}
