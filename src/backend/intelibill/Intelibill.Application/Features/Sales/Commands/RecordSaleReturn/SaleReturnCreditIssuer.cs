using ErrorOr;
using Intelibill.Application.Features.CreditNotes.Services;
using Intelibill.Application.Features.Sales.Services.Returns;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Sales.Commands.RecordSaleReturn;

/// <summary>Issues a recorded return's customer credit (the ledger return credit and the credit note) and stages it for the save.</summary>
public sealed class SaleReturnCreditIssuer(
    ICreditNoteCodeGenerator creditNoteCodeGenerator,
    ICreditNoteRepository creditNoteRepository,
    ICustomerLedgerEntryRepository customerLedgerEntryRepository)
{
    public static ErrorOr<CustomerLedgerEntry> CreateReturnCredit(
        RecordSaleReturnCommand command,
        SaleReturnValidationResult validated,
        BusinessDay businessDay,
        string returnNumber)
    {
        return CustomerLedgerEntry.Create(
            command.ShopId,
            validated.Sale.CustomerId!.Value,
            validated.Sale.Id,
            CustomerLedgerEntryType.ReturnCredit,
            validated.Calculation.DueReductionAmount,
            businessDay.Date,
            $"Return credit from {returnNumber} for sale {validated.Sale.InvoiceNumber}",
            command.ActorUserId);
    }

    public async Task<ErrorOr<CreditNote>> IssueCreditNoteAsync(
        RecordSaleReturnCommand command,
        SaleReturnValidationResult validated,
        SaleReturn saleReturn,
        BusinessDay businessDay,
        string returnNumber,
        CancellationToken cancellationToken)
    {
        var code = await creditNoteCodeGenerator.GenerateAsync(command.ShopId, businessDay.Date, cancellationToken);
        var reason = ResolveCreditNoteReason(command, returnNumber);
        return CreditNote.Issue(
            command.ShopId,
            saleReturn.Id,
            validated.Calculation.PayoutAmount,
            reason,
            code,
            command.CreditNoteExpiresAt,
            validated.Sale.CustomerId);
    }

    /// <summary>Adds the return credit, then the credit note, to the unit of work when each was issued.</summary>
    public async Task AddCreditsAsync(
        CustomerLedgerEntry? returnCredit,
        CreditNote? creditNote,
        CancellationToken cancellationToken)
    {
        if (returnCredit is not null)
            await customerLedgerEntryRepository.AddAsync(returnCredit, cancellationToken);
        if (creditNote is not null)
            await creditNoteRepository.AddAsync(creditNote, cancellationToken);
    }

    private static string ResolveCreditNoteReason(RecordSaleReturnCommand command, string returnNumber)
    {
        if (!string.IsNullOrWhiteSpace(command.CreditNoteReason))
        {
            return command.CreditNoteReason.Trim();
        }

        if (!string.IsNullOrWhiteSpace(command.Notes))
        {
            return command.Notes.Trim();
        }

        return $"Return credit from {returnNumber}";
    }
}
