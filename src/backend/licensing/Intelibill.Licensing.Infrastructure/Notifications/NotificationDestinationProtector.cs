using System.Security.Cryptography;
using System.Text;
using Intelibill.Shared.Security;
using Microsoft.AspNetCore.DataProtection;

namespace Intelibill.Licensing.Infrastructure.Notifications;

internal sealed class NotificationDestinationProtector(
    IDataProtectionProvider protection,
    OpaqueTokenPrimitives tokens)
{
    private const string JobPurpose = "InteliBill.Licensing.NotificationDestination/v1";
    private const string ContactPurpose = "InteliBill.Licensing.BillingContactDestination/v1";
    private const string ProofPurpose = "InteliBill.Licensing.BillingContactProof/v1";

    internal string ProtectJobDestination(
        Guid accountId,
        string scopeKind,
        Guid scopeId,
        string recipientKey,
        string normalizedEmail) =>
        JobProtector(accountId, scopeKind, scopeId, recipientKey).Protect(normalizedEmail);

    internal string? UnprotectJobDestination(
        Guid accountId,
        string scopeKind,
        Guid scopeId,
        string recipientKey,
        string protectedDestination)
    {
        try
        {
            return JobProtector(accountId, scopeKind, scopeId, recipientKey).Unprotect(protectedDestination);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    internal string ProtectContactDestination(Guid accountId, Guid contactId, string normalizedEmail) =>
        ContactProtector(accountId, contactId).Protect(normalizedEmail);

    internal string? UnprotectContactDestination(Guid accountId, Guid contactId, string protectedDestination)
    {
        try
        {
            return ContactProtector(accountId, contactId).Unprotect(protectedDestination);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    internal (string Token, byte[] Hash) CreateStandaloneProof(
        Guid accountId,
        Guid contactId,
        byte[] destinationHash)
    {
        var token = tokens.NewToken();
        return (token, HashProof(accountId, contactId, destinationHash, token));
    }

    internal static byte[] HashEmail(string normalizedEmail) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(normalizedEmail));

    internal static byte[] HashProof(
        Guid accountId,
        Guid contactId,
        byte[] destinationHash,
        string token) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{ProofPurpose}:{accountId:D}:{contactId:D}:{Convert.ToHexStringLower(destinationHash)}:{token}"));

    internal string ProtectProof(Guid account, Guid contact, Guid delivery, string token) =>
        ProofProtector(account, contact, delivery).Protect(token);

    internal string? OpenProof(Guid account, Guid contact, Guid delivery, string ciphertext)
    {
        try
        {
            return ProofProtector(account, contact, delivery).Unprotect(ciphertext);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private IDataProtector ProofProtector(Guid account, Guid contact, Guid delivery) =>
        protection.CreateProtector(ProofPurpose, account.ToString("D"), contact.ToString("D"), delivery.ToString("D"));

    private IDataProtector JobProtector(Guid accountId, string scopeKind, Guid scopeId, string recipientKey) =>
        protection.CreateProtector(JobPurpose, accountId.ToString("D"), scopeKind, scopeId.ToString("D"), recipientKey);

    private IDataProtector ContactProtector(Guid accountId, Guid contactId) =>
        protection.CreateProtector(ContactPurpose, accountId.ToString("D"), contactId.ToString("D"));
}
