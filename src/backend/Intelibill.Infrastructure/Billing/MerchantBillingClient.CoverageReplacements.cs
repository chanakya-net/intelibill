using ErrorOr;
using System.Text.Json;
using Intelibill.Application.Features.Billing;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Infrastructure.Billing;

internal sealed partial class MerchantBillingClient : IMerchantCoverageReplacements
{
    public Task<ErrorOr<CoverageReplacementPreviewV1>> ReplacementsAsync(MerchantBillingCaller caller, Guid account,
        Guid term, int page, CancellationToken ct) => ReadAffiliationAsync<CoverageReplacementPreviewV1>(caller,
            new(CoverageReplacementsV1.Read, HttpMethod.Get, CoverageReplacementsV1.ReadPath(account, term, page), account, []),
            row => row.TermId == term && row.CoveredShops is { Length: <= CoverageReplacementsV1.PageSize }
                && row.EligibleShops is { Length: <= CoverageReplacementsV1.PageSize }, ct);

    public Task<ErrorOr<CoverageReplacementV1>> ScheduleReplacementAsync(MerchantBillingCaller caller, Guid account, Guid term,
        ScheduleCoverageReplacementV1 command, CancellationToken ct) => ReadAffiliationAsync<CoverageReplacementV1>(caller,
            new(CoverageReplacementsV1.Schedule, HttpMethod.Post, CoverageReplacementsV1.Path(account, term), account,
                JsonSerializer.SerializeToUtf8Bytes(command, Json)), row => row.AccountId == account && row.TermId == term &&
                row.OutgoingShopId == command.OutgoingShopId && row.IncomingShopId == command.IncomingShopId, ct);

    public Task<ErrorOr<CoverageReplacementV1>> RecoverReplacementAsync(MerchantBillingCaller caller, Guid account, Guid term,
        Guid key, CancellationToken ct) => ReadAffiliationAsync<CoverageReplacementV1>(caller, new(CoverageReplacementsV1.Recover,
            HttpMethod.Get, $"{CoverageReplacementsV1.Path(account, term)}/operations/{key:D}", account, []),
            row => row.AccountId == account && row.TermId == term, ct);

    public async Task<ErrorOr<ReplacementOwnerV1>> ReplacementConsentAsync(MerchantBillingCaller caller, Guid account, Guid shop,
        Guid replacement, ShopAffiliationConsentAction action, Guid key, CancellationToken ct)
    {
        if (action != ShopAffiliationConsentAction.Read && key == Guid.Empty) return MerchantBillingErrors.OperationKeyInvalid;
        try
        {
            if (!await consents.IsOwnerAsync(caller, shop, ct)) return ShopAffiliationConsentErrors.OwnerRequired;
            var detail = await ReadAffiliationAsync<CoverageReplacementV1>(caller, new(CoverageReplacementsV1.OwnerRead, HttpMethod.Get,
                CoverageReplacementsV1.OwnerPath(account, shop, replacement), null, []), r => r.AccountId == account && r.IncomingShopId == shop &&
                    r.ReplacementId == replacement, ct);
            return detail.IsError ? detail.Errors : await replacementConsents.RunAsync(caller, detail.Value, action, key, ct);
        }
        catch (Exception error) when (error is Npgsql.NpgsqlException or TimeoutException)
        {
            ct.ThrowIfCancellationRequested();
            return Unavailable("CoverageReplacementStorageUnavailable", error);
        }
    }
}
