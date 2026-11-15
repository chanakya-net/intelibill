using System.Text;
using System.Text.Json;
using ErrorOr;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Billing;
using Intelibill.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Intelibill.Infrastructure.Billing;

internal sealed class MerchantBillingDelegationIssuer(
    IMerchantBillingSigningKeys keys, ILiveSessionAuthority authority, ApplicationDbContext db, TimeProvider clock)
{
    internal async Task<ErrorOr<IssuedBillingDelegationToken>> IssueAsync(
        MerchantBillingAccessRequest access, Guid requestId, CancellationToken cancellationToken)
    {
        using var key = await keys.ResolveAsync(cancellationToken);
        if (key is null)
            return MerchantBillingErrors.Unavailable;

        var session = access.Session;
        if (!await authority.IsValidAsync(session, cancellationToken))
            return MerchantBillingErrors.SessionInvalid;

        var sessionExpiry = await (from current in db.AuthSessions.AsNoTracking()
            join actor in db.Users.AsNoTracking() on current.UserId equals actor.Id
            where current.Id == session.SessionId && actor.Id == session.UserId
                && current.RevokedAt == null && current.ActiveShopId == session.ActiveShopId
                && current.SecurityVersion == session.SecurityVersion && actor.SecurityVersion == session.SecurityVersion
                && actor.IsLoginEnabled && ((actor.Email != null && actor.IsEmailVerified) ||
                    (actor.PhoneNumber != null && actor.IsPhoneVerified))
            select (DateTimeOffset?)current.ExpiresAt).SingleOrDefaultAsync(cancellationToken);

        if (sessionExpiry is null)
            return MerchantBillingErrors.SessionInvalid;

        var issued = clock.GetUtcNow().ToUnixTimeSeconds();
        var expiry = Math.Min(Math.Min(sessionExpiry.Value.ToUnixTimeSeconds(), access.AccessExpiry),
            issued + MerchantBillingDelegationV1.LifetimeSeconds);
        if (expiry <= issued)
            return MerchantBillingErrors.SessionInvalid;

        var signingInput = EncodeSigningInput(key.Id, access, requestId, issued, expiry);
        var signature = await key.SignAsync(Encoding.ASCII.GetBytes(signingInput), cancellationToken);
        return clock.GetUtcNow().ToUnixTimeSeconds() < expiry
            ? new IssuedBillingDelegationToken(signingInput + "." + Base64UrlEncoder.Encode(signature))
            : MerchantBillingErrors.SessionInvalid;
    }

    private static string EncodeSigningInput(string keyId, MerchantBillingAccessRequest access, Guid requestId,
        long issued, long expiry)
    {
        var header = new { alg = "RS256", typ = MerchantBillingDelegationV1.TokenType, kid = keyId };
        var payload = new MerchantBillingAssertionV1(
            access.Session.UserId, access.AccountId, requestId, Guid.NewGuid(), issued, expiry);
        return Base64UrlEncoder.Encode(JsonSerializer.SerializeToUtf8Bytes(header)) + "." +
            Base64UrlEncoder.Encode(JsonSerializer.SerializeToUtf8Bytes(payload));
    }
}

internal sealed record IssuedBillingDelegationToken(string DelegationToken)
{
    public override string ToString() => "[REDACTED]";
}
