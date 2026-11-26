using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Coverage;

public enum CoverageTransferReviewOutcome { Current, Incomplete, Stale, Expired, Unavailable }

/// <summary>One accepted consent exactly as it was bound: purpose, event and the authority tuple that gave it.</summary>
public sealed record CoverageTransferConsentBinding(
    string Purpose, Guid EventId, Guid AccountId, Guid ActorId, Guid MemberId, long AuthorityRevision, int Generation);

/// <summary>Every fact a reviewer may rely on, true at <c>ObservedAt</c> only.</summary>
public sealed record CoverageTransferReviewObservation(
    Guid TransferId, long ProposalRevision, string TermsDigest, Guid SourceAccountId, Guid DestinationAccountId,
    Guid ShopId, Guid SourceAffiliationId, int ObservedPermitCount, DateTimeOffset? ObservedPermitDeadline,
    CoverageTransferConsentBinding[] Consents, DateTimeOffset ObservedAt,
    int MerchantPermitCount = 0, DateTimeOffset? MerchantMaxPermitValidUntil = null, string? MerchantPermitDigest = null,
    CoverageTransferShopAuthorityV1? MerchantProof = null, DateTimeOffset? ProofDeadline = null);

public sealed record CoverageTransferReviewCheck(
    CoverageTransferReviewOutcome Outcome, string? Reason, CoverageTransferReviewObservation? Observation)
{
    public static CoverageTransferReviewCheck Of(CoverageTransferReviewOutcome outcome, string reason) => new(outcome, reason, null);
}

public interface ICoverageTransferReviewStore
{
    /// <summary>
    /// In its own transaction: lock both billing accounts in canonical order, then the proposal, and evaluate both
    /// live Billing Owner tuples, all three exact consent events, revocations, frozen evidence and expiry.
    /// </summary>
    Task<CoverageTransferReviewCheck> CaptureAsync(Guid transferId, long expectedRevision, string termsDigest, CancellationToken ct) =>
        CaptureAsync(transferId, expectedRevision, termsDigest, null, ct);

    Task<CoverageTransferReviewCheck> CaptureAsync(Guid transferId, long expectedRevision, string termsDigest,
        CoverageTransferShopAuthorityV1? merchant, CancellationToken ct);

    /// <summary>
    /// The same locked predicate inside the caller's open transaction, for a final decision write to repeat
    /// immediately before it commits.
    /// </summary>
    Task<CoverageTransferReviewCheck> EvaluateInTransactionAsync(Guid transferId, long expectedRevision, string termsDigest,
        CancellationToken ct) => EvaluateInTransactionAsync(transferId, expectedRevision, termsDigest, null, ct);

    Task<CoverageTransferReviewCheck> EvaluateInTransactionAsync(Guid transferId, long expectedRevision, string termsDigest,
        CoverageTransferShopAuthorityV1? merchant, CancellationToken ct);
}

public interface IMerchantCoverageTransferAuthority
{
    /// <summary>A fresh signed exchange with the merchant service; null when it is unreachable or its answer does not verify.</summary>
    Task<CoverageTransferShopAuthorityV1?> CheckAsync(CoverageTransferShopAuthorityCheckV1 check, CancellationToken ct);
}

/// <summary>
/// The live authority check a provider review must pass before it may act on a transfer. A collected-consents
/// display state or an earlier positive answer never authorizes anything: this rechecks both billing-owner epochs
/// under lock, asks the merchant service about the named shop's owner in a signed exchange, then relocks and
/// requires the exact same bound evidence. Outage, expiry, a mismatched reply or any stale fact fails closed.
/// </summary>
public sealed class CoverageTransferAuthority(
    ICoverageTransferReviewStore store, IMerchantCoverageTransferAuthority merchant, TimeProvider transportClock)
{
    private static readonly TimeSpan ProofLifetime = TimeSpan.FromSeconds(30);

    public async Task<CoverageTransferReviewCheck> CheckForReviewAsync(Guid transferId, long expectedRevision,
        string termsDigest, CancellationToken ct)
    {
        var first = await store.CaptureAsync(transferId, expectedRevision, termsDigest, ct);
        if (first is not { Outcome: CoverageTransferReviewOutcome.Current, Observation: { } captured })
            return first;

        var check = Challenge(captured, EntitlementSnapshotsV1.Instant(transportClock).Add(ProofLifetime));
        var answer = await merchant.CheckAsync(check, ct);
        if (answer is null || transportClock.GetUtcNow() >= check.Deadline)
            return CoverageTransferReviewCheck.Of(CoverageTransferReviewOutcome.Unavailable, "ShopAuthorityUnavailable");
        if (!answer.Current)
            return CoverageTransferReviewCheck.Of(CoverageTransferReviewOutcome.Stale, answer.Reason ?? "ShopOwnerChanged");
        if (answer.OutstandingPermitCount > captured.ObservedPermitCount ||
            (answer.MaxPermitValidUntil is { } end && !(end <= captured.ObservedPermitDeadline)))
            return CoverageTransferReviewCheck.Of(CoverageTransferReviewOutcome.Stale, "PermitEvidenceChanged");

        var second = await store.CaptureAsync(transferId, expectedRevision, termsDigest, answer, ct);
        if (transportClock.GetUtcNow() >= check.Deadline)
            return CoverageTransferReviewCheck.Of(CoverageTransferReviewOutcome.Unavailable, "ShopAuthorityUnavailable");
        if (second is not { Outcome: CoverageTransferReviewOutcome.Current, Observation: { } current })
            return second;
        return SameEvidence(captured, current)
            ? second with { Observation = current with
                { MerchantPermitCount = answer.OutstandingPermitCount, MerchantMaxPermitValidUntil = answer.MaxPermitValidUntil,
                    MerchantPermitDigest = answer.PermitDigest, MerchantProof = answer, ProofDeadline = check.Deadline } }
            : CoverageTransferReviewCheck.Of(CoverageTransferReviewOutcome.Stale, "ChangedDuringCheck");
    }

    private static CoverageTransferShopAuthorityCheckV1 Challenge(CoverageTransferReviewObservation o, DateTimeOffset deadline)
    {
        var shop = o.Consents.Single(c => c.Purpose == CoverageTransfersV1.ShopOwner);
        return new(CoverageTransfersV1.SchemaVersion, Guid.NewGuid(), o.TransferId, o.SourceAccountId, o.DestinationAccountId,
            o.ShopId, o.SourceAffiliationId, o.ProposalRevision, o.TermsDigest, shop.EventId, shop.ActorId, shop.MemberId,
            shop.AuthorityRevision, deadline);
    }

    private static bool SameEvidence(CoverageTransferReviewObservation a, CoverageTransferReviewObservation b) =>
        a.TransferId == b.TransferId && a.ProposalRevision == b.ProposalRevision && a.TermsDigest == b.TermsDigest &&
        a.ObservedPermitCount == b.ObservedPermitCount && a.ObservedPermitDeadline == b.ObservedPermitDeadline &&
        a.Consents.SequenceEqual(b.Consents);
}
