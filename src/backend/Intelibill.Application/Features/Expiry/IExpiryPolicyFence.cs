using ErrorOr;

namespace Intelibill.Application.Features.Expiry;

/// <summary>Requires the caller's transaction. Lock after posting period and before till/party/document/batch.
/// Shared locks protect posting decisions; exclusive locks protect policy/date edits until commit.</summary>
public interface IExpiryPolicyFence
{
    Task<ErrorOr<Success>> AcquireAsync(Guid shopId, bool exclusive, CancellationToken ct);
}
