using InventoryAI.Domain.Common;

namespace InventoryAI.Domain.Events;

public sealed record PasswordResetRequestedEvent(Guid UserId, string Email, string ResetLink) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
}
