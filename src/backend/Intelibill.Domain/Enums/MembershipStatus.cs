namespace Intelibill.Domain.Enums;

/// <summary>
/// Lifecycle of one shop membership. Suspension and removal are shop-scoped: they end access to
/// that shop without touching the global account, which may own or staff other shops. Removed rows
/// are retained for audit rather than deleted.
/// </summary>
public enum MembershipStatus
{
    Active = 1,
    Suspended = 2,
    Removed = 3,
}
