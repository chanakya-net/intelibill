using System.Security.Cryptography;
using System.Text;
using Intelibill.Shared.Security;
using Microsoft.AspNetCore.DataProtection;

namespace Intelibill.Licensing.Infrastructure.Billing;

internal sealed class BillingInvitationSecrets(IDataProtectionProvider protection, OpaqueTokenPrimitives tokens)
{
    private const string Purpose = "InteliBill.Billing.ManagerInvitation/v1";

    internal (byte[] Hash, string Sealed) Create(Guid account, Guid invitation, Guid recipient)
    {
        var token = tokens.NewToken();
        return (Hash(account, invitation, recipient, token), Protector(account, invitation, recipient).Protect(token));
    }

    internal string? Read(Guid account, Guid invitation, Guid recipient, string sealedProof)
    {
        try
        {
            return Protector(account, invitation, recipient).Unprotect(sealedProof);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    internal static byte[] Hash(Guid account, Guid invitation, Guid recipient, string token) =>
        SHA256.HashData(Encoding.UTF8.GetBytes($"{Purpose}:{account:D}:{invitation:D}:{recipient:D}:{token}"));

    private IDataProtector Protector(Guid account, Guid invitation, Guid recipient) =>
        protection.CreateProtector(Purpose, account.ToString("D"), invitation.ToString("D"), recipient.ToString("D"));
}
