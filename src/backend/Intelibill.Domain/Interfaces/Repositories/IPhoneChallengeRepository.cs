using Intelibill.Domain.Entities;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Domain.Interfaces.Repositories;

/// <summary>
/// Phone challenges and the locks that order them. <see cref="BeginAsync"/> takes the same
/// identity lock the email ownership flow takes, so a phone transition and an email transition on
/// one account serialize against each other instead of racing.
/// </summary>
public interface IPhoneChallengeRepository
{
    Task<IAsyncDisposable> BeginAsync(CancellationToken ct);

    /// <summary>
    /// Locks the challenge row and returns it. Two confirmations of one delivered code both land
    /// here, and the lock is what makes the second one see the first one's consumption.
    /// </summary>
    Task<PhoneChallenge?> FindForUpdateAsync(Guid id, CancellationToken ct);

    /// <summary>Closes every open challenge for this destination and purpose, so a resend invalidates its predecessor.</summary>
    Task SupersedeAsync(string destination, string purpose, Guid? userId, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Whether another code may be sent to this destination now: inside the hourly ceiling and past
    /// the resend spacing. Durable, so it holds across replicas and restarts rather than only
    /// inside one process.
    /// </summary>
    Task<bool> HasSendBudgetAsync(
        string destination,
        DateTimeOffset now,
        int sendsPerHour,
        TimeSpan resendSpacing,
        CancellationToken ct);

    /// <summary>Locks and reads whoever holds this number, or null when nobody does.</summary>
    Task<PhoneOccupant?> LockOccupantAsync(string destination, CancellationToken ct);

    /// <summary>
    /// Clears an unverified contact number from an account, as its own statement, so the release
    /// and the new account's insert cannot reach the unique index in the wrong order. Refuses to
    /// touch a number the account proved.
    /// </summary>
    Task DetachUnverifiedPhoneAsync(Guid userId, CancellationToken ct);

    /// <summary>Locks the account and confirms it still holds this number as a proved factor at this version.</summary>
    Task<bool> LockVerifiedPhoneAsync(Guid userId, long version, string destination, CancellationToken ct);

    /// <summary>Whether the account has accumulated business history, which makes recovery a reviewed process.</summary>
    Task<bool> HasBusinessHistoryAsync(Guid userId, CancellationToken ct);

    /// <summary>Ends every session and refresh family on the account, in the users -> auth_sessions -> refresh_tokens order.</summary>
    Task InvalidateCredentialsAsync(Guid userId, CancellationToken ct);

    void Add(PhoneChallenge challenge);

    void AddSms(IdentitySms message);

    Task<bool> CommitAsync(CancellationToken ct);
}
