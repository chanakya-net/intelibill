using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Sales.Commands.RecordSale;

/// <summary>Locks each credit note a sale tenders and posts its redemption against the sale.</summary>
public sealed class SaleCreditNoteRedeemer(ICreditNoteRepository creditNoteRepository)
{
    internal async Task<ErrorOr<IReadOnlyList<SaleCreditNoteRedemptionSummaryDto>>> LockAndRedeemCreditNotesAsync(
        Guid shopId,
        Guid saleId,
        IReadOnlyList<CreditNoteRedemptionCommand> creditNoteRedemptions,
        Guid? saleCustomerId,
        bool customerMismatchConfirmed,
        CancellationToken cancellationToken)
    {
        if (creditNoteRedemptions.Count == 0)
        {
            return Array.Empty<SaleCreditNoteRedemptionSummaryDto>();
        }

        var summaries = new List<SaleCreditNoteRedemptionSummaryDto>(creditNoteRedemptions.Count);
        foreach (var redemption in creditNoteRedemptions)
        {
            var creditNote = await creditNoteRepository.GetByCodeForUpdateWithRedemptionsAsync(
                shopId,
                redemption.Code,
                cancellationToken);
            if (creditNote is null)
            {
                return Errors.CreditNote.CreditNoteNotFound(redemption.Code);
            }

            if (creditNote.LinkedCustomerId.HasValue && creditNote.LinkedCustomerId.Value != saleCustomerId && !customerMismatchConfirmed)
            {
                return Errors.CreditNote.CustomerMismatchRequiresConfirmation;
            }

            var redeemResult = creditNote.Redeem(shopId, saleId, redemption.Amount);
            if (redeemResult.IsError)
            {
                return redeemResult.Errors;
            }

            await creditNoteRepository.AddRedemptionAsync(redeemResult.Value, cancellationToken);
            summaries.Add(new SaleCreditNoteRedemptionSummaryDto(creditNote.Id, creditNote.Code, redemption.Amount));
        }

        return summaries;
    }
}
