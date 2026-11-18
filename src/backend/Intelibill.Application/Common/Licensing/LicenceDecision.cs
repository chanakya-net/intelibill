using ErrorOr;

namespace Intelibill.Application.Common.Licensing;

public enum LicenceOutcome
{
    Allowed,
    RequiresOwnerSetup,
    RequiresProvenSource,
    Blocked,
}

/// <summary>Matrix result. A result is advisory while the shop is observe-only.</summary>
public readonly record struct LicenceDecision(LicenceOutcome Outcome, bool Enforced, Error? Denial = null);
