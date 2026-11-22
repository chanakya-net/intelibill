using ErrorOr;
using Intelibill.Application.Common.Security;
using Intelibill.Domain.Entities;
using Intelibill.Domain.ValueObjects;
using PermitErrors = Intelibill.Application.Common.Errors.Errors.OfflinePermit;

namespace Intelibill.Application.Common.Licensing.Offline;

public sealed partial class OfflineLicencePermitIssuer
{
    private async Task<ErrorOr<OfflinePermitEvidence>> IssueWithinAsync(SessionClaims session,
        OfflineSnapshotCompletion completion, Guid? tillSessionId, CancellationToken ct)
    {
        var admitted = await AdmitAsync(session, completion.ShopId, ct);
        if (admitted.IsError)
            return admitted.Errors;

        var (licence, head, auth) = admitted.Value;
        var tillGate = await gates.EnterAsync(completion.ShopId, session.UserId, tillSessionId, Now(), ct);
        if (tillGate.IsError)
            return tillGate.Errors;

        var loaded = await LoadEvidenceAsync(completion, head, auth, ct);
        if (loaded.IsError)
            return loaded.Errors;

        var (grant, lease, snapshot) = loaded.Value;
        var existing = await store.FindPermitAsync(completion.ShopId, completion.SnapshotId, ct);
        var now = Now();
        var caps = new OfflinePermitCaps(grant.AuthorityVerifiedAt, grant.AuthorityValidUntil, head.ValidUntil,
            grant.DeviceVerifiedAt, Earlier(grant.AuthDeadline, auth.AuthDeadline), lease.ExpiresAt,
            new FiscalYear(lease.FiscalYearStart).NextStartsAt, completion.CompletedAt, snapshot.EarliestExpiry,
            tillGate.Value.TillDayEndsAt);
        if (existing is not null)
            return StillOperating(licence, now) is { } expired
                ? expired
                : Redeliver(existing, completion, tillGate.Value.TillSessionId, caps, now);

        var window = OfflineLicencePermitPolicy.Evaluate(caps, now);
        if (window.IsError)
            return window.Errors;

        var stored = await SignAndStoreAsync(completion, tillGate.Value.TillSessionId, grant, head, caps, window.Value, ct);
        if (stored.IsError)
            return stored.Errors;

        // Waits above (actor row, till, signing, a competing insert) may have outlasted the head or
        // the half-open window, or the clock may have stepped backward below NotBefore.
        var finalNow = Now();
        if (StillOperating(licence, finalNow) is { } stale)
            return stale;
        return AdmitWindow(stored.Value, finalNow);
    }

    /// <summary>The original grant, its lease and the snapshot's retained rows, each checked against the capture.</summary>
    private async Task<ErrorOr<(OfflineLicenseGrant Grant, InvoiceLease Lease, OfflineSnapshotFacts Snapshot)>> LoadEvidenceAsync(
        OfflineSnapshotCompletion completion, VerifiedHead head, OfflineAuthFacts auth, CancellationToken ct)
    {
        if (!captures.TryVerifyCompleted(completion))
            return PermitErrors.SnapshotIncomplete;

        if (await store.FindGrantAsync(completion.ShopId, completion.GrantId, ct) is not { } grant)
            return PermitErrors.GrantNotFound;
        if (grant.UserId != completion.UserId || grant.SessionId != completion.SessionId ||
            grant.LeaseId != completion.LeaseId || !string.Equals(grant.DeviceId, completion.DeviceId, StringComparison.Ordinal))
            return PermitErrors.ScopeMismatch;
        if (await ProveGrantAsync(grant, ct) is { } forged)
            return forged;

        // An existing lease is only usable through the grant written when it was reserved.
        if (await store.FindLeaseAsync(completion.ShopId, grant.LeaseId, ct) is not { } lease ||
            lease.ReservedByUserId != grant.UserId || lease.ReservedBySessionId != grant.SessionId ||
            !string.Equals(lease.DeviceId, grant.DeviceId, StringComparison.Ordinal) || lease.ReservedAt > grant.CapturedAt)
            return PermitErrors.ScopeMismatch;

        // Equal versions also catch a role, status or shop scope that changed away and back.
        if (auth.MembershipAuthorityVersion != grant.MembershipAuthorityVersion ||
            auth.SessionScopeVersion != grant.SessionScopeVersion || head.AccountId != grant.AccountId ||
            head.AffiliationRequestId != grant.AffiliationRequestId || head.AccountRevision < grant.AccountRevision ||
            head.ShopRevision < grant.ShopRevision)
            return PermitErrors.AuthorityChanged;

        var snapshot = await store.ReadSnapshotAsync(completion.ShopId, completion.SnapshotId, ct);
        return IsComplete(snapshot, completion, grant, Now()) ? (grant, lease, snapshot) : PermitErrors.SnapshotIncomplete;
    }

    /// <summary>The row is an index; the signed bytes are the grant. They must agree and still verify.</summary>
    private async Task<Error?> ProveGrantAsync(OfflineLicenseGrant grant, CancellationToken ct)
    {
        if (await store.FindKeyAsync(grant.ShopId, grant.KeyId, ct) is not { } publicKey)
            return PermitErrors.UnknownKey;

        var payload = OfflinePermitSignatures.Verify(grant.SignedGrant, OfflinePermitContractV1.GrantType, publicKey);
        if (payload.IsError)
            return payload.FirstError;
        return payload.Value.AsSpan().SequenceEqual(OfflinePermitContractV1.Encode(ToSnapshot(grant)))
            ? null : PermitErrors.InvalidSignature;
    }

    /// <summary>Every emitted lot is retained, under one catalogue, inside this completed capture's chronology.</summary>
    private static bool IsComplete(OfflineSnapshotFacts snapshot, OfflineSnapshotCompletion completion, OfflineLicenseGrant grant,
        DateTimeOffset now)
    {
        if (completion.BatchCount < 0 || snapshot.BatchCount != completion.BatchCount ||
            completion.StartedAt < grant.CapturedAt || completion.CompletedAt < completion.StartedAt ||
            completion.CompletedAt > now)
            return false;
        if (snapshot.BatchCount == 0)
            return true;
        return snapshot is { CatalogVersions: 1, FirstCapturedAt: { } first, LastCapturedAt: { } last, EarliestExpiry: not null } &&
            snapshot.CatalogVersion == completion.CatalogVersion && first >= completion.StartedAt &&
            first >= grant.CapturedAt && last <= completion.CompletedAt;
    }

    /// <summary>
    /// The same completion again. Authority was checked on the way here; the stored bytes go out only
    /// when every immutable completion fact matches and the instant lies inside [NotBefore, ValidUntil).
    /// </summary>
    private static ErrorOr<OfflinePermitEvidence> Redeliver(OfflineLicensePermit existing, OfflineSnapshotCompletion completion,
        Guid? verifiedTillSessionId, OfflinePermitCaps caps, DateTimeOffset now)
    {
        if (existing.GrantId != completion.GrantId || existing.LeaseId != completion.LeaseId ||
            existing.UserId != completion.UserId || existing.SessionId != completion.SessionId ||
            !string.Equals(existing.DeviceId, completion.DeviceId, StringComparison.Ordinal) ||
            existing.BatchCount != completion.BatchCount || existing.CatalogVersion != completion.CatalogVersion ||
            existing.CompletedAt != completion.CompletedAt || existing.TillSessionId != verifiedTillSessionId)
            return PermitErrors.SnapshotIncomplete;

        var evidence = ToEvidence(existing);
        if (AdmitWindow(evidence, now) is { IsError: true } outside)
            return outside.Errors;

        return OfflineLicencePermitPolicy.Deadlines(caps).Min(cap => cap.Deadline) < existing.ValidUntil
            ? PermitErrors.Tightened
            : evidence;
    }

    private async Task<ErrorOr<OfflinePermitEvidence>> SignAndStoreAsync(OfflineSnapshotCompletion completion,
        Guid? verifiedTillSessionId, OfflineLicenseGrant grant, VerifiedHead head, OfflinePermitCaps caps,
        OfflinePermitWindow window, CancellationToken ct)
    {
        var permit = new OfflineLicensePermitV1(OfflinePermitContractV1.Version, Guid.NewGuid(), grant.Id,
            OfflinePermitContractV1.Digest(grant.SignedGrant), grant.ShopId, grant.AccountId, grant.UserId, grant.SessionId,
            grant.DeviceId, grant.LeaseId, completion.SnapshotId, grant.AccountRevision, grant.ShopRevision,
            head.AccountRevision, head.ShopRevision, grant.AuthorityVerifiedAt, grant.DeviceVerifiedAt,
            caps.SnapshotCompletedAt, window.NotBefore, window.NotBefore, window.ValidUntil, window.LimitingReason,
            verifiedTillSessionId);
        var signed = await SignAsync(grant.ShopId, OfflinePermitContractV1.PermitType, OfflinePermitContractV1.Encode(permit), ct);
        if (signed.IsError)
            return signed.Errors;

        var row = new OfflineLicensePermit
        {
            Id = permit.PermitId, ShopId = permit.ShopId, GrantId = permit.GrantId, LeaseId = permit.LeaseId,
            SnapshotId = permit.SnapshotId, UserId = permit.UserId, SessionId = permit.SessionId, DeviceId = permit.DeviceId,
            TillSessionId = permit.TillSessionId, BatchCount = completion.BatchCount, CatalogVersion = completion.CatalogVersion,
            CurrentAccountRevision = permit.CurrentAccountRevision, CurrentShopRevision = permit.CurrentShopRevision,
            CompletedAt = permit.SnapshotCompletedAt, NotBefore = permit.NotBefore, ValidUntil = permit.ValidUntil,
            LimitingReason = permit.LimitingReason, KeyId = signed.Value.KeyId, SignedPermit = signed.Value.Token,
        };
        if (await store.TryInsertPermitAsync(row, ct))
            return ToEvidence(row);

        // Another request stored this completion first; its bytes are the permit.
        return await store.FindPermitAsync(row.ShopId, row.SnapshotId, ct) is { } winner
            ? Redeliver(winner, completion, verifiedTillSessionId, caps, Now())
            : PermitErrors.Busy;
    }

    private static ErrorOr<OfflinePermitEvidence> AdmitWindow(OfflinePermitEvidence permit, DateTimeOffset now)
    {
        if (OfflineLicencePermitPolicy.Covers(permit.NotBefore, permit.ValidUntil, now))
            return permit;
        return now < permit.NotBefore
            ? PermitErrors.OutsideValidity
            : PermitErrors.DeadlineReached(permit.LimitingReason);
    }

    private static OfflinePermitEvidence ToEvidence(OfflineLicensePermit permit) =>
        new(permit.Id, permit.KeyId, permit.SignedPermit, permit.NotBefore, permit.ValidUntil, permit.LimitingReason);

    private static DateTimeOffset Earlier(DateTimeOffset left, DateTimeOffset right) => left < right ? left : right;

    private static GrantSnapshotV1 ToSnapshot(OfflineLicenseGrant grant) => new(OfflinePermitContractV1.Version, grant.Id,
        grant.ShopId, grant.AccountId, grant.AffiliationRequestId, grant.AccountRevision, grant.ShopRevision,
        grant.AccountEvidenceId, grant.ShopEvidenceId, grant.AuthorityVerifiedAt, grant.AuthorityValidUntil, grant.UserId,
        grant.SessionId, grant.DeviceId, grant.LeaseId, grant.MembershipAuthorityVersion, grant.SessionScopeVersion,
        grant.DeviceVerifiedAt, grant.AuthDeadline, grant.CapturedAt);
}
