using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Unit.Tests.Entities;

public sealed class ReconciliationIssueCaseTests
{
    private static readonly DateTimeOffset At = new(2026, 5, 10, 10, 0, 0, TimeSpan.Zero);

    private static ReconciliationIssue Create(
        ReconciliationIssueType type = ReconciliationIssueType.PricingVariance,
        decimal? printed = null,
        decimal? consumed = null) =>
        ReconciliationIssue.Create(
            Guid.NewGuid(), null, "client-1", "device-1", type, "Code", "Message", Guid.NewGuid(),
            printedQuantity: printed, consumedQuantity: consumed);

    [Fact]
    public void Create_StartsOpenWithEvidence()
    {
        var submissionId = Guid.NewGuid();
        var issue = ReconciliationIssue.Create(
            Guid.NewGuid(), null, "c", "d", ReconciliationIssueType.IssueTimingReview, "X", "Y", Guid.NewGuid(),
            invoiceNumber: "INV-1", offlineSubmissionId: submissionId);

        Assert.Equal(ReconciliationCaseStatus.Open, issue.Status);
        Assert.Equal("INV-1", issue.InvoiceNumber);
        Assert.Equal(submissionId, issue.OfflineSubmissionId);
    }

    [Fact]
    public void Escalate_FromOpen_SetsEscalated()
    {
        var issue = Create();

        var result = issue.Escalate(Guid.NewGuid(), At);

        Assert.False(result.IsError);
        Assert.Equal(ReconciliationCaseStatus.Escalated, issue.Status);
        Assert.False(issue.IsResolved);
    }

    [Fact]
    public void Escalate_WhenEscalated_Conflicts()
    {
        var issue = Create();
        issue.Escalate(Guid.NewGuid(), At);

        var result = issue.Escalate(Guid.NewGuid(), At);

        Assert.True(result.IsError);
        Assert.Equal("Reconciliation.CaseNotOpen", result.FirstError.Code);
    }

    [Fact]
    public void Close_FromEscalated_Resolves()
    {
        var issue = Create();
        issue.Escalate(Guid.NewGuid(), At);
        var actor = Guid.NewGuid();
        var linked = Guid.NewGuid();

        var result = issue.Close(ReconciliationAction.LinkExistingSale, actor, At, " done ", linked, null);

        Assert.False(result.IsError);
        Assert.Equal(ReconciliationCaseStatus.Resolved, issue.Status);
        Assert.True(issue.IsResolved);
        Assert.Equal(actor, issue.ResolvedBy);
        Assert.Equal(At, issue.ResolvedAt);
        Assert.Equal(ReconciliationAction.LinkExistingSale, issue.Resolution);
        Assert.Equal("done", issue.ResolutionReason);
        Assert.Equal(linked, issue.LinkedOperationId);
    }

    [Fact]
    public void Close_WhenResolved_Conflicts()
    {
        var issue = Create();
        issue.Close(ReconciliationAction.AcknowledgeVariance, Guid.NewGuid(), At, "ok", null, null);

        var result = issue.Close(ReconciliationAction.AcknowledgeVariance, Guid.NewGuid(), At, "again", null, null);

        Assert.True(result.IsError);
        Assert.Equal("Reconciliation.CaseAlreadyResolved", result.FirstError.Code);
    }

    [Fact]
    public void Resolve_SetsResolvedStatus()
    {
        var issue = Create();
        issue.Resolve(Guid.NewGuid(), At);
        Assert.Equal(ReconciliationCaseStatus.Resolved, issue.Status);
    }

    [Fact]
    public void UnresolvedQuantity_Printed5Consumed3_Is2() =>
        Assert.Equal(2m, Create(ReconciliationIssueType.StockVariance, 5m, 3m).UnresolvedQuantity);

    [Fact]
    public void UnresolvedQuantity_ConsumedAbovePrinted_IsZero() =>
        Assert.Equal(0m, Create(ReconciliationIssueType.StockVariance, 3m, 5m).UnresolvedQuantity);

    [Fact]
    public void UnresolvedQuantity_NonStockVariance_IsNull() =>
        Assert.Null(Create(ReconciliationIssueType.PricingVariance, 5m, 3m).UnresolvedQuantity);
}
