using ErrorOr;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Security;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using AuthErrors = Intelibill.Application.Common.Errors.Errors.Auth;
using PermitErrors = Intelibill.Application.Common.Errors.Errors.OfflinePermit;

namespace Intelibill.Application.Common.Licensing.Offline;

/// <summary>
/// Lock order, matching the sale: licence fence (shared account then shop) → actor row (shared) →
/// expiry fence → till gate and session → evidence rows. Projection delivery, session revocation and
/// membership changes take the same locks exclusively, so a restriction either commits first and is
/// seen here, or waits until this permit is stored.
/// </summary>
public sealed partial class OfflineLicencePermitIssuer(
    IShopLicenceFence fence,
    ILiveSessionAuthority authority,
    IMembershipAuthorityRepository actors,
    OfflinePermitGates gates,
    IOfflineLicenceEvidenceStore store,
    IOfflinePermitSigner signer,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IOfflineLicencePermitIssuer
{
    public async Task<ErrorOr<OfflineGrantEvidence>> CaptureGrantAsync(SessionClaims? session, InvoiceLease lease,
        CancellationToken ct)
    {
        if (session is null || session.ActiveShopId != lease.ShopId || session.UserId != lease.ReservedByUserId ||
            session.SessionId != lease.ReservedBySessionId || string.IsNullOrWhiteSpace(lease.DeviceId))
            return PermitErrors.ScopeMismatch;
        if (!store.IsBeingReserved(lease))
            return PermitErrors.LeaseNotNew;

        var admitted = await AdmitAsync(session, lease.ShopId, ct);
        if (admitted.IsError)
            return admitted.Errors;

        var (licence, head, auth) = admitted.Value;
        var now = Now();
        if (lease.ReservedAt > now || lease.ExpiresAt <= now)
            return PermitErrors.ScopeMismatch;

        var grant = new GrantSnapshotV1(OfflinePermitContractV1.Version, Guid.NewGuid(), lease.ShopId, head.AccountId,
            head.AffiliationRequestId, head.AccountRevision, head.ShopRevision, head.AccountEvidenceId, head.ShopEvidenceId,
            head.VerifiedAt, head.ValidUntil, session.UserId, session.SessionId, lease.DeviceId, lease.Id,
            auth.MembershipAuthorityVersion, auth.SessionScopeVersion, now, auth.AuthDeadline, now);
        var signed = await SignAsync(lease.ShopId, OfflinePermitContractV1.GrantType, OfflinePermitContractV1.Encode(grant), ct);
        if (signed.IsError)
            return signed.Errors;

        // Signing can wait on a remote key; the fenced head may have run out meanwhile.
        if (StillOperating(licence) is { } stale)
            return stale;

        store.StageGrant(ToRow(grant, signed.Value.KeyId, signed.Value.Token));
        return new OfflineGrantEvidence(grant.GrantId, signed.Value.KeyId, signed.Value.Token);
    }

    public async Task<ErrorOr<OfflinePermitEvidence>> IssueAsync(OfflinePermitRequest request, CancellationToken ct)
    {
        var completion = request.Completion;
        if (request.Session is not { } session || session.ActiveShopId != completion.ShopId ||
            session.UserId != completion.UserId || session.SessionId != completion.SessionId)
            return PermitErrors.ScopeMismatch;

        await unitOfWork.BeginTransactionAsync(ct);
        var committed = false;
        try
        {
            var issued = await IssueWithinAsync(session, request, ct);
            if (issued.IsError)
                return issued;

            await unitOfWork.CommitTransactionAsync(ct);
            committed = true;
            return issued;
        }
        finally
        {
            if (!committed)
                await unitOfWork.RollbackTransactionAsync(CancellationToken.None);
        }
    }

    public async Task<ErrorOr<OfflineLicensePermitV1>> VerifyAsync(string? signedPermit, OfflinePermitScope expected,
        DateTimeOffset at, CancellationToken ct)
    {
        // The key is looked up for the shop the caller expects; another shop's key is simply unknown here.
        var keyId = OfflinePermitSignatures.ReadKeyId(signedPermit, OfflinePermitContractV1.PermitType);
        var publicKey = keyId is null ? null : await store.FindKeyAsync(expected.ShopId, keyId, ct);
        return OfflinePermitSignatures.VerifyPermit(signedPermit, expected, at, _ => publicKey);
    }

    /// <summary>Fence, then the actor row, then live authority: the facts every later step relies on.</summary>
    private async Task<ErrorOr<(ShopLicenceLease Licence, VerifiedHead Head, OfflineAuthFacts Auth)>> AdmitAsync(
        SessionClaims session, Guid shopId, CancellationToken ct)
    {
        var entered = await fence.EnterAsync(shopId, LicenceAction.NewTrading, ct);
        if (entered.IsError)
            return entered.Errors;

        var licence = entered.Value;
        if (VerifiedHead.From(licence) is not { } head)
            return licence.Decision.Denial ?? PermitErrors.AuthorityUnavailable;

        // Revocation, rescope and membership changes hold this row for update; wait for them, then read.
        var locked = await actors.TryLockUserAsync(session.UserId, forShare: true, ct);
        if (locked != RowLockOutcome.Acquired)
            return locked == RowLockOutcome.TimedOut ? PermitErrors.Busy : AuthErrors.SessionInvalidated;
        if (!await authority.IsValidAsync(session, ct) ||
            await store.ReadAuthAsync(shopId, session.UserId, session.SessionId, Now(), ct) is not { } auth)
            return AuthErrors.SessionInvalidated;

        return (licence, head, auth);
    }

    private async Task<ErrorOr<OfflinePermitSignature>> SignAsync(Guid shopId, string type, byte[] payload, CancellationToken ct)
    {
        if (await signer.SignAsync(type, payload, ct) is not { } signature)
            return PermitErrors.SigningUnavailable;

        var retained = await store.RetainKeyAsync(shopId, signature.KeyId, signature.PublicKey, Now(), ct);
        return retained.IsError ? retained.Errors : signature;
    }

    /// <summary>The fenced head cannot change before commit, but the clock can pass its deadline.</summary>
    private Error? StillOperating(ShopLicenceLease licence)
    {
        var decision = licence.CurrentDecision();
        return decision is { Outcome: LicenceOutcome.Allowed, ValidUntil: { } until } && Now() < until
            ? null
            : decision.Denial ?? PermitErrors.AuthorityUnavailable;
    }

    private DateTimeOffset Now() => OfflinePermitContractV1.Instant(clock.GetUtcNow());

    private static OfflineLicenseGrant ToRow(GrantSnapshotV1 grant, string keyId, string token) => new()
    {
        Id = grant.GrantId, ShopId = grant.ShopId, LeaseId = grant.LeaseId, AccountId = grant.AccountId,
        AffiliationRequestId = grant.AffiliationRequestId, AccountRevision = grant.AccountRevision,
        ShopRevision = grant.ShopRevision, AccountEvidenceId = grant.AccountEvidenceId, ShopEvidenceId = grant.ShopEvidenceId,
        AuthorityVerifiedAt = grant.AuthorityVerifiedAt, AuthorityValidUntil = grant.AuthorityValidUntil,
        UserId = grant.UserId, SessionId = grant.SessionId, DeviceId = grant.DeviceId,
        MembershipAuthorityVersion = grant.MembershipAuthorityVersion, SessionScopeVersion = grant.SessionScopeVersion,
        DeviceVerifiedAt = grant.DeviceVerifiedAt, AuthDeadline = grant.AuthDeadline, CapturedAt = grant.CapturedAt,
        KeyId = keyId, SignedGrant = token,
    };

    /// <summary>An operating decision with both proven heads; an observe-only shop qualifies, an unproven one never does.</summary>
    private sealed record VerifiedHead(Guid AccountId, Guid AffiliationRequestId, long AccountRevision, long ShopRevision,
        Guid AccountEvidenceId, Guid ShopEvidenceId, DateTimeOffset VerifiedAt, DateTimeOffset ValidUntil)
    {
        internal static VerifiedHead? From(ShopLicenceLease licence) =>
            licence is
            {
                Decision: { Outcome: LicenceOutcome.Allowed, AuthorityVerifiedAt: { } verified, ValidUntil: { } until },
                AccountId: { } account, AffiliationRequestId: { } affiliation, AccountRevision: { } accountRevision,
                ShopRevision: { } shopRevision, AccountEvidenceId: { } accountEvidence, ShopEvidenceId: { } shopEvidence,
            }
                ? new(account, affiliation, accountRevision, shopRevision, accountEvidence, shopEvidence,
                    OfflinePermitContractV1.Instant(verified), OfflinePermitContractV1.Instant(until))
                : null;
    }
}
