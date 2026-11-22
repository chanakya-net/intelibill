using ErrorOr;
using Intelibill.Application.Common.Licensing;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Common.Operations;

/// <summary>The uniqueness scope of one financial operation: shop, type and key — never the actor.</summary>
public sealed record FinancialOperationScope(Guid ShopId, FinancialOperationType OperationType, string Key)
{
    public LicenceAction? SuspensionAction { get; init; }

    /// <summary>Live caller authority for a duplicate read, independent of the request hash.</summary>
    public Func<CancellationToken, Task<Error?>>? RecheckReplayAuthority { get; init; }

    /// <summary>Reevaluates write authority after completing the receipt while rollback is still possible.</summary>
    public Func<CancellationToken, Task<Error?>>? RecheckBeforeCommit { get; init; }
}
