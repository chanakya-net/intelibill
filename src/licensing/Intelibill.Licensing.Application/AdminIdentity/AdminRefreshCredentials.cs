using ErrorOr;
using Intelibill.Licensing.Domain.Entities;

namespace Intelibill.Licensing.Application.AdminIdentities;

/// <summary>
/// A live session plus the one refresh credential the browser now holds for it. The raw credential
/// exists only here and in the response cookie; storage keeps its digest.
/// </summary>
public sealed record AdminSessionGrant(AdminSession Session, string RefreshCredential, DateTimeOffset AccessIssuedAt);

public interface IAdminRefreshCredentials
{
    /// <summary>
    /// Consumes the presented credential and issues its single successor in the same session.
    /// A consumed credential presented again ends the whole session.
    /// </summary>
    Task<ErrorOr<AdminSessionGrant>> RotateAsync(string? presented, CancellationToken cancellationToken);

    /// <summary>Ends the presented family and reports whether the credential identified one.</summary>
    Task<bool> EndAsync(string? presented, CancellationToken cancellationToken);
}

public static class AdminRefreshErrors
{
    public static Error SessionEnded => Error.Unauthorized("Admin.SessionEnded", "The provider session has ended.");
}
