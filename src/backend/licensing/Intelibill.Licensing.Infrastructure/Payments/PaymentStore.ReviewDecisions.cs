using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.Payments;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Payments;

internal sealed partial class PaymentStore
{
    public Task<ErrorOr<T>> InReviewScopeAsync<T>(AdminSessionCaller caller, Guid account, bool write,
        Func<DateTimeOffset, CancellationToken, Task<ErrorOr<T>>> operation, CancellationToken ct) =>
        InManualScopeAsync(caller, account, Guid.Empty, true, write, operation, ct);

    public Task<UnmatchedPayment?> FindCaseAsync(Guid account, Guid id, CancellationToken ct) =>
        ReviewCases(account).SingleOrDefaultAsync(row => row.CaseId == id, ct);

    public async Task<PaymentReviewDecision?> FindDecisionAsync(Guid account, Guid key, bool byOperation, CancellationToken ct)
    {
        var row = await FindReviewDecisionRowAsync(account, key, byOperation, ct);
        if (row is null)
            return null;
        var command = JsonSerializer.Deserialize<ReviewPaymentCommand>(
            ReviewProtector(account, row.CaseId, row.OperationKey).Unprotect(row.ProtectedPayload))
            ?? throw new CryptographicException("Payment review evidence cannot be read.");
        return new(row.CaseId, row.OperationKey, 1, command.Action, row.Disposition, row.AdminId, row.DecidedAt,
            row.MfaVerifiedAt, command.Reason, command.Evidence, row.TermId);
    }

    public async Task<bool> SameDecisionAsync(Guid account, PaymentReviewDecision decision, ReviewPaymentCommand command, CancellationToken ct)
    {
        var row = await FindReviewDecisionRowAsync(account, decision.OperationKey, true, ct);
        return row is not null && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(ReviewProtector(account, row.CaseId, row.OperationKey).Unprotect(row.ProtectedPayload)),
            Encoding.UTF8.GetBytes(Canonical(command)));
    }

    public Task SaveDecisionAsync(Guid account, AdminSessionCaller caller, PaymentReviewDecision decision, ReviewPaymentCommand command,
        string? acceptedSourceHash, string? recoveryPolicy, CancellationToken ct)
    {
        var protectedPayload = ReviewProtector(account, decision.CaseId, decision.OperationKey).Protect(Canonical(command));
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.payment_review_decisions (account_id, case_id, operation_key, admin_id, session_id, action, disposition,
                mfa_verified_at, decided_at, protected_payload, term_id, accepted_source_hash, reviewed_source_hash, recovery_policy)
            VALUES ({account}, {decision.CaseId}, {decision.OperationKey}, {caller.AdminId}, {caller.SessionId},
                {decision.Action}, {decision.Disposition}, {decision.MfaVerifiedAt}, {decision.DecidedAt}, {protectedPayload},
                {decision.TermId}, {acceptedSourceHash}, {command.SourceHash}, {recoveryPolicy})
            """, ct);
    }

    private Task<ReviewDecisionRow?> FindReviewDecisionRowAsync(Guid account, Guid key, bool byOperation, CancellationToken ct) =>
        db.Database.SqlQuery<ReviewDecisionRow>($"""
            SELECT case_id, operation_key, disposition, admin_id, decided_at, mfa_verified_at, protected_payload, term_id
            FROM public.payment_review_decisions WHERE account_id = {account} AND
                CASE WHEN {byOperation} THEN operation_key = {key} ELSE case_id = {key} END
            """).SingleOrDefaultAsync(ct);

    private IDataProtector ReviewProtector(Guid account, Guid id, Guid key) =>
        protection.CreateProtector("InteliBill.Licensing.PaymentReview/1", account.ToString("D"), id.ToString("D"), key.ToString("D"));

    private static string Canonical(ReviewPaymentCommand command) =>
        JsonSerializer.Serialize(command with { Reason = command.Reason.Trim(), Evidence = command.Evidence.Trim() });

    private sealed record ReviewDecisionRow(Guid CaseId, Guid OperationKey, string Disposition, Guid AdminId, DateTimeOffset DecidedAt,
        DateTimeOffset MfaVerifiedAt, string ProtectedPayload, Guid? TermId);
}
