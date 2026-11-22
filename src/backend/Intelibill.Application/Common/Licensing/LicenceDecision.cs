using ErrorOr;

namespace Intelibill.Application.Common.Licensing;

public enum LicenceOutcome
{
    Allowed,
    RequiresOwnerSetup,
    RequiresProvenSource,
    Blocked,
    /// <summary>Only identity, contact and recovery necessities qualify.</summary>
    RequiresRecoveryNecessity,
}

/// <summary>Matrix result. A result is advisory while the shop is observe-only.</summary>
public readonly record struct LicenceDecision(LicenceOutcome Outcome, bool Enforced, Error? Denial = null,
    DateTimeOffset? AuthorityVerifiedAt = null, DateTimeOffset? ValidUntil = null)
{
    /// <summary>Earliest proven commercial or trading restriction already in effect, if known.</summary>
    public DateTimeOffset? SourceCompletionCutoff { get; init; }

    public Error? OperatingRefusal { get; init; }
    public string Status { get; init; } = "Unknown";
}
