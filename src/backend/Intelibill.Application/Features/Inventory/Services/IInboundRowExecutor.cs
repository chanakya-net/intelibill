using ErrorOr;
using Intelibill.Application.Features.Inventory.DTOs;

namespace Intelibill.Application.Features.Inventory.Services;

/// <summary>The durable identity of one row of a keyed bulk intake.</summary>
/// <param name="Key">Unique per shop for this row of this operation; see <c>AddInventoryBatchCommandHandler</c>.</param>
/// <param name="RequestHash">Hash of the row's business payload; a resend with another hash is refused.</param>
public sealed record InboundRowOperation(string Key, string RequestHash);

/// <summary>
/// Receives one stock-intake row as its own unit of work.
/// </summary>
/// <remarks>
/// <para>
/// Each call runs in a fresh scope — its own <c>DbContext</c>, change tracker and item/inventory
/// caches — and commits the row's item, batch, movement, inventory and payable together or not at
/// all. Nothing a rejected row staged can reach a later row's save.
/// </para>
/// <para>
/// With an <see cref="InboundRowOperation"/> the row runs under a financial operation receipt: a
/// row already committed under that key replays its stored result without running again.
/// </para>
/// <para>
/// A business rejection comes back as its own errors. For a keyed row, an unplanned failure while
/// the row is being staged and saved — a dropped connection, a lost unique-index race — comes back
/// as <c>Inventory.RowRetryable</c>, and nothing was committed. A failure that may follow a commit
/// (completing or committing the receipt, replaying it) and any failure of an unkeyed row throw.
/// Cancellation is never swallowed.
/// </para>
/// </remarks>
public interface IInboundRowExecutor
{
    Task<ErrorOr<AddInventoryResultDto>> ExecuteAsync(
        Guid shopId,
        Guid actorUserId,
        InboundRowOperation? operation,
        InboundInventoryLineInput row,
        CancellationToken cancellationToken);
}
