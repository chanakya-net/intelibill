using ErrorOr;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Sales.Services.Returns;

/// <summary>
/// One return line as the policy sees it: the paid invoice share that came back (its gross and the
/// output tax charged on it) and the refund approved for it.
/// </summary>
public sealed record ReturnAccountingLineRequest(
    Guid SaleItemId,
    decimal ShareGrossAmount,
    decimal ShareTaxAmount,
    decimal ApprovedRefundAmount);

/// <summary>
/// Classifies an approved refund into taxable value, output tax and a commercial-only remainder.
/// Which reductions qualify for a tax adjustment is a tax/document decision (plan 20): a policy
/// either classifies every line completely or refuses the return, and its version is stored with
/// the snapshot so a later policy never re-reads an earlier posting.
/// </summary>
public interface IReturnAccountingPolicy
{
    string Version { get; }

    ErrorOr<IReadOnlyList<ReturnLineAccounting>> Classify(IReadOnlyList<ReturnAccountingLineRequest> lines);
}
