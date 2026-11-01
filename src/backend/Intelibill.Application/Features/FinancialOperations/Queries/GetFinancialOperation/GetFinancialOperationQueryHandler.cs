using System.Text.Json;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.FinancialOperations.Queries.GetFinancialOperation;

/// <summary>
/// Read-only recovery of a financial operation's committed outcome, for any active owner or
/// manager of the shop — not only its initiator, so a colleague can reconcile it.
/// </summary>
/// <remarks>
/// Authorization runs before the lookup, so a caller without authority learns nothing about
/// whether the key exists. A miss means nothing committed is visible yet, never that a timed-out
/// request failed: the client converges by re-sending the identical request with the same key.
/// </remarks>
public sealed class GetFinancialOperationQueryHandler(
    IShopRepository shopRepository,
    IOperationReceiptRepository receiptRepository)
{
    public async Task<ErrorOr<FinancialOperationDto>> Handle(
        GetFinancialOperationQuery query,
        CancellationToken cancellationToken)
    {
        var membership = await shopRepository.GetMembershipAsync(query.UserId, query.ShopId, cancellationToken);
        if (membership is null || !membership.IsActive)
            return Errors.Shop.MembershipNotFound;

        if (membership.Role is not (ShopRole.Owner or ShopRole.Manager))
            return Errors.Operation.UserIsNotOwnerOrManager;

        var operationType = FinancialOperationTypes.FromSlug(query.OperationType);
        if (operationType is null)
            return Errors.Operation.UnknownType;

        var key = query.OperationKey.Trim();
        if (key.Length is 0 or > OperationReceipt.MaxKeyLength)
            return Errors.Operation.InvalidKey;

        var receipt = await receiptRepository.GetCompletedAsync(query.ShopId, operationType.Value, key, cancellationToken);
        if (receipt?.ResponseBody is null || receipt.CompletedAt is null)
            return Errors.Operation.NotFound;

        using var response = JsonDocument.Parse(receipt.ResponseBody);
        return new FinancialOperationDto(
            FinancialOperationTypes.ToSlug(receipt.OperationType),
            receipt.OperationKey,
            receipt.TargetId,
            receipt.ActorUserId,
            receipt.CompletedAt.Value,
            response.RootElement.Clone());
    }
}
