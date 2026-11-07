using FluentValidation;
using Intelibill.Application.Features.SupplierReturns.DTOs;
using Intelibill.Domain.Entities;

namespace Intelibill.Application.Features.SupplierReturns.Services;

public sealed class SupplierReturnRequestValidator : AbstractValidator<SupplierReturnRequest>
{
    public SupplierReturnRequestValidator()
    {
        RuleFor(x=>x.OperationKey).NotEmpty().MaximumLength(128);
        RuleFor(x=>x.SupplierId).NotEmpty();
        RuleFor(x=>x.Reason).NotEmpty().Must(x=>x?.Trim().Length is >=3 and <=255);
        RuleFor(x=>x.SupplierReference).MaximumLength(255);
        RuleFor(x=>x.Lines).NotNull().Must(lines=>lines is {Count:>=1 and <=100} && lines.Select(l=>l.ReceiptValueId).Distinct().Count()==lines.Count);
        RuleForEach(x=>x.Lines).ChildRules(line=>
        {
            line.RuleFor(x=>x.ReceiptValueId).NotEmpty();
            line.RuleFor(x=>x.ExpectedReceiptVersion).GreaterThan(0u);
            line.RuleFor(x=>x.ExpectedBatchVersion).GreaterThan(0u);
            line.RuleFor(x=>x.Quantity).GreaterThan(0m);
            line.RuleFor(x=>x.ProposedCreditAmount).InclusiveBetween(0m,PurchaseReceiptValue.MaxTotalCost).Must(amount=>decimal.Round(amount,2)==amount);
        });
    }
}
public sealed class SupplierReturnCreditRequestValidator : AbstractValidator<SupplierReturnCreditRequest>
{
    public SupplierReturnCreditRequestValidator()
    {
        RuleFor(x=>x.OperationKey).NotEmpty().MaximumLength(128);
        RuleFor(x=>x.ExpectedVersion).GreaterThan(0u);
        RuleFor(x=>x.Reason).NotEmpty().Must(x=>x?.Trim().Length is >=3 and <=255);
        RuleFor(x=>x.SupplierReference).MaximumLength(255);
        RuleFor(x=>x.Lines).NotNull().Must(lines=>lines is {Count:>=1 and <=100} && lines.Select(l=>l.ReturnLineId).Distinct().Count()==lines.Count);
        RuleForEach(x=>x.Lines).ChildRules(line=>
        {
            line.RuleFor(x=>x.ReturnLineId).NotEmpty();
            line.RuleFor(x=>x.AcceptedAmount).InclusiveBetween(0m,PurchaseReceiptValue.MaxTotalCost).Must(amount=>decimal.Round(amount,2)==amount);
        });
    }
}
