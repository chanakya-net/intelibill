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
    IOfflineSalesSnapshotStreamingService streaming,
    OfflineSnapshotCaptureRegistry captures,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IOfflineLicencePermitIssuer
{
    private const string CaptureSavepoint = "offline_permit_capture";

    public OfflineSnapshotCapture StartSnapshotCapture(SessionClaims? session, Guid grantId, Guid leaseId, string deviceId) =>
        new(session, grantId, leaseId, deviceId, streaming, captures, clock);

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
        if (lease.ReservedAt > now)
            return PermitErrors.ScopeMismatch;
        if (lease.ExpiresAt <= now)
            return PermitErrors.DeadlineReached(OfflineLicencePermitPolicy.LeaseExpiry);

        var grant = new GrantSnapshotV1(OfflinePermitContractV1.Version, Guid.NewGuid(), lease.ShopId, head.AccountId,
            head.AffiliationRequestId, head.AccountRevision, head.ShopRevision, head.AccountEvidenceId, head.ShopEvidenceId,
            head.VerifiedAt, head.ValidUntil, session.UserId, session.SessionId, lease.DeviceId, lease.Id,
            auth.MembershipAuthorityVersion, auth.SessionScopeVersion, now, auth.AuthDeadline, now);
        if (await signer.SignAsync(OfflinePermitContractV1.GrantType, OfflinePermitContractV1.Encode(grant), ct) is not { } signature)
            return PermitErrors.SigningUnavailable;

        // Signing can wait on a remote key; verify auth, licence, lease and clock before writing any key row.
        if (ValidateCaptureStillValid(licence, grant, auth, lease, Now()) is { } postSignFailure)
            return postSignFailure;

        // Isolate first-use key insertion in a savepoint so any late refusal rolls back cleanly inside
        // the caller's reservation transaction without preventing a permitted legacy lease commit.
        var retained = await store.ExecuteInSavepointAsync<Success>(CaptureSavepoint, async () =>
        {
            var saved = await store.RetainKeyAsync(lease.ShopId, signature.KeyId, signature.PublicKey, Now(), ct);
            if (saved.IsError)
                return saved.Errors;
            return ValidateCaptureStillValid(licence, grant, auth, lease, Now()) is { } late
                ? late
                : Result.Success;
        }, ct);
        if (retained.IsError)
            return retained.Errors;

        store.StageGrant(ToRow(grant, signature.KeyId, signature.Token));
        return new OfflineGrantEvidence(grant.GrantId, signature.KeyId, signature.Token);
    }

    public async Task<ErrorOr<OfflinePermitEvidence>> IssueAsync(OfflinePermitRequest request, CancellationToken ct)
    {
        if (request.Completion is not { } completion)
            return PermitErrors.SnapshotIncomplete;
        if (request.Session is not { } session || session.ActiveShopId != completion.ShopId ||
            session.UserId != completion.UserId || session.SessionId != completion.SessionId ||
            completion.GrantId != request.GrantId)
            return PermitErrors.ScopeMismatch;

        await unitOfWork.BeginTransactionAsync(ct);
        var committed = false;
        try
        {
            var issued = await IssueWithinAsync(session, completion, request.TillSessionId, ct);
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

    /// <summary>
    /// Fence, then actor row, then live authority, then commercial licence head: keeps the account/shop
    /// to actor lock order while ensuring revoked or unauthorized actors never see commercial licence state.
    /// </summary>
    private async Task<ErrorOr<(ShopLicenceLease Licence, VerifiedHead Head, OfflineAuthFacts Auth)>> AdmitAsync(
        SessionClaims session, Guid shopId, CancellationToken ct)
    {
        var entered = await fence.EnterAsync(shopId, LicenceAction.NewTrading, ct);
        if (entered.IsError)
            return entered.Errors;

        // Revocation, rescope and membership changes hold this row for update; wait for them, then read.
        var locked = await actors.TryLockUserAsync(session.UserId, forShare: true, ct);
        if (locked != RowLockOutcome.Acquired)
            return locked == RowLockOutcome.TimedOut ? PermitErrors.Busy : AuthErrors.SessionInvalidated;
        if (!await authority.IsValidAsync(session, ct) ||
            await store.ReadAuthAsync(shopId, session.UserId, session.SessionId, Now(), ct) is not { } auth)
            return AuthErrors.SessionInvalidated;

        var licence = entered.Value;
        if (VerifiedHead.From(licence) is not { } head)
            return licence.Decision.Denial ?? PermitErrors.AuthorityUnavailable;

        return (licence, head, auth);
    }

    private static Error? ValidateCaptureStillValid(ShopLicenceLease licence, GrantSnapshotV1 grant, OfflineAuthFacts auth,
        InvoiceLease lease, DateTimeOffset now)
    {
        if (now < grant.CapturedAt)
            return PermitErrors.OutsideValidity;
        if (now >= auth.AuthDeadline)
            return AuthErrors.SessionInvalidated;
        if (StillOperating(licence, now) is { } stale)
            return stale;
        if (now >= lease.ExpiresAt)
            return PermitErrors.DeadlineReached(OfflineLicencePermitPolicy.LeaseExpiry);
        return null;
    }

    private async Task<ErrorOr<OfflinePermitSignature>> SignAsync(Guid shopId, string type, byte[] payload, CancellationToken ct)
    {
        if (await signer.SignAsync(type, payload, ct) is not { } signature)
            return PermitErrors.SigningUnavailable;

        var retained = await store.RetainKeyAsync(shopId, signature.KeyId, signature.PublicKey, Now(), ct);
        return retained.IsError ? retained.Errors : signature;
    }

    /// <summary>The fenced head cannot change before commit, but the clock can pass its deadline.</summary>
    private static Error? StillOperating(ShopLicenceLease licence, DateTimeOffset now)
    {
        var decision = licence.CurrentDecision();
        return decision is { Outcome: LicenceOutcome.Allowed, ValidUntil: { } until } && now < until
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
