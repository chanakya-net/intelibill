using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;
using Intelibill.Domain.Entities;

namespace Intelibill.Application.Unit.Tests.Features.Sales.Commands.SyncOfflineSales;

public class OfflineIssueTimingPolicyTests
{
    // Fiscal year 2025-26 runs 1 April 2025 to 31 March 2026 (UTC).
    private static readonly DateTimeOffset Day1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Day8 = Day1.AddDays(7);
    private static readonly InvoiceLease Lease = CreateLease(Day1, Day8, fiscalYearStart: 2025);

    [Fact]
    public void ClaimAndReceiptInsideLease_Accepted()
    {
        Assert.Null(OfflineIssueTimingPolicy.Evaluate(Lease, Day1.AddDays(2), Day1.AddDays(4)));
    }

    [Fact]
    public void ClaimAtReservationMinusSkew_Accepted_AndOneTickEarlier_IsBeforeLease()
    {
        Assert.Null(OfflineIssueTimingPolicy.Evaluate(Lease, Day1, Day1.AddDays(1)));
        Assert.Null(OfflineIssueTimingPolicy.Evaluate(Lease, Day1.AddTicks(-1), Day1.AddDays(1)));
        Assert.Null(OfflineIssueTimingPolicy.Evaluate(Lease, Day1.AddMinutes(-5), Day1.AddDays(1)));
        AssertCode(Errors.OfflineReconciliation.IssueTimeBeforeLease, OfflineIssueTimingPolicy.Evaluate(Lease, Day1.AddMinutes(-5).AddTicks(-1), Day1.AddDays(1)));
    }

    [Fact]
    public void ClaimFiveMinutesAfterReceipt_Accepted_AndLaterIsInFuture()
    {
        var receivedAt = Day1.AddDays(2);

        Assert.Equal(TimeSpan.FromMinutes(5), OfflineIssueTimingPolicy.ClockSkewAllowance);
        Assert.Null(OfflineIssueTimingPolicy.Evaluate(Lease, receivedAt.AddMinutes(5), receivedAt));
        AssertCode(Errors.OfflineReconciliation.IssueTimeInFuture, OfflineIssueTimingPolicy.Evaluate(Lease, receivedAt.AddMinutes(5).AddTicks(1), receivedAt));
        AssertCode(Errors.OfflineReconciliation.IssueTimeInFuture, OfflineIssueTimingPolicy.Evaluate(Lease, receivedAt.AddMinutes(6), receivedAt));
    }

    [Fact]
    public void ClaimInAnotherFiscalYear_IsFiscalYearMismatch()
    {
        // The lease straddles 1 April 00:00 IST (31 March 18:30 UTC): the claim is inside the lease
        // window but in fiscal year 2026-27.
        var straddling = CreateLease(
            new DateTimeOffset(2026, 3, 28, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 4, 0, 0, 0, TimeSpan.Zero),
            fiscalYearStart: 2025);

        AssertCode(
            Errors.OfflineReconciliation.IssueFiscalYearMismatch,
            OfflineIssueTimingPolicy.Evaluate(straddling, new DateTimeOffset(2026, 3, 31, 18, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 4, 2, 0, 0, 0, TimeSpan.Zero)));
        Assert.Null(OfflineIssueTimingPolicy.Evaluate(straddling, new DateTimeOffset(2026, 3, 31, 18, 29, 59, TimeSpan.Zero), new DateTimeOffset(2026, 4, 2, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void FiscalYearFollowsTheIndiaBusinessDate_NotTheClaimedOffset()
    {
        var straddling = CreateLease(
            new DateTimeOffset(2026, 3, 28, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 4, 0, 0, 0, TimeSpan.Zero),
            fiscalYearStart: 2025);
        var received = new DateTimeOffset(2026, 4, 2, 0, 0, 0, TimeSpan.Zero);

        // 1 April 03:00 in India is still 31 March in UTC, but the business date is 1 April.
        AssertCode(
            Errors.OfflineReconciliation.IssueFiscalYearMismatch,
            OfflineIssueTimingPolicy.Evaluate(straddling, new DateTimeOffset(2026, 4, 1, 3, 0, 0, TimeSpan.FromHours(5.5)), received));

        // A device clock in another offset is read on the same calendar: 31 March 23:00 at -05:00 is 1 April in India.
        AssertCode(
            Errors.OfflineReconciliation.IssueFiscalYearMismatch,
            OfflineIssueTimingPolicy.Evaluate(straddling, new DateTimeOffset(2026, 3, 31, 23, 0, 0, TimeSpan.FromHours(-5)), received));
        Assert.Null(OfflineIssueTimingPolicy.Evaluate(straddling, new DateTimeOffset(2026, 3, 31, 23, 0, 0, TimeSpan.FromHours(5.5)), received));
    }

    [Fact]
    public void ClaimAtOrAfterExpiry_IsIssuedAfterLeaseExpiry()
    {
        AssertCode(Errors.OfflineReconciliation.IssuedAfterLeaseExpiry, OfflineIssueTimingPolicy.Evaluate(Lease, Day8, Day8.AddDays(1)));
        AssertCode(Errors.OfflineReconciliation.IssuedAfterLeaseExpiry, OfflineIssueTimingPolicy.Evaluate(Lease, Day1.AddDays(8), Day1.AddDays(9)));
    }

    [Fact]
    public void ReceiptAtOrAfterExpiry_RequiresHistoricalReview_EvenForAPlausibleClaim()
    {
        AssertCode(Errors.OfflineReconciliation.HistoricalLeaseReviewRequired, OfflineIssueTimingPolicy.Evaluate(Lease, Day1.AddDays(2), Day8));
        AssertCode(Errors.OfflineReconciliation.HistoricalLeaseReviewRequired, OfflineIssueTimingPolicy.Evaluate(Lease, Day8.AddMinutes(-1), Day8.AddMinutes(1)));
        Assert.Null(OfflineIssueTimingPolicy.Evaluate(Lease, Day1.AddDays(2), Day8.AddTicks(-1)));
    }

    [Fact]
    public void RulesApplyInOrder_BeforeLeaseWinsOverEverythingElse()
    {
        // Before the lease (beyond skew) and also after receipt + 5 min is impossible; before the lease and received after expiry is not.
        AssertCode(Errors.OfflineReconciliation.IssueTimeBeforeLease, OfflineIssueTimingPolicy.Evaluate(Lease, Day1.AddDays(-1), Day8.AddDays(2)));

        // In the future and after expiry: the future rule answers first.
        AssertCode(Errors.OfflineReconciliation.IssueTimeInFuture, OfflineIssueTimingPolicy.Evaluate(Lease, Day8.AddDays(1), Day1.AddDays(2)));
    }

    private static void AssertCode(ErrorOr.Error expected, ErrorOr.Error? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.Code, actual.Value.Code);
        Assert.Equal(ErrorOr.ErrorType.Validation, actual.Value.Type);
    }

    private static InvoiceLease CreateLease(DateTimeOffset reservedAt, DateTimeOffset expiresAt, int fiscalYearStart) =>
        InvoiceLease.Create(Guid.NewGuid(), Guid.NewGuid(), "device-1", fiscalYearStart, "INV-2025-26-", 1, 200, 6, reservedAt, expiresAt);
}
