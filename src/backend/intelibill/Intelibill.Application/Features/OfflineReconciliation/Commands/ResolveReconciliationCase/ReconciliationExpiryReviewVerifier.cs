using Intelibill.Application.Features.Expiry;
using Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using static Intelibill.Application.Features.OfflineReconciliation.Commands.ResolveReconciliationCase.ResolveReconciliationCaseCommandHandler;

namespace Intelibill.Application.Features.OfflineReconciliation.Commands.ResolveReconciliationCase;

/// <summary>
/// Decides whether an expiry-review case's frozen envelope and its posted sale pass the expiry
/// evidence checks, and whether the envelope may still be reconciled into its till.
/// </summary>
public sealed class ReconciliationExpiryReviewVerifier(
    ISaleRepository saleRepository,
    IOfflineExpiryAuthority expiryAuthority,
    OfflineTillReconciliationEligibility tillEligibility)
{
    internal async Task<ExpiryChecks> VerifyExpiryReviewAsync(
        ReconciliationIssue issue,
        OfflineSubmission? submission,
        ResolveReconciliationCaseCommand command,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var tillEligible = false;
        var expiryVerified = false;
        var expiryLinkVerified = false;
        if (issue.IssueType == ReconciliationIssueType.ExpiryReview && submission is not null)
        {
            var payload = OfflineSubmissionPayload.TryReadValidated(command.ShopId, submission);
            expiryVerified = payload is not null && payload.Items.Where(x => !x.IsServiceLine).All(x => x.ExpiryEvidence is not null) &&
                !(await expiryAuthority.ValidateFrozenAsync(command.ShopId, payload, now, cancellationToken)).IsError;
            tillEligible = expiryVerified && submission.AttributableUserId is { } issuer &&
                await tillEligibility.CanReconcileAsync(command.ShopId, payload!, issuer, now, cancellationToken);
            var posted = command.LinkedOperationId is { } linked
                ? await saleRepository.GetByIdAsync(linked, command.ShopId, cancellationToken)
                : await saleRepository.GetByClientSaleIdAsync(command.ShopId, submission.DeviceId, submission.ClientSaleId, cancellationToken);
            expiryLinkVerified = posted is not null && await expiryAuthority.VerifiesPostedSaleAsync(submission, posted, cancellationToken);
        }

        return new ExpiryChecks(tillEligible, expiryVerified, expiryLinkVerified);
    }
}
