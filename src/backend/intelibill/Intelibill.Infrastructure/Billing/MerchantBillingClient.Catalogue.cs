using ErrorOr;
using Intelibill.Application.Features.Billing;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Application.Common.Errors;

namespace Intelibill.Infrastructure.Billing;

internal sealed partial class MerchantBillingClient
{
    public Task<ErrorOr<PublishedCatalogueV1>> ListPlansAsync(MerchantBillingCaller caller, Guid? after,
        CancellationToken cancellationToken)
    {
        if (after == Guid.Empty)
            return Task.FromResult<ErrorOr<PublishedCatalogueV1>>(Errors.Billing.CatalogueCursorInvalid);

        return RunAsync<PublishedCatalogueV1>(caller,
            new BillingDelegationIntent(PublishedCatalogueV1.Read, HttpMethod.Get, PublishedCatalogueV1.ReadPath(after), null, []),
            PublishedCatalogueV1.MaxResponseBytes, async (response, token) =>
            {
                var page = await ReadJsonAsync<PublishedCatalogueV1>(response, token);
                return page?.Items is { Length: <= PublishedCatalogueV1.PageSize } && ValidCatalogue(page, after)
                    ? page : Unavailable("InvalidResponseContract");
            }, cancellationToken);
    }

    public Task<ErrorOr<PublishedPlanVersionV1>> ReadPlanVersionAsync(MerchantBillingCaller caller, Guid versionId,
        CancellationToken cancellationToken)
    {
        if (versionId == Guid.Empty)
            return Task.FromResult<ErrorOr<PublishedPlanVersionV1>>(MerchantBillingErrors.PlanVersionNotFound);
        return RunAsync<PublishedPlanVersionV1>(caller,
            new BillingDelegationIntent(PublishedCatalogueV1.Read, HttpMethod.Get, PublishedCatalogueV1.VersionPath(versionId), null, []),
            PublishedCatalogueV1.MaxVersionResponseBytes, async (response, token) =>
            {
                var version = await ReadJsonAsync<PublishedPlanVersionV1>(response, token);
                return version?.Terms?.VersionId == versionId && ValidOffer(version.Terms)
                    ? version : Unavailable("InvalidResponseContract");
            }, cancellationToken);
    }

    private static bool ValidCatalogue(PublishedCatalogueV1 page, Guid? after)
    {
        var last = after;
        foreach (var offer in page.Items)
        {
            if (!ValidOffer(offer) || (last is { } prior && offer.PlanId.CompareTo(prior) <= 0))
                return false;
            last = offer.PlanId;
        }
        return page.NextCursor is null || (page.Items.Length == PublishedCatalogueV1.PageSize && page.NextCursor == last);
    }

    private static bool ValidOffer(PublishedPlanTermsV1? offer) =>
        offer is { SchemaVersion: 1, Currency: "INR", PriceBasis: "TaxInclusiveFinal", BillingTimeZone: "Asia/Kolkata",
            TermCalendar: "CalendarMonthOrYear", EligiblePaidGraceDays: 7, PaymentConfirmationIsTaxInvoice: false,
            GraceExclusions: ["Trial", "Complimentary", "Temporary", "Transition", "ScheduledCancellation", "Suspension"],
            Name.Length: > 0 and <= 80, VersionNumber: > 0, MonthlyPricePaise: >= 0 and <= 1_000_000_000_000,
            AnnualPricePaise: >= 0 and <= 1_000_000_000_000, MembersPerShop: >= 1 and <= 100_000 }
        && offer.PlanId != Guid.Empty && offer.VersionId != Guid.Empty && !offer.Name.Any(char.IsControl)
        && Policy(offer.CustomerTermsVersion) && Policy(offer.RefundPolicyVersion) && ValidTrial(offer)
        && (offer.Kind switch
        {
            "single-shop" => offer.IncludedShops == 1 && !offer.ExtraShopsOffered && NoExtras(offer),
            "multi-shop" => offer.IncludedShops is >= 2 and <= 100_000 && (offer.ExtraShopsOffered
                ? offer.ExtraShopMonthlyPricePaise is >= 0 and <= 1_000_000_000_000
                  && offer.ExtraShopAnnualPricePaise is >= 0 and <= 1_000_000_000_000 : NoExtras(offer)),
            _ => false,
        });

    private static bool ValidTrial(PublishedPlanTermsV1 offer) =>
        offer.TrialPolicy is { DurationDays: 14, VersionNumber: > 0, ActiveMemberCap: >= 1 and <= 100_000,
            MaxConcurrentTrialShops: >= 1 and <= 100_000 } trial
        && trial.VersionId != Guid.Empty && Policy(trial.TrialTermsVersion) && trial.DevelopmentFixture == offer.DevelopmentFixture;

    private static bool NoExtras(PublishedPlanTermsV1 offer) =>
        offer.ExtraShopMonthlyPricePaise is null && offer.ExtraShopAnnualPricePaise is null;

    private static bool Policy(string? reference) => reference is { Length: > 0 and <= 40 }
        && char.IsAsciiLetterOrDigit(reference[0])
        && reference.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');
}
