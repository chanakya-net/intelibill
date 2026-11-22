using ErrorOr;
using Intelibill.Application.Common.Security;
using Intelibill.Domain.Entities;
using Intelibill.Domain.ValueObjects;
using PermitErrors = Intelibill.Application.Common.Errors.Errors.OfflinePermit;

namespace Intelibill.Application.Common.Licensing.Offline;

public sealed partial class OfflineLicencePermitIssuer
{
    private async Task<ErrorOr<OfflinePermitEvidence>> IssueWithinAsync(SessionClaims session, OfflinePermitRequest request,
        CancellationToken ct)
    {
        var completion = request.Completion;
        var admitted = await AdmitAsync(session, completion.ShopId, ct);
        if (admitted.IsError)
            return admitted.Errors;

        var (licence, head, auth) = admitted.Value;
        var tillDayEnd = await gates.EnterAsync(completion.ShopId, session.UserId, request.TillSessionId, Now(), ct);
        if (tillDayEnd.IsError)
            return tillDayEnd.Errors;

        var loaded = await LoadEvidenceAsync(request, head, auth, ct);
        if (loaded.IsError)
            return loaded.Errors;

        var (grant, lease, snapshot) = loaded.Value;
        var existing = await store.FindPermitAsync(completion.ShopId, completion.SnapshotId, ct);
        var now = Now();
        var caps = new OfflinePermitCaps(grant.AuthorityVerifiedAt, grant.AuthorityValidUntil, head.ValidUntil,
            grant.DeviceVerifiedAt, Earlier(grant.AuthDeadline, auth.AuthDeadline), lease.ExpiresAt,
            new FiscalYear(lease.FiscalYearStart).NextStartsAt, existing?.CompletedAt ?? now, snapshot.EarliestExpiry,
            tillDayEnd.Value);
        if (existing is not null)
            return Redeliver(existing, request, caps, now);

        var window = OfflineLicencePermitPolicy.Evaluate(caps, now);
        if (window.IsError)
            return window.Errors;

        var stored = await SignAndStoreAsync(request, grant, head, caps, window.Value, ct);
        if (stored.IsError)
            return stored.Errors;

        // Waits above (actor row, till, signing, a competing insert) may have outlasted the head or the window.
        if (StillOperating(licence) is { } stale)
            return stale;
        return Now() < stored.Value.ValidUntil ? stored : PermitErrors.DeadlineReached(stored.Value.LimitingReason);
    }

    /// <summary>The original grant, its lease and the snapshot's retained rows, each checked against the caller.</summary>
    private async Task<ErrorOr<(OfflineLicenseGrant Grant, InvoiceLease Lease, OfflineSnapshotFacts Snapshot)>> LoadEvidenceAsync(
        OfflinePermitRequest request, VerifiedHead head, OfflineAuthFacts auth, CancellationToken ct)
    {
        var completion = request.Completion;
        if (await store.FindGrantAsync(completion.ShopId, request.GrantId, ct) is not { } grant)
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

    /// <summary>Every emitted lot is retained, under one catalogue, captured after the reservation and before now.</summary>
    private static bool IsComplete(OfflineSnapshotFacts snapshot, OfflineSnapshotCompletion completion, OfflineLicenseGrant grant,
        DateTimeOffset now)
    {
        if (completion.BatchCount < 0 || snapshot.BatchCount != completion.BatchCount)
            return false;
        if (snapshot.BatchCount == 0)
            return true;
        return snapshot is { CatalogVersions: 1, FirstCapturedAt: { } first, LastCapturedAt: { } last, EarliestExpiry: not null } &&
            snapshot.CatalogVersion == completion.CatalogVersion && first >= grant.CapturedAt && last <= now;
    }

    /// <summary>
    /// The same completion again. Authority was checked on the way here; the stored bytes go out only
    /// while they are still inside every limit known now. History stays either way.
    /// </summary>
    private static ErrorOr<OfflinePermitEvidence> Redeliver(OfflineLicensePermit existing, OfflinePermitRequest request,
        OfflinePermitCaps caps, DateTimeOffset now)
    {
        if (existing.GrantId != request.GrantId || existing.TillSessionId != request.TillSessionId)
            return PermitErrors.SnapshotIncomplete;
        if (now >= existing.ValidUntil)
            return PermitErrors.DeadlineReached(existing.LimitingReason);
        return OfflineLicencePermitPolicy.Deadlines(caps).Min(cap => cap.Deadline) < existing.ValidUntil
            ? PermitErrors.Tightened
            : ToEvidence(existing);
    }

    private async Task<ErrorOr<OfflinePermitEvidence>> SignAndStoreAsync(OfflinePermitRequest request, OfflineLicenseGrant grant,
        VerifiedHead head, OfflinePermitCaps caps, OfflinePermitWindow window, CancellationToken ct)
    {
        var completion = request.Completion;
        var permit = new OfflineLicensePermitV1(OfflinePermitContractV1.Version, Guid.NewGuid(), grant.Id,
            OfflinePermitContractV1.Digest(grant.SignedGrant), grant.ShopId, grant.AccountId, grant.UserId, grant.SessionId,
            grant.DeviceId, grant.LeaseId, completion.SnapshotId, grant.AccountRevision, grant.ShopRevision,
            head.AccountRevision, head.ShopRevision, grant.AuthorityVerifiedAt, grant.DeviceVerifiedAt,
            caps.SnapshotCompletedAt, window.NotBefore, window.NotBefore, window.ValidUntil, window.LimitingReason,
            request.TillSessionId);
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
            ? Redeliver(winner, request, caps, Now())
            : PermitErrors.Busy;
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
