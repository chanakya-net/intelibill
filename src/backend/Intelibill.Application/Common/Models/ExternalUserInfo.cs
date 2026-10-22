namespace Intelibill.Application.Common.Models;

public sealed record ExternalUserInfo(
    string ProviderKey,
    string? Email,
    string FirstName,
    string LastName,
    ExternalEmailTrust EmailTrust = ExternalEmailTrust.Unknown);

public enum ExternalEmailTrust { Unknown, Unverified, Verified }
