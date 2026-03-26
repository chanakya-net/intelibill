using Intelibill.Domain.Common;

namespace Intelibill.Domain.Events;

public sealed record UserRegisteredEvent(Guid UserId, string? Email) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
}
