using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Entities;

/// <summary>
/// The durable identity and committed outcome of one financial command.
/// </summary>
/// <remarks>
/// <para>
/// Unique on shop, operation type and key — not on the actor — so another authorized operator in
/// the same shop reconciles the same operation instead of posting it again.
/// </para>
/// <para>
/// Reserved and completed with raw SQL inside the transaction that carries the business mutation
/// (see <c>FinancialOperationExecutor</c>); a committed row therefore always holds its response.
/// EF only reads it. Receipts are financial records: they are never deleted or expired.
/// </para>
/// </remarks>
public sealed class OperationReceipt : BaseEntity
{
    public const int MaxKeyLength = 128;

    public Guid ShopId { get; private set; }
    public FinancialOperationType OperationType { get; private set; }
    public string OperationKey { get; private set; } = string.Empty;

    /// <summary>SHA-256 hex of the canonical business payload, including the target.</summary>
    public string RequestHash { get; private set; } = string.Empty;
    public int HashVersion { get; private set; }

    public Guid ActorUserId { get; private set; }
    public Guid TargetId { get; private set; }
    public OperationReceiptStatus Status { get; private set; }

    public int ResponseVersion { get; private set; }

    /// <summary>The response the first commit returned, replayed verbatim to every retry.</summary>
    public string? ResponseBody { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    private OperationReceipt() { }
}
