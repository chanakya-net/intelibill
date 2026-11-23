using System.Text.Json;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Licensing.Offline;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;

public enum OfflineLicenceHistoryDecision
{
    ObserveOnlyAllow = 1,
    Supported = 2,
    Hold = 3,
    Denied = 4,
}

/// <summary>Safe, bounded observation facts for persistence and case detail display (never raw tokens).</summary>
public sealed record OfflineLicenceObservationFact(
    string ReasonCode,
    string Subreason,
    string? ComparedField = null,
    DateTimeOffset? WindowStart = null,
    DateTimeOffset? WindowEnd = null,
    long? ObservedAccountRevision = null,
    long? ObservedShopRevision = null,
    DateTimeOffset? SourceCompletionCutoff = null,
    Guid? CompetingSubmissionId = null);

public sealed record OfflineLicenceHistoryResult(
    OfflineLicenceHistoryDecision Decision,
    Error? Error = null,
    OfflineLicenceObservationFact? Observation = null,
    OfflineLicensePermit? MatchedPermit = null,
    OfflineLicenseGrant? MatchedGrant = null,
    DateTimeOffset? SourceCompletionCutoff = null)
{
    public OfflineSalePostingOutcome? ToPostingOutcome() => Decision switch
    {
        OfflineLicenceHistoryDecision.ObserveOnlyAllow or OfflineLicenceHistoryDecision.Supported => null,
        OfflineLicenceHistoryDecision.Denied => new OfflineSalePostingOutcome.Rejected(Error!.Value),
        _ => new OfflineSalePostingOutcome.Held(
            ReconciliationIssueType.LicenseHistoryReview,
            Error!.Value,
            Error.Value.Description,
            [],
            Observation),
    };
}

public interface IOfflineLicenceHistoryAuthority
{
    Task<OfflineSalePostingOutcome?> EvaluateAsync(ShopLicenceLease licence, OfflineSalePostingRequest request, CancellationToken ct);

    Task<OfflineLicenceHistoryResult> AssessAsync(
        ShopLicenceLease licence,
        OfflineSalePostingRequest request,
        bool existingHistoryHold,
        CancellationToken ct);
}

internal sealed class OfflineLicenceHistoryAuthority(IOfflineLicenceEvidenceStore store, IOfflineLicencePermitIssuer issuer)
    : IOfflineLicenceHistoryAuthority
{
    public async Task<OfflineSalePostingOutcome?> EvaluateAsync(
        ShopLicenceLease licence,
        OfflineSalePostingRequest request,
        CancellationToken ct) =>
        (await AssessAsync(licence, request, request.IsExistingHistoryReview, ct)).ToPostingOutcome();

    public async Task<OfflineLicenceHistoryResult> AssessAsync(
        ShopLicenceLease licence,
        OfflineSalePostingRequest request,
        bool existingHistoryHold,
        CancellationToken ct)
    {
        var decision = licence.CurrentDecision();
        if (!decision.Enforced && !existingHistoryHold)
            return new(OfflineLicenceHistoryDecision.ObserveOnlyAllow, SourceCompletionCutoff: decision.SourceCompletionCutoff);

        if (decision.Denial is { } denial && (decision.Enforced || existingHistoryHold))
        {
            var subreason = string.Equals(denial.Code, Errors.Licence.SecuritySuspended.Code, StringComparison.Ordinal)
                ? "SecuritySuspended"
                : "LicenceDenied";
            var obs = new OfflineLicenceObservationFact(
                denial.Code, subreason, "restriction",
                ObservedAccountRevision: licence.AccountRevision,
                ObservedShopRevision: licence.ShopRevision,
                SourceCompletionCutoff: decision.SourceCompletionCutoff);
            return decision.Enforced
                ? new(OfflineLicenceHistoryDecision.Denied, denial, obs, SourceCompletionCutoff: decision.SourceCompletionCutoff)
                : new(OfflineLicenceHistoryDecision.Hold, Errors.OfflineLicenceHistory.RestrictionConflict, obs, SourceCompletionCutoff: decision.SourceCompletionCutoff);
        }

        if (request.Lease is not { } lease)
            return Hold(Errors.OfflineLicenceHistory.EvidenceMissing, "LeaseMissing", "leaseId", licence, decision);

        if (licence.UndatedTradingRestriction)
            return Hold(Errors.OfflineLicenceHistory.RestrictionConflict, "UndatedTradingRestriction", "restriction", licence, decision);

        var competingId = await store.FindConflictingReceiptIdAsync(
            request.ShopId, request.DeviceId, request.ClientSaleId,
            request.Sale.InvoiceNumber.Trim(), request.PayloadHash, ct);
        if (competingId is { } competing)
        {
            return Hold(Errors.OfflineLicenceHistory.ConflictingReceipt, "CompetingReceipt", "payloadHash",
                licence, decision, competingSubmissionId: competing);
        }

        var evidence = request.Sale.LicenceEvidence;
        if (evidence is { IsObject: false })
            return Hold(Errors.OfflineLicenceHistory.EvidenceUnsupported, "EvidenceMalformed", "licenceEvidence", licence, decision);
        if (evidence is { Version: not 1 })
            return Hold(Errors.OfflineLicenceHistory.EvidenceUnsupported, "EvidenceVersionUnsupported", "version", licence, decision);
        if (request.AttributedUserId == Guid.Empty)
        {
            var subreason = request.Sale.IssuedByUserId.HasValue ? "IssuerUnverified" : "IssuerMissing";
            return Hold(Errors.OfflineLicenceHistory.EvidenceMissing, subreason, "issuedByUserId", licence, decision);
        }

        if (evidence is null)
            return await CheckUnanchoredLeaseAsync(licence, decision, request, lease, existingHistoryHold, ct);

        return await CheckPermitAsync(licence, decision, request, evidence, lease, ct);
    }

    private async Task<OfflineLicenceHistoryResult> CheckUnanchoredLeaseAsync(
        ShopLicenceLease licence,
        LicenceDecision decision,
        OfflineSalePostingRequest request,
        InvoiceLease lease,
        bool existingHistoryHold,
        CancellationToken ct)
    {
        if (await store.HasGrantForLeaseAsync(request.ShopId, lease.Id, ct))
            return Hold(Errors.OfflineLicenceHistory.EvidenceMissing, "EvidenceMissingOnAnchoredLease", "licenceEvidence", licence, decision);

        if (existingHistoryHold && !decision.Enforced)
            return Hold(Errors.OfflineLicenceHistory.EvidenceMissing, "ObserveOnlyUnproven", "licenceEvidence", licence, decision);

        var soldAt = request.Sale.SoldAt;
        if (lease.ReservedByUserId != request.AttributedUserId)
            return Hold(Errors.OfflineLicenceHistory.EvidenceMissing, "LeaseReserverUnproven", "leaseId", licence, decision);

        if (request.FirstReceivedAt is not { } receipt
            || soldAt < lease.ReservedAt - OfflineIssueTimingPolicy.ClockSkewAllowance
            || soldAt >= lease.ExpiresAt
            || soldAt > receipt + OfflineIssueTimingPolicy.ClockSkewAllowance
            || FiscalYear.ForDate(soldAt).StartYear != lease.FiscalYearStart)
        {
            return Hold(Errors.OfflineLicenceHistory.OutsidePermitWindow, "OutsideLeaseWindow", "soldAt",
                licence, decision, windowStart: lease.ReservedAt, windowEnd: lease.ExpiresAt);
        }

        if (licence.ReleasedAccountRestriction > 0 || licence.ReleasedShopRestriction > 0)
            return Hold(Errors.OfflineLicenceHistory.RestrictionConflict, "RestrictionReleasedAfterIssue", "restriction", licence, decision);

        if (decision.SourceCompletionCutoff <= soldAt
            || (decision.Status == "TradingRestricted" && decision.SourceCompletionCutoff is null))
            return Hold(Errors.OfflineLicenceHistory.RestrictionConflict, "SoldAtAtOrAfterCutoff", "soldAt", licence, decision);

        if (decision.Outcome == LicenceOutcome.RequiresProvenSource && decision.SourceCompletionCutoff is null)
            return Hold(Errors.OfflineLicenceHistory.EvidenceMissing, "CutoffUnknown", "sourceCompletionCutoff", licence, decision);

        return await CheckRetainedCommercialCoverageAsync(licence, decision, request.ShopId, soldAt, ct);
    }

    private async Task<OfflineLicenceHistoryResult> CheckRetainedCommercialCoverageAsync(
        ShopLicenceLease licence,
        LicenceDecision decision,
        Guid shopId,
        DateTimeOffset soldAt,
        CancellationToken ct)
    {
        if (licence.AccountId is not { } accountId || licence.AffiliationRequestId is not { } affiliationId)
            return Hold(Errors.OfflineLicenceHistory.EvidenceMissing, "HistoricalCoverageUnproven", "licenceEvidence", licence, decision);

        var rows = await store.ReadGrantEvidenceAsync(shopId, accountId, affiliationId, ct);
        var covered = false;
        foreach (var row in rows)
        {
            if (licence.ShopRevision is { } maxShop && row.ShopRevision > maxShop) continue;
            if (licence.AccountRevision is { } maxAccount && row.AccountRevision > maxAccount) continue;
            if (!TryDecodeSnapshot(row.Payload, out var snapshot)) continue;

            if (HasRestrictionCovering(snapshot, soldAt))
                return Hold(Errors.OfflineLicenceHistory.RestrictionConflict, "HistoricalRestrictionConflict", "restriction", licence, decision);

            if (CoversSaleInstant(snapshot, row.IssuedAt, soldAt))
                covered = true;
        }

        return covered
            ? new(OfflineLicenceHistoryDecision.Supported, SourceCompletionCutoff: decision.SourceCompletionCutoff)
            : Hold(Errors.OfflineLicenceHistory.EvidenceMissing, "HistoricalCoverageUnproven", "licenceEvidence", licence, decision);
    }

    private async Task<OfflineLicenceHistoryResult> CheckPermitAsync(
        ShopLicenceLease licence,
        LicenceDecision decision,
        OfflineSalePostingRequest request,
        FrozenLicenceEvidence evidence,
        InvoiceLease lease,
        CancellationToken ct)
    {
        if (evidence.Id("snapshotId") is not { } snapshot)
            return Hold(Errors.OfflineLicenceHistory.EvidenceInvalid, "SnapshotIdInvalid", "snapshotId", licence, decision);
        if (evidence.Id("leaseId") != lease.Id)
            return Hold(Errors.OfflineLicenceHistory.EvidenceInvalid, "LeaseMismatch", "leaseId", licence, decision);
        if (request.Sale.IssuedByUserId != request.AttributedUserId)
            return Hold(Errors.OfflineLicenceHistory.EvidenceInvalid, "IssuerMismatch", "issuedByUserId", licence, decision);

        var permit = await store.FindPermitAsync(request.ShopId, snapshot, ct);
        if (CheckMatchedPermit(permit, request, evidence, lease.Id, licence, decision) is { } permitFailure)
            return permitFailure;

        var grant = await store.FindGrantAsync(request.ShopId, permit!.GrantId, ct);
        if (CheckMatchedGrant(grant, permit, request, evidence, lease, licence, decision) is { } grantFailure)
            return grantFailure;

        if (licence.ReleasedAccountRestriction > permit.CurrentAccountRevision
            || licence.ReleasedShopRestriction > permit.CurrentShopRevision)
        {
            return Hold(Errors.OfflineLicenceHistory.RestrictionConflict, "RestrictionReleasedAfterPermit", "restriction",
                licence, decision, permit, grant);
        }

        var soldAt = request.Sale.SoldAt;
        if (CheckPermitWindow(permit, grant!, soldAt, request.FirstReceivedAt, licence, decision) is { } windowFailure)
            return windowFailure;

        var scope = new OfflinePermitScope(request.ShopId, request.AttributedUserId, grant!.SessionId, request.DeviceId, lease.Id);
        var proof = await issuer.VerifyAsync(permit.SignedPermit, scope, soldAt, ct);
        if (proof.IsError)
        {
            var subreason = IsTemporaryVerificationFailure(proof.FirstError) ? "VerificationKeyUnavailable" : "SignatureInvalid";
            var field = subreason == "VerificationKeyUnavailable" ? "keyId" : "signedPermit";
            return Hold(Errors.OfflineLicenceHistory.EvidenceInvalid, subreason, field, licence, decision, permit, grant);
        }

        if (proof.Value.PermitId != permit.Id || proof.Value.GrantId != grant.Id
            || proof.Value.GrantSha256 != evidence.Text("grantSha256")
            || proof.Value.SnapshotId != snapshot
            || proof.Value.AccountId != grant.AccountId
            || proof.Value.TillSessionId != request.Sale.Till?.SessionId)
        {
            return Hold(Errors.OfflineLicenceHistory.EvidenceInvalid, "ClaimMismatch", "signedPermit", licence, decision, permit, grant);
        }

        if (decision.SourceCompletionCutoff <= soldAt
            || (decision.Status == "TradingRestricted" && decision.SourceCompletionCutoff is null))
        {
            return Hold(Errors.OfflineLicenceHistory.RestrictionConflict, "SoldAtAtOrAfterCutoff", "soldAt",
                licence, decision, permit, grant);
        }

        return new(OfflineLicenceHistoryDecision.Supported, MatchedPermit: permit, MatchedGrant: grant,
            SourceCompletionCutoff: decision.SourceCompletionCutoff);
    }

    private static OfflineLicenceHistoryResult? CheckMatchedPermit(
        OfflineLicensePermit? permit,
        OfflineSalePostingRequest request,
        FrozenLicenceEvidence evidence,
        Guid leaseId,
        ShopLicenceLease licence,
        LicenceDecision decision)
    {
        if (permit is null || request.ReceivedPermitId != permit.Id || evidence.Id("permitId") != permit.Id || evidence.Id("grantId") != permit.GrantId)
            return Hold(Errors.OfflineLicenceHistory.EvidenceInvalid, "PermitNotMatched", "permitId", licence, decision, permit);
        if (evidence.Text("keyId") != permit.KeyId)
            return Hold(Errors.OfflineLicenceHistory.EvidenceInvalid, "KeyMismatch", "keyId", licence, decision, permit);
        if (evidence.Text("signedPermit") != permit.SignedPermit)
            return Hold(Errors.OfflineLicenceHistory.EvidenceInvalid, "SignatureMismatch", "signedPermit", licence, decision, permit);
        if (permit.TillSessionId != request.Sale.Till?.SessionId)
            return Hold(Errors.OfflineLicenceHistory.EvidenceInvalid, "TillSessionMismatch", "tillSessionId", licence, decision, permit);
        if (permit.LeaseId != leaseId || permit.UserId != request.AttributedUserId
            || !string.Equals(permit.DeviceId, request.DeviceId, StringComparison.Ordinal))
            return Hold(Errors.OfflineLicenceHistory.EvidenceInvalid, "ActorScopeMismatch", "scope", licence, decision, permit);
        return null;
    }

    private static OfflineLicenceHistoryResult? CheckMatchedGrant(
        OfflineLicenseGrant? grant,
        OfflineLicensePermit permit,
        OfflineSalePostingRequest request,
        FrozenLicenceEvidence evidence,
        InvoiceLease lease,
        ShopLicenceLease licence,
        LicenceDecision decision)
    {
        if (grant is null || (request.ReceivedGrantId is { } capturedGrant && capturedGrant != grant.Id) || grant.LeaseId != lease.Id)
            return Hold(Errors.OfflineLicenceHistory.EvidenceInvalid, "GrantNotMatched", "grantId", licence, decision, permit);
        if (grant.AccountId != licence.AccountId || grant.AffiliationRequestId != licence.AffiliationRequestId)
            return Hold(Errors.OfflineLicenceHistory.EvidenceInvalid, "AffiliationMismatch", "grantId", licence, decision, permit, grant);
        if (grant.UserId != request.AttributedUserId
            || !string.Equals(grant.DeviceId, request.DeviceId, StringComparison.Ordinal)
            || permit.SessionId != grant.SessionId
            || (lease.ReservedByUserId is { } reserver && reserver != grant.UserId)
            || (lease.ReservedBySessionId is { } reservedSession && reservedSession != grant.SessionId))
            return Hold(Errors.OfflineLicenceHistory.EvidenceInvalid, "ActorScopeMismatch", "scope", licence, decision, permit, grant);
        if (evidence.Text("grantSha256") != OfflinePermitContractV1.Digest(grant.SignedGrant))
            return Hold(Errors.OfflineLicenceHistory.EvidenceInvalid, "GrantDigestMismatch", "grantSha256", licence, decision, permit, grant);
        return null;
    }

    private static OfflineLicenceHistoryResult? CheckPermitWindow(
        OfflineLicensePermit permit,
        OfflineLicenseGrant grant,
        DateTimeOffset soldAt,
        DateTimeOffset? firstReceivedAt,
        ShopLicenceLease licence,
        LicenceDecision decision)
    {
        if (soldAt < permit.NotBefore)
            return Hold(Errors.OfflineLicenceHistory.OutsidePermitWindow, "SoldAtBeforePermit", "soldAt", licence, decision, permit, grant);
        if (soldAt >= permit.ValidUntil)
            return Hold(Errors.OfflineLicenceHistory.OutsidePermitWindow, "SoldAtAtOrAfterPermitEnd", "soldAt", licence, decision, permit, grant);
        if (firstReceivedAt is not { } receipt || receipt < permit.NotBefore)
            return Hold(Errors.OfflineLicenceHistory.OutsidePermitWindow, "FirstReceiptBeforePermit", "firstReceivedAt", licence, decision, permit, grant);
        if (soldAt > receipt + OfflineIssueTimingPolicy.ClockSkewAllowance)
            return Hold(Errors.OfflineLicenceHistory.OutsidePermitWindow, "SoldAtAfterFirstReceiptSkew", "soldAt", licence, decision, permit, grant);
        return null;
    }

    private static bool IsTemporaryVerificationFailure(Error error) =>
        string.Equals(error.Code, Errors.OfflinePermit.UnknownKey.Code, StringComparison.Ordinal)
        || string.Equals(error.Code, Errors.OfflinePermit.AuthorityUnavailable.Code, StringComparison.Ordinal)
        || string.Equals(error.Code, Errors.Licence.VerificationUnavailable.Code, StringComparison.Ordinal);

    private static bool TryDecodeSnapshot(byte[] payload, out EntitlementSnapshotV1 snapshot)
    {
        try
        {
            var decoded = JsonSerializer.Deserialize<EntitlementSnapshotV1>(payload, EntitlementSnapshotsV1.Json);
            if (decoded is not null && decoded.HasValidIdentity())
            {
                snapshot = decoded;
                return true;
            }
        }
        catch (JsonException)
        {
        }

        snapshot = null!;
        return false;
    }

    private static bool HasRestrictionCovering(EntitlementSnapshotV1 snapshot, DateTimeOffset soldAt)
    {
        if (snapshot.Shop is { } shop)
        {
            if (shop.State == ShopClosuresV1.Closed || shop.ClosureRevision > 0 || shop.ReopenedRevision > 0 || shop.ReleasedRevision > 0)
                return true;
            if (shop.State == "Suspended" && (shop.Suspension is null || shop.Suspension.EffectiveAt <= soldAt))
                return true;
        }

        if (snapshot.Account is { } account)
        {
            if (account.ReleasedRevision > 0 || account.PreviousReleaseRevision > 0)
                return true;
            if (account.Restriction is "Trading" or "Security" && (account.Suspension is null || account.Suspension.EffectiveAt <= soldAt))
                return true;
        }

        return false;
    }

    private static bool CoversSaleInstant(EntitlementSnapshotV1 snapshot, DateTimeOffset issuedAt, DateTimeOffset soldAt) =>
        snapshot.Shop is { State: "Active" or "Grace", StartsAt: { } startsAt, AccessEndsAt: { } endsAt } shop
        && snapshot.Account is { Restriction: "Unrestricted" } account
        && shop.IsValid() && EntitlementSnapshotsV1.ReadRestrictionError(account) is null
        && shop.UpstreamVerifiedAt <= issuedAt
        && account.UpstreamVerifiedAt <= issuedAt
        && shop.UpstreamVerifiedAt <= soldAt
        && account.UpstreamVerifiedAt <= soldAt
        && startsAt <= soldAt
        && soldAt < endsAt;

    private static OfflineLicenceHistoryResult Hold(
        Error error,
        string subreason,
        string? comparedField,
        ShopLicenceLease licence,
        LicenceDecision decision,
        OfflineLicensePermit? permit = null,
        OfflineLicenseGrant? grant = null,
        DateTimeOffset? windowStart = null,
        DateTimeOffset? windowEnd = null,
        Guid? competingSubmissionId = null)
    {
        var observation = new OfflineLicenceObservationFact(
            error.Code,
            subreason,
            comparedField,
            windowStart ?? permit?.NotBefore,
            windowEnd ?? permit?.ValidUntil,
            licence.AccountRevision,
            licence.ShopRevision,
            decision.SourceCompletionCutoff,
            competingSubmissionId);
        return new(OfflineLicenceHistoryDecision.Hold, error, observation, permit, grant, decision.SourceCompletionCutoff);
    }
}
