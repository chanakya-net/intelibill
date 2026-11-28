using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Sales.Commands.RecordSale;

/// <summary>Decides whether a sale request replays the committed sale for its idempotency key, refuses it, or proceeds.</summary>
public sealed class SaleReplayReader(
    ISaleRepository saleRepository,
    ILiveSessionAuthority sessionAuthority,
    ISuspensionAccessReader suspensionAccess,
    ICreditNoteRepository creditNoteRepository,
    SaleDtoBuilder saleDtoBuilder)
{
    /// <summary>Runs right after the idempotency key lock, before any other lock or read.</summary>
    internal async Task<ErrorOr<SaleDto>?> RecheckSessionThenReplayAsync(
        RecordSaleCommand command,
        string normalizedIdempotencyKey,
        string requestHash,
        CancellationToken cancellationToken)
    {
        // The prior attempt may have held this key while this device lost its authority.
        if (!await sessionAuthority.IsValidAsync(command.Session, cancellationToken))
            return Errors.Auth.SessionInvalidated;

        return await TryGetReplayForIdempotentSaleAsync(
            command, normalizedIdempotencyKey, requestHash, cancellationToken);
    }

    /// <summary>A refused redemption may mean the same sale committed meanwhile; that sale is replayed instead.</summary>
    internal async Task<ErrorOr<SaleDto>> ReplayOrRefuseAsync(
        RecordSaleCommand command,
        string normalizedIdempotencyKey,
        string requestHash,
        List<Error> refusal,
        CancellationToken cancellationToken)
    {
        var replayResult = await TryGetReplayForIdempotentSaleAsync(
            command,
            normalizedIdempotencyKey,
            requestHash,
            cancellationToken);

        if (replayResult.HasValue)
            return replayResult.Value;

        return refusal;
    }

    internal async Task<ErrorOr<SaleDto>?> TryGetReplayForIdempotentSaleAsync(
        RecordSaleCommand command,
        string idempotencyKey,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var concurrentSale = await saleRepository.GetByIdempotencyKeyAsync(command.ShopId, command.ActorUserId, idempotencyKey, cancellationToken);
        if (concurrentSale is null)
            return null;

        if (!await sessionAuthority.IsValidAsync(command.Session, cancellationToken))
            return Errors.Auth.SessionInvalidated;

        if (await suspensionAccess.ReadActiveShopDenialAsync(command.ShopId, LicenceAction.Recovery, cancellationToken) is { } denied)
            return denied;

        if (!string.Equals(concurrentSale.RequestHash, requestHash, StringComparison.Ordinal))
            return Errors.Sale.IdempotencyConflict;

        var concurrentRedemptions = await LoadCreditNoteRedemptionsAsync(concurrentSale.ShopId, concurrentSale.Id, cancellationToken);
        if (concurrentRedemptions.IsError)
            return concurrentRedemptions.Errors;

        return await saleDtoBuilder.BuildSaleDtoAsync(concurrentSale, concurrentSale.Warnings, concurrentRedemptions.Value, cancellationToken);
    }

    private async Task<ErrorOr<IReadOnlyList<SaleCreditNoteRedemptionSummaryDto>>> LoadCreditNoteRedemptionsAsync(
        Guid shopId,
        Guid saleId,
        CancellationToken cancellationToken)
    {
        var redemptions = await creditNoteRepository.GetRedemptionsBySaleIdAsync(shopId, saleId, cancellationToken) ?? [];

        return redemptions.Select(redemption =>
            new SaleCreditNoteRedemptionSummaryDto(
                redemption.CreditNoteId,
                redemption.Code,
                redemption.Amount)).ToList();
    }
}
