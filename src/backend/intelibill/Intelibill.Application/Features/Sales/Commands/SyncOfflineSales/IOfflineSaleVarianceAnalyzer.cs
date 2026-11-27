using ErrorOr;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;

public interface IOfflineSaleVarianceAnalyzer
{
    Task<Guid> AddReviewIssueAsync(
        Guid shopId,
        Guid? saleId,
        string clientSaleId,
        string deviceId,
        ReconciliationIssueType issueType,
        string code,
        string message,
        Guid actorUserId,
        CancellationToken cancellationToken,
        string? invoiceNumber = null,
        Guid? offlineSubmissionId = null);

    Task<ReconciliationIssue> OpenOrAttachLicenceHistoryAsync(
        Guid shopId,
        Guid? saleId,
        string clientSaleId,
        string deviceId,
        string invoiceNumber,
        Guid offlineSubmissionId,
        Guid createdBy,
        Error reason,
        OfflineLicenceObservationFact? observation,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken);

    Task AttachLicenceObservationAsync(
        ReconciliationIssue existingCase,
        Guid offlineSubmissionId,
        OfflineLicenceObservationFact observation,
        DateTimeOffset observedAt,
        bool advanceVersionOnNewObservation,
        CancellationToken cancellationToken);

    Task PersistReviewIssuesAsync(
        Guid saleId,
        IEnumerable<ReconciliationIssue> issues,
        CancellationToken cancellationToken);
}
