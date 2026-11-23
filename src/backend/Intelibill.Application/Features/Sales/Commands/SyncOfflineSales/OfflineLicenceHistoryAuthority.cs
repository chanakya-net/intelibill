using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Licensing.Offline;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;

public interface IOfflineLicenceHistoryAuthority
{
    Task<OfflineSalePostingOutcome?> EvaluateAsync(ShopLicenceLease licence, OfflineSalePostingRequest request, CancellationToken ct);
}

internal sealed class OfflineLicenceHistoryAuthority(IOfflineLicenceEvidenceStore store, IOfflineLicencePermitIssuer issuer)
    : IOfflineLicenceHistoryAuthority
{
    public async Task<OfflineSalePostingOutcome?> EvaluateAsync(ShopLicenceLease licence, OfflineSalePostingRequest request, CancellationToken ct)
    {
        var decision = licence.CurrentDecision();
        if (!decision.Enforced) return null;
        if (decision.Denial is { } denial) return new OfflineSalePostingOutcome.Rejected(denial);
        var evidence = request.Sale.LicenceEvidence;
        if (request.Lease is not { } lease) return Hold(Errors.OfflineLicenceHistory.EvidenceMissing);
        if (licence.UndatedTradingRestriction) return Hold(Errors.OfflineLicenceHistory.RestrictionConflict);
        if (await store.HasConflictingReceiptAsync(request.ShopId, request.DeviceId, request.ClientSaleId,
            request.Sale.InvoiceNumber.Trim(), request.PayloadHash, ct)) return Hold(Errors.OfflineLicenceHistory.ConflictingReceipt);
        if (evidence is null)
        {
            if (await store.HasGrantForLeaseAsync(request.ShopId, lease.Id, ct)) return Hold(Errors.OfflineLicenceHistory.EvidenceMissing);
            // Pre-permit leases retain their established historical recovery only with a known access end.
            if (decision.Outcome == LicenceOutcome.RequiresProvenSource && (decision.SourceCompletionCutoff is null ||
                lease.ReservedByUserId != request.AttributedUserId || licence.ReleasedAccountRestriction > 0 ||
                licence.ReleasedShopRestriction > 0))
                return Hold(Errors.OfflineLicenceHistory.EvidenceMissing);
        }
        else if (await CheckPermitAsync(licence, request, evidence, lease.Id, ct) is { } invalid) return invalid;

        return decision.SourceCompletionCutoff <= request.Sale.SoldAt ||
            (decision.Status == "TradingRestricted" && decision.SourceCompletionCutoff is null)
            ? Hold(Errors.OfflineLicenceHistory.RestrictionConflict) : null;
    }

    private async Task<OfflineSalePostingOutcome?> CheckPermitAsync(ShopLicenceLease licence, OfflineSalePostingRequest request,
        FrozenLicenceEvidence evidence, Guid leaseId, CancellationToken ct)
    {
        if (evidence.Version != 1) return Hold(Errors.OfflineLicenceHistory.EvidenceUnsupported);
        if (evidence.Id("snapshotId") is not { } snapshot || evidence.Id("leaseId") != leaseId ||
            request.Sale.IssuedByUserId != request.AttributedUserId) return Hold(Errors.OfflineLicenceHistory.EvidenceInvalid);
        var permit = await store.FindPermitAsync(request.ShopId, snapshot, ct);
        if (permit is null || request.ReceivedPermitId != permit.Id || evidence.Id("permitId") != permit.Id || evidence.Id("grantId") != permit.GrantId ||
            evidence.Text("signedPermit") != permit.SignedPermit || evidence.Text("keyId") != permit.KeyId ||
            permit.TillSessionId != request.Sale.Till?.SessionId || permit.LeaseId != leaseId)
            return Hold(Errors.OfflineLicenceHistory.EvidenceInvalid);

        var grant = await store.FindGrantAsync(request.ShopId, permit.GrantId, ct);
        if (grant is null || grant.AccountId != licence.AccountId || grant.AffiliationRequestId != licence.AffiliationRequestId ||
            grant.LeaseId != leaseId || evidence.Text("grantSha256") != OfflinePermitContractV1.Digest(grant.SignedGrant))
            return Hold(Errors.OfflineLicenceHistory.EvidenceInvalid);

        if (licence.ReleasedAccountRestriction > permit.CurrentAccountRevision ||
            licence.ReleasedShopRestriction > permit.CurrentShopRevision) return Hold(Errors.OfflineLicenceHistory.RestrictionConflict);

        var soldAt = request.Sale.SoldAt;
        if (soldAt < permit.NotBefore || soldAt >= permit.ValidUntil || request.FirstReceivedAt is not { } receipt ||
            receipt < permit.NotBefore || soldAt > receipt + OfflineIssueTimingPolicy.ClockSkewAllowance)
            return Hold(Errors.OfflineLicenceHistory.OutsidePermitWindow);
        var scope = new OfflinePermitScope(request.ShopId, request.AttributedUserId, grant.SessionId, request.DeviceId, leaseId);
        var proof = await issuer.VerifyAsync(permit.SignedPermit, scope, soldAt, ct);
        return proof.IsError || proof.Value.PermitId != permit.Id || proof.Value.GrantId != grant.Id ||
            proof.Value.GrantSha256 != evidence.Text("grantSha256") || proof.Value.SnapshotId != snapshot
            ? Hold(Errors.OfflineLicenceHistory.EvidenceInvalid) : null;
    }

    private static OfflineSalePostingOutcome.Held Hold(Error error) =>
        new(ReconciliationIssueType.ProvenanceReview, error, error.Description, []);
}
