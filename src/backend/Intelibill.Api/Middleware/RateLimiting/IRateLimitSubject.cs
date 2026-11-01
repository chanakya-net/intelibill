namespace Intelibill.Api.Middleware.RateLimiting;

/// <summary>
/// Who, besides the caller, a limited endpoint's requests are counted against.
/// </summary>
public enum RateLimitSubject
{
    /// <summary>Only the caller: route, signed-in user and client address.</summary>
    None,

    /// <summary>
    /// The account a credential is being tried against, however many addresses the attempts come
    /// from. Counted whether or not the account exists.
    /// </summary>
    Account,

    /// <summary>
    /// The mailbox or phone a message would be sent to, so no number of callers can flood one
    /// recipient. Counted whether or not anything is actually sent.
    /// </summary>
    Destination,
}

/// <summary>
/// A request model that names the account or destination it acts on. Implemented explicitly, so
/// the member is neither bound from the body nor serialized back.
/// </summary>
public interface IRateLimitSubject
{
    /// <summary>The email address or phone number as the caller typed it; null when absent.</summary>
    string? Subject { get; }
}
