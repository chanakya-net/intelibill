using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Payments;

public interface IPaymentConfirmationStore
{
    Task IssueAsync(Guid account, Guid order, Guid? attempt, Guid? manualReceipt, DateTimeOffset issuedAt, CancellationToken ct);
    Task<PaymentConfirmationV1[]> ListConfirmationsAsync(Guid account, Guid order, CancellationToken ct);
    Task<PaymentConfirmationSnapshotV1?> FindConfirmationAsync(Guid account, Guid order, Guid confirmation, CancellationToken ct);
}
