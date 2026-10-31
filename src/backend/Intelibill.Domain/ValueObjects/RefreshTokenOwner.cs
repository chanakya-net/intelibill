namespace Intelibill.Domain.ValueObjects;

/// <summary>
/// Which account and session a presented refresh credential belongs to — enough to take their
/// locks in order, and nothing a decision may rest on, since it is read before any lock is held.
/// </summary>
public sealed record RefreshTokenOwner(Guid TokenId, Guid UserId, Guid? AuthSessionId);
