namespace Intelibill.Licensing.Application.Payments;

public interface IRefundReconciliation
{
    Task<bool> RunOneAsync(CancellationToken ct);
}
