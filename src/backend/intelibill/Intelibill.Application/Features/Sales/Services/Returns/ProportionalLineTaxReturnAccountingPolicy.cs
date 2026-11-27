using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Sales.Services.Returns;

/// <summary>
/// <c>proportional-line-tax/1</c>: the part of a refund within the paid share reverses tax in the
/// share's own proportion (₹50 of a ₹100 + 18% share → ₹42.37 + ₹7.63); anything approved above the
/// paid share is a commercial-only adjustment that reverses no tax. A refund against a share with
/// no paid value, or a share whose tax is outside its gross, has no proportion to apply and is
/// refused rather than guessed.
/// </summary>
internal sealed class ProportionalLineTaxReturnAccountingPolicy : IReturnAccountingPolicy
{
    public const string PolicyVersion = "proportional-line-tax/1";

    public string Version => PolicyVersion;

    public ErrorOr<IReadOnlyList<ReturnLineAccounting>> Classify(IReadOnlyList<ReturnAccountingLineRequest> lines)
    {
        var classified = new List<ReturnLineAccounting>(lines.Count);
        foreach (var line in lines)
        {
            if (line.ApprovedRefundAmount == 0m)
            {
                classified.Add(new ReturnLineAccounting(line.SaleItemId, 0m, 0m, 0m));
                continue;
            }

            if (line.ApprovedRefundAmount < 0m
                || line.ShareGrossAmount <= 0m
                || line.ShareTaxAmount < 0m
                || line.ShareTaxAmount > line.ShareGrossAmount)
            {
                return Errors.Sale.ReturnAccountingPolicyUnsupported(line.SaleItemId);
            }

            var withinShare = Math.Min(line.ApprovedRefundAmount, line.ShareGrossAmount);
            var tax = RoundMoney(withinShare * line.ShareTaxAmount / line.ShareGrossAmount);
            classified.Add(new ReturnLineAccounting(
                line.SaleItemId,
                RoundMoney(withinShare - tax),
                tax,
                RoundMoney(line.ApprovedRefundAmount - withinShare)));
        }

        return classified;
    }

    private static decimal RoundMoney(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
