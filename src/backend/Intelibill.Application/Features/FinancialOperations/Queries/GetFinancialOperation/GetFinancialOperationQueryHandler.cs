using Intelibill.Application.Common.Licensing;
using System.Text.Json;
using Intelibill.Application.Common.Interfaces;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Payments.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.FinancialOperations.Queries.GetFinancialOperation;

/// <summary>
/// Read-only recovery of a financial operation's committed outcome, for any active owner or
/// manager of the shop within the operation’s authority rules. Batch disposition recovery requires a live owner.
/// </summary>
/// <remarks>
/// Authorization runs before the lookup, so a caller without authority learns nothing about
/// whether the key exists. A miss means nothing committed is visible yet, never that a timed-out
/// request failed: the client converges by re-sending the identical request with the same key.
/// </remarks>
public sealed class GetFinancialOperationQueryHandler(
    IShopRepository shopRepository,
    IOperationReceiptRepository receiptRepository,
    IPaymentTransactionRunner transactions,
    IShopLicenceFence fence,
    ILiveSessionAuthority sessions)
{
    [LicenceAction(LicenceAction.Recovery)]
    public async Task<ErrorOr<FinancialOperationDto>> Handle(
        GetFinancialOperationQuery query,
        CancellationToken cancellationToken)
    {
        var operationType = FinancialOperationTypes.FromSlug(query.OperationType);
        if (operationType is { } type && await CheckRecoveryAuthorityAsync(query, type, cancellationToken) is { } denied)
            return denied;

        var membership = await shopRepository.GetMembershipAsync(query.UserId, query.ShopId, cancellationToken);
        if (membership is null || !membership.IsActive)
            return Errors.Shop.MembershipNotFound;

        if (membership.Role is not (ShopRole.Owner or ShopRole.Manager))
            return Errors.Operation.UserIsNotOwnerOrManager;

        if (operationType is null)
            return Errors.Operation.UnknownType;

        var key = query.OperationKey.Trim();
        if (key.Length is 0 or > OperationReceipt.MaxKeyLength)
            return Errors.Operation.InvalidKey;

        if (operationType is FinancialOperationType.CustomerPayment or FinancialOperationType.SupplierPayment
            or FinancialOperationType.SupplierReturn or FinancialOperationType.SupplierReturnCredit or FinancialOperationType.PurchaseReceiptReversal
            or FinancialOperationType.ReceiptCostCorrection or FinancialOperationType.BatchDisposition)
            return await transactions.RunAsync<FinancialOperationDto>(null, query.UserId, query.ShopId, null,
                ct => RecoverUnderFenceAsync(query, operationType.Value, key, ct), cancellationToken);

        return await ReadOutcomeAsync(query.ShopId, operationType.Value, key, cancellationToken);
    }

    private async Task<ErrorOr<FinancialOperationDto>> RecoverUnderFenceAsync(GetFinancialOperationQuery query, FinancialOperationType type,
        string key, CancellationToken ct)
    {
        var lease = await fence.EnterAsync(query.ShopId, LicenceAction.Recovery, ct);
        if (lease.IsError) return lease.Errors;
        if (lease.Value.CurrentDecision().EnforcedDenial() is { } denied) return denied;

        if (await CheckRecoveryAuthorityAsync(query, type, ct) is { } authorityDenied)
            return authorityDenied;

        var outcome = await ReadOutcomeAsync(query.ShopId, type, key, ct);
        if (await CheckRecoveryAuthorityAsync(query, type, ct) is { } readAuthorityDenied)
            return readAuthorityDenied;
        if (lease.Value.CurrentDecision().EnforcedDenial() is { } readDenial) return readDenial;
        return outcome;
    }

    private async Task<Error?> CheckRecoveryAuthorityAsync(GetFinancialOperationQuery query,
        FinancialOperationType type, CancellationToken ct)
    {
        if (type is not (FinancialOperationType.ReceiptCostCorrection or FinancialOperationType.BatchDisposition))
            return null;
        if (query.Session is not { } session || session.UserId != query.UserId ||
            session.ActiveShopId != query.ShopId || !await sessions.IsValidAsync(session, ct))
            return Errors.Auth.SessionInvalidated;
        if (type == FinancialOperationType.BatchDisposition && session.ActiveShopRole != nameof(ShopRole.Owner))
            return Error.Forbidden("Expiry.OwnerRequired", "Current owner required.");
        return session.ActiveShopRole is nameof(ShopRole.Owner) or nameof(ShopRole.Manager)
            ? null : Errors.Operation.UserIsNotOwnerOrManager;
    }

    private async Task<ErrorOr<FinancialOperationDto>> ReadOutcomeAsync(Guid shop, FinancialOperationType type,
        string key, CancellationToken cancellationToken)
    {
        var receipt = await receiptRepository.GetCompletedAsync(shop, type, key, cancellationToken);
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
