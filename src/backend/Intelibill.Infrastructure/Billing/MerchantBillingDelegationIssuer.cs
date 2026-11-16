using System.Security.Cryptography;
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

/// <summary>
/// A request the merchant server builds itself. Callers never choose the action, method or path freely:
/// each factory binds one licensing route, and the body digest covers the exact bytes that will be sent.
/// </summary>
internal sealed record BillingDelegationIntent(string Action, HttpMethod Method, string Path, Guid? AccountId, byte[] Body)
{
    internal static BillingDelegationIntent ReadAccount(Guid accountId) => new(MerchantBillingDelegationV1.ReadAccount,
        HttpMethod.Get, MerchantBillingDelegationV1.AccountAccessPath(accountId), accountId, []);

    internal static BillingDelegationIntent ListAccounts(Guid? after) => new(MerchantBillingDelegationV1.ListAccounts,
        HttpMethod.Get, MerchantBillingDelegationV1.AccountListPath(after), null, []);

    internal static BillingDelegationIntent CreateAccount(byte[] body) => new(MerchantBillingDelegationV1.CreateAccount,
        HttpMethod.Post, MerchantBillingDelegationV1.AccountsPath, null, body);

    internal string BodySha256 => Convert.ToHexStringLower(SHA256.HashData(Body));
}

internal sealed class MerchantBillingDelegationIssuer(
    IMerchantBillingSigningKeys keys, ILiveSessionAuthority authority, ApplicationDbContext db, TimeProvider clock)
{
    internal async Task<ErrorOr<IssuedBillingDelegationToken>> IssueAsync(
        MerchantBillingCaller caller, BillingDelegationIntent intent, Guid requestId, CancellationToken cancellationToken)
    {
        using var key = await keys.ResolveAsync(cancellationToken);
        if (key is null)
            return MerchantBillingErrors.Unavailable;

        var session = caller.Session;
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
        var expiry = Math.Min(Math.Min(sessionExpiry.Value.ToUnixTimeSeconds(), caller.AccessExpiry),
            issued + MerchantBillingDelegationV1.LifetimeSeconds);
        if (expiry <= issued)
            return MerchantBillingErrors.SessionInvalid;

        var signingInput = EncodeSigningInput(key.Id, session.UserId, intent, requestId, issued, expiry);
        var signature = await key.SignAsync(Encoding.ASCII.GetBytes(signingInput), cancellationToken);
        return clock.GetUtcNow().ToUnixTimeSeconds() < expiry
            ? new IssuedBillingDelegationToken(signingInput + "." + Base64UrlEncoder.Encode(signature))
            : MerchantBillingErrors.SessionInvalid;
    }

    private static string EncodeSigningInput(string keyId, Guid actorId, BillingDelegationIntent intent, Guid requestId,
        long issued, long expiry)
    {
        var header = new { alg = "RS256", typ = MerchantBillingDelegationV1.TokenType, kid = keyId };
        // The account read keeps its original field set byte for byte; actor-scoped actions omit the account.
        byte[] payload = intent.AccountId is { } account
            ? JsonSerializer.SerializeToUtf8Bytes(new MerchantBillingAssertionV1(
                actorId, account, requestId, Guid.NewGuid(), issued, expiry)
            {
                Action = intent.Action, Method = intent.Method.Method, Path = intent.Path, BodySha256 = intent.BodySha256,
            })
            : JsonSerializer.SerializeToUtf8Bytes(new MerchantBillingActorAssertionV1(actorId, intent.Action,
                intent.Method.Method, intent.Path, intent.BodySha256, requestId, Guid.NewGuid(), issued, expiry));
        return Base64UrlEncoder.Encode(JsonSerializer.SerializeToUtf8Bytes(header)) + "." +
            Base64UrlEncoder.Encode(payload);
    }
}

internal sealed record IssuedBillingDelegationToken(string DelegationToken)
{
    public override string ToString() => "[REDACTED]";
}
