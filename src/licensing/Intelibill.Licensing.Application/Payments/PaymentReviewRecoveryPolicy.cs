using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Payments;

public sealed record ReviewSourceShop(Guid ShopId, string Name, Guid AffiliationRequestId);

public sealed record ReviewCancellationWithdrawal(int Revision, DateTimeOffset RecordedAt);

public sealed record ReviewFundingTime(DateTimeOffset EarliestCaptureAt, DateTimeOffset LatestPossibleCaptureAt);

internal sealed record ReviewRecoverySource(Guid CaseId, Guid? ReceiptId, CheckoutSource Source,
    DateTimeOffset LatestPossibleCaptureAt);

internal static class PaymentReviewRecoveryPolicy
{
    internal const string Version = "accepted-source-equivalence/1";

    internal static CheckoutSource? Equivalent(CheckoutQuoteV1 quote, CheckoutSource? live, ReviewSourceShop[] frozen,
        ReviewCancellationWithdrawal[] withdrawals)
    {
        if (live is null || live.Prior is { CancellationRevision: var revision } && revision % 2 != 0 ||
            live.Shops.Length != frozen.Length ||
            live.Shops.Where((shop, index) => shop.ShopId != frozen[index].ShopId ||
                shop.RequestId != frozen[index].AffiliationRequestId).Any())
            return null;
        if (live.Matches(quote))
            return live;

        // Rebuild the accepted digest using the sealed names and only actual cancellation history.
        var original = live with { Shops = live.Shops.Select((shop, index) => shop with { Name = frozen[index].Name }).ToArray() };
        if (original.Matches(quote))
            return live;
        if (original.Prior is not { } prior || !withdrawals.Any(w => w.Revision == prior.CancellationRevision))
            return null;
        return withdrawals.Where(w => w.RecordedAt <= quote.CreatedAt).Select(w => w.Revision).Prepend(0).Any(candidate => candidate < prior.CancellationRevision &&
            (original with { Prior = prior with { CancellationRevision = candidate } }).Matches(quote)) ? live : null;
    }
}
