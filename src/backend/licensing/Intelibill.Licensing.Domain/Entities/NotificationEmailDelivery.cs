namespace Intelibill.Licensing.Domain.Entities;

/// <summary>A single SMTP submission intent. The planned notice remains immutable.</summary>
public sealed class NotificationEmailDelivery
{
    public Guid Id { get; private set; }
    public Guid AccountId { get; private set; }
    public Guid? JobId { get; private set; }
    public Guid? ProofContactId { get; private set; }
    public Guid? AffiliationRequestId { get; private set; }
    public string? ProtectedProof { get; private set; }
    public DateTimeOffset DueAt { get; private set; }
    public DateTimeOffset? NextAttemptAt { get; private set; }
    public string State { get; private set; } = "Pending";
    public int Attempts { get; private set; }
    public Guid? LeaseToken { get; private set; }
    public DateTimeOffset? LeaseExpiresAt { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public DateTimeOffset? OutcomeAt { get; private set; }
}
