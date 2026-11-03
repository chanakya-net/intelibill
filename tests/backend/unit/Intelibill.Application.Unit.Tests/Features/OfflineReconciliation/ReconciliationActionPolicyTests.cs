using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.OfflineReconciliation;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Unit.Tests.Features.OfflineReconciliation;

public sealed class ReconciliationActionPolicyTests
{
    private static readonly Guid ShopId = Guid.NewGuid();
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Cashier = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(ReconciliationIssueType.PricingVariance)]
    [InlineData(ReconciliationIssueType.DiscountVariance)]
    [InlineData(ReconciliationIssueType.CustomerVariance)]
    public void InformationalVariance_OffersAcknowledgeAndEscalate(ReconciliationIssueType type)
    {
        var issue = Case(type, "offline_sync.variance");

        var actions = ReconciliationActionPolicy.AvailableActions(issue, null, ShopRole.Manager, Owner);

        Assert.Equal([ReconciliationAction.AcknowledgeVariance, ReconciliationAction.Escalate], actions);
    }

    [Fact]
    public void StockVariance_PrintedMoreThanConsumed_OffersLinkStockCorrectionAndEscalate()
    {
        var issue = StockCase(printed: 5m, consumed: 3m);

        var actions = ReconciliationActionPolicy.AvailableActions(issue, null, ShopRole.Owner, Owner);

        Assert.Equal([ReconciliationAction.LinkStockCorrection, ReconciliationAction.Escalate], actions);
    }

    [Fact]
    public void StockVariance_WithoutUnresolvedQuantityOrBatch_OffersOnlyEscalate()
    {
        var consumed = StockCase(printed: 5m, consumed: 5m);
        var noBatch = ReconciliationIssue.Create(
            ShopId, Guid.NewGuid(), "client-1", "device-1", ReconciliationIssueType.StockVariance, "offline_sync.stock_shortage", "short", Cashier,
            itemId: Guid.NewGuid(), inventoryBatchId: null, printedQuantity: 5m, availableQuantity: 3m, consumedQuantity: 3m);

        Assert.Equal([ReconciliationAction.Escalate], ReconciliationActionPolicy.AvailableActions(consumed, null, ShopRole.Owner, Owner));
        Assert.Equal([ReconciliationAction.Escalate], ReconciliationActionPolicy.AvailableActions(noBatch, null, ShopRole.Owner, Owner));
    }

    [Theory]
    [InlineData("OfflineReconciliation.HistoricalLeaseReviewRequired")]
    [InlineData("OfflineReconciliation.RecoveryReviewRequired")]
    [InlineData("OfflineReconciliation.IssuerNotSubmitter")]
    public void HeldEnvelope_WithAttributableUser_OffersRetryLinkAndEscalate(string code)
    {
        var envelope = Envelope(OfflineSubmissionSource.Sync, asserted: null, original: null);
        var issue = Case(ReconciliationIssueType.IssueTimingReview, code, envelope.Id);

        var actions = ReconciliationActionPolicy.AvailableActions(issue, envelope, ShopRole.Owner, Owner);

        Assert.Equal(
            [ReconciliationAction.RetryHistoricalLease, ReconciliationAction.LinkExistingSale, ReconciliationAction.Escalate],
            actions);
    }

    [Fact]
    public void HeldEnvelope_WithoutAttributableUser_OffersLinkAndEscalate()
    {
        // A recovered invoice whose issuer could not be verified names nobody a replay may be attributed to.
        var envelope = Envelope(OfflineSubmissionSource.Recovery, asserted: Cashier, original: null);
        var issue = Case(ReconciliationIssueType.RecoveryReview, Errors.OfflineReconciliation.RecoveryReviewRequired.Code, envelope.Id);

        var actions = ReconciliationActionPolicy.AvailableActions(issue, envelope, ShopRole.Owner, Owner);

        Assert.Equal([ReconciliationAction.LinkExistingSale, ReconciliationAction.Escalate], actions);
    }

    [Theory]
    [InlineData("Sale.IdempotencyConflict")]
    [InlineData("Sale.InvoiceNumberAlreadyUsed")]
    public void ConflictEnvelope_OffersLinkExistingSaleAndEscalate(string code)
    {
        var envelope = Envelope(OfflineSubmissionSource.Sync, asserted: null, original: null);
        var issue = Case(ReconciliationIssueType.ValidationConflict, code, envelope.Id);

        var actions = ReconciliationActionPolicy.AvailableActions(issue, envelope, ShopRole.Manager, Owner);

        Assert.Equal([ReconciliationAction.LinkExistingSale, ReconciliationAction.Escalate], actions);
    }

    [Theory]
    [InlineData("Sale.IdempotencyConflict")]
    [InlineData("OfflineReconciliation.HistoricalLeaseReviewRequired")]
    public void LegacyHeldCaseWithoutEnvelope_OffersOnlyEscalate(string code)
    {
        var issue = Case(ReconciliationIssueType.InvoiceConflict, code);

        Assert.Equal([ReconciliationAction.Escalate], ReconciliationActionPolicy.AvailableActions(issue, null, ShopRole.Owner, Owner));
    }

    [Theory]
    [InlineData(ReconciliationIssueType.ArithmeticConflict, "Sale.OfflineArithmeticMismatch")]
    [InlineData(ReconciliationIssueType.InvoiceConflict, "Sale.InvoiceLeaseNotFound")]
    [InlineData(ReconciliationIssueType.IssueTimingReview, "OfflineReconciliation.IssueTimeBeforeLease")]
    [InlineData(ReconciliationIssueType.IssueTimingReview, "OfflineReconciliation.IssuedAfterLeaseExpiry")]
    [InlineData(ReconciliationIssueType.ProvenanceReview, "OfflineReconciliation.ProvenanceUnknown")]
    [InlineData(ReconciliationIssueType.ProvenanceReview, "OfflineReconciliation.IssuerUnverified")]
    public void EverythingElse_OffersOnlyEscalate(ReconciliationIssueType type, string code)
    {
        var envelope = Envelope(OfflineSubmissionSource.Sync, asserted: null, original: null);
        var issue = Case(type, code, envelope.Id);

        Assert.Equal([ReconciliationAction.Escalate], ReconciliationActionPolicy.AvailableActions(issue, envelope, ShopRole.Owner, Owner));
    }

    [Fact]
    public void Escalated_KeepsResolutionsButNotEscalate()
    {
        var variance = Case(ReconciliationIssueType.PricingVariance, "offline_sync.variance");
        variance.Escalate(Owner, Now);
        var provenance = Case(ReconciliationIssueType.ProvenanceReview, Errors.OfflineReconciliation.ProvenanceUnknown.Code);
        provenance.Escalate(Owner, Now);

        Assert.Equal([ReconciliationAction.AcknowledgeVariance], ReconciliationActionPolicy.AvailableActions(variance, null, ShopRole.Owner, Owner));
        Assert.Empty(ReconciliationActionPolicy.AvailableActions(provenance, null, ShopRole.Owner, Owner));
    }

    [Fact]
    public void Staff_OwnOpenCase_OffersOnlyEscalate()
    {
        var issue = Case(ReconciliationIssueType.PricingVariance, "offline_sync.variance", createdBy: Cashier);

        Assert.Equal([ReconciliationAction.Escalate], ReconciliationActionPolicy.AvailableActions(issue, null, ShopRole.Staff, Cashier));
    }

    [Fact]
    public void Staff_OtherUsersCaseOrEscalatedCase_OffersNothing()
    {
        var others = Case(ReconciliationIssueType.PricingVariance, "offline_sync.variance", createdBy: Owner);
        var escalated = Case(ReconciliationIssueType.PricingVariance, "offline_sync.variance", createdBy: Cashier);
        escalated.Escalate(Cashier, Now);

        Assert.Empty(ReconciliationActionPolicy.AvailableActions(others, null, ShopRole.Staff, Cashier));
        Assert.Empty(ReconciliationActionPolicy.AvailableActions(escalated, null, ShopRole.Staff, Cashier));
    }

    [Fact]
    public void Resolved_OffersNothing()
    {
        var issue = Case(ReconciliationIssueType.PricingVariance, "offline_sync.variance", createdBy: Cashier);
        issue.Close(ReconciliationAction.AcknowledgeVariance, Owner, Now, "Checked", null, null);

        Assert.Empty(ReconciliationActionPolicy.AvailableActions(issue, null, ShopRole.Owner, Owner));
        Assert.Empty(ReconciliationActionPolicy.AvailableActions(issue, null, ShopRole.Staff, Cashier));
    }

    private static ReconciliationIssue Case(
        ReconciliationIssueType type,
        string code,
        Guid? submissionId = null,
        Guid? createdBy = null) =>
        ReconciliationIssue.Create(
            ShopId,
            null,
            "client-1",
            "device-1",
            type,
            code,
            "message",
            createdBy ?? Cashier,
            invoiceNumber: "INV-2026-27-000001",
            offlineSubmissionId: submissionId);

    private static ReconciliationIssue StockCase(decimal printed, decimal consumed) =>
        ReconciliationIssue.Create(
            ShopId,
            Guid.NewGuid(),
            "client-1",
            "device-1",
            ReconciliationIssueType.StockVariance,
            "offline_sync.stock_shortage",
            "short",
            Cashier,
            itemId: Guid.NewGuid(),
            inventoryBatchId: Guid.NewGuid(),
            printedQuantity: printed,
            availableQuantity: consumed,
            consumedQuantity: consumed);

    private static OfflineSubmission Envelope(OfflineSubmissionSource source, Guid? asserted, Guid? original) =>
        OfflineSubmission.Record(
            ShopId,
            "device-1",
            "client-1",
            "INV-2026-27-000001",
            "HASH",
            "{}",
            Now,
            Now,
            source,
            source == OfflineSubmissionSource.Recovery ? Owner : Cashier,
            asserted,
            original,
            Guid.NewGuid(),
            source == OfflineSubmissionSource.Recovery ? "op-1" : null);
}
