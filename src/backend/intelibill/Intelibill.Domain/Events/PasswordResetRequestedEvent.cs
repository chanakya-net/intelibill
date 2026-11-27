using Intelibill.Domain.Common;

namespace Intelibill.Domain.Events;

public sealed record PasswordResetRequestedEvent(Guid UserId, string Email, string ResetLink) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
}
