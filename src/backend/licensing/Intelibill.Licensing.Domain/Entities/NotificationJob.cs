using Intelibill.Licensing.Domain.Policies;

namespace Intelibill.Licensing.Domain.Entities;

/// <summary>
/// Durable planned notification intent (the licensing outbox intent). One row exists per
/// semantic key <c>(account_id, scope_kind, scope_id, recipient_key, purpose, source_kind,
/// source_id, source_revision, threshold)</c>; delivery transport and send-time invalidation
/// belong to later slices.
/// </summary>
public sealed class NotificationJob
{
    private NotificationJob()
    {
    }

    private NotificationJob(
        Guid id,
        Guid accountId,
        string scopeKind,
        Guid scopeId,
        string recipientKey,
        Guid? recipientUserId,
        Guid? contactId,
        string audience,
        string purpose,
        string sourceKind,
        Guid sourceId,
        string sourceRevision,
        string threshold,
        DateTimeOffset dueAt,
        DateTimeOffset effectiveDeadlineAt,
        string timeZone,
        string locale,
        string[] allowedChannels,
        string status,
        string templateKey,
        string templateArguments,
        string? protectedDestination,
        string? destinationHint,
        DateTimeOffset plannedAt,
        DateTimeOffset createdAt)
    {
        Id = id;
        AccountId = accountId;
        ScopeKind = scopeKind;
        ScopeId = scopeId;
        RecipientKey = recipientKey;
        RecipientUserId = recipientUserId;
        ContactId = contactId;
        Audience = audience;
        Purpose = purpose;
        SourceKind = sourceKind;
        SourceId = sourceId;
        SourceRevision = sourceRevision;
        Threshold = threshold;
        DueAt = dueAt;
        EffectiveDeadlineAt = effectiveDeadlineAt;
        TimeZone = timeZone;
        Locale = locale;
        AllowedChannels = allowedChannels;
        Status = status;
        TemplateKey = templateKey;
        TemplateArguments = templateArguments;
        ProtectedDestination = protectedDestination;
        DestinationHint = destinationHint;
        PlannedAt = plannedAt;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid AccountId { get; private set; }
    public string ScopeKind { get; private set; } = string.Empty;
    public Guid ScopeId { get; private set; }
    public string RecipientKey { get; private set; } = string.Empty;
    public Guid? RecipientUserId { get; private set; }
    public Guid? ContactId { get; private set; }
    public string Audience { get; private set; } = string.Empty;
    public string Purpose { get; private set; } = string.Empty;
    public string SourceKind { get; private set; } = string.Empty;
    public Guid SourceId { get; private set; }
    public string SourceRevision { get; private set; } = string.Empty;
    public string Threshold { get; private set; } = string.Empty;
    public DateTimeOffset DueAt { get; private set; }
    public DateTimeOffset EffectiveDeadlineAt { get; private set; }
    public string TimeZone { get; private set; } = BillingCalendar.Zone;
    public string Locale { get; private set; } = "en-IN";
    public string[] AllowedChannels { get; private set; } = [];
    public string Status { get; private set; } = "Scheduled";
    public string TemplateKey { get; private set; } = string.Empty;
    public string TemplateArguments { get; private set; } = "{}";
    public string? ProtectedDestination { get; private set; }
    public string? DestinationHint { get; private set; }
    public DateTimeOffset PlannedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static NotificationJob Schedule(
        Guid accountId,
        string scopeKind,
        Guid scopeId,
        string recipientKey,
        Guid? recipientUserId,
        Guid? contactId,
        string audience,
        string purpose,
        string sourceKind,
        Guid sourceId,
        string sourceRevision,
        string threshold,
        DateTimeOffset dueAt,
        DateTimeOffset effectiveDeadlineAt,
        string locale,
        string[] allowedChannels,
        string templateKey,
        string templateArguments,
        string? protectedDestination,
        string? destinationHint,
        DateTimeOffset plannedAt) =>
        new(
            Guid.CreateVersion7(plannedAt),
            accountId,
            scopeKind,
            scopeId,
            recipientKey,
            recipientUserId,
            contactId,
            audience,
            purpose,
            sourceKind,
            sourceId,
            sourceRevision,
            threshold,
            dueAt,
            effectiveDeadlineAt,
            BillingCalendar.Zone,
            locale,
            allowedChannels,
            "Scheduled",
            templateKey,
            templateArguments,
            protectedDestination,
            destinationHint,
            plannedAt,
            plannedAt);
}

/// <summary>
/// Account-approved commercial notice contact with separate verification, opt-in and revocation.
/// Grants no billing account membership, no login, and no merchant shop access.
/// </summary>
public sealed class BillingNoticeContact
{
    private BillingNoticeContact()
    {
    }

    public Guid Id { get; private set; }
    public Guid AccountId { get; private set; }
    public Guid? MerchantUserId { get; private set; }
    public byte[] DestinationHash { get; private set; } = [];
    public string ProtectedDestination { get; private set; } = string.Empty;
    public string DestinationHint { get; private set; } = string.Empty;
    public string Locale { get; private set; } = "en-IN";
    public Guid ApprovedByActorId { get; private set; }
    public DateTimeOffset ApprovedAt { get; private set; }
    public Guid OperationKey { get; private set; }
    public byte[]? VerificationTokenHash { get; private set; }
    public DateTimeOffset? VerificationExpiresAt { get; private set; }
    public DateTimeOffset? VerifiedAt { get; private set; }
    public DateTimeOffset? OptedInAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public Guid? RevokedByActorId { get; private set; }
    public int Revision { get; private set; }

    public bool IsEligible =>
        VerifiedAt is not null && OptedInAt is not null && RevokedAt is null;
}

/// <summary>Append-only immutable consent lifecycle history for billing notice contacts.</summary>
public sealed class BillingNoticeContactEvent
{
    private BillingNoticeContactEvent()
    {
    }

    public Guid Id { get; private set; }
    public Guid ContactId { get; private set; }
    public Guid AccountId { get; private set; }
    public string EventKind { get; private set; } = string.Empty;
    public Guid? ActorId { get; private set; }
    public int Revision { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
}
