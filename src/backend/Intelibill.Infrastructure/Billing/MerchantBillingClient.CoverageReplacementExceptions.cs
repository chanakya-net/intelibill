using System.Text.Json;
using ErrorOr;
using Intelibill.Application.Features.Billing;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Infrastructure.Billing;

internal sealed partial class MerchantBillingClient : IMerchantCoverageReplacementExceptions
{
    public Task<ErrorOr<CoverageReplacementExceptionPreviewV1>> CoverageExceptionsAsync(MerchantBillingCaller caller, Guid account,
        Guid term, int page, CancellationToken ct) =>
        ReadAffiliationAsync<CoverageReplacementExceptionPreviewV1>(caller,
            new(CoverageReplacementExceptionsV1.Read, HttpMethod.Get,
                CoverageReplacementExceptionsV1.ReadPath(account, term, page), account, []),
            row => row.TermId == term && row.CoveredShops is { Length: <= CoverageReplacementExceptionsV1.PageSize }
                && row.EligibleShops is { Length: <= CoverageReplacementExceptionsV1.PageSize }, ct);

    public Task<ErrorOr<CoverageReplacementExceptionV1>> RequestCoverageExceptionAsync(MerchantBillingCaller caller, Guid account,
        Guid term, RequestCoverageReplacementExceptionV1 command, CancellationToken ct) =>
        ReadAffiliationAsync<CoverageReplacementExceptionV1>(caller,
            new(CoverageReplacementExceptionsV1.Request, HttpMethod.Post,
                CoverageReplacementExceptionsV1.Path(account, term), account,
                JsonSerializer.SerializeToUtf8Bytes(command, Json)),
            row => row.AccountId == account && row.TermId == term
                && row.OutgoingShopId == command.OutgoingShopId && row.IncomingShopId == command.IncomingShopId, ct);

    public Task<ErrorOr<CoverageReplacementExceptionV1>> RecoverCoverageExceptionAsync(MerchantBillingCaller caller, Guid account,
        Guid term, Guid key, CancellationToken ct) =>
        ReadAffiliationAsync<CoverageReplacementExceptionV1>(caller,
            new(CoverageReplacementExceptionsV1.Recover, HttpMethod.Get,
                CoverageReplacementExceptionsV1.RecoverPath(account, term, key), account, []),
            row => row.AccountId == account && row.TermId == term, ct);

    public async Task<ErrorOr<CoverageReplacementExceptionOwnerV1>> CoverageExceptionConsentAsync(MerchantBillingCaller caller,
        Guid account, Guid shop, Guid exceptionId, ShopAffiliationConsentAction action, Guid key, CancellationToken ct)
    {
        if (action != ShopAffiliationConsentAction.Read && key == Guid.Empty)
            return MerchantBillingErrors.OperationKeyInvalid;
        try
        {
            if (!await consents.IsOwnerAsync(caller, shop, ct))
                return ShopAffiliationConsentErrors.OwnerRequired;
            var detail = await ReadAffiliationAsync<CoverageReplacementExceptionV1>(caller,
                new(CoverageReplacementExceptionsV1.OwnerRead, HttpMethod.Get,
                    CoverageReplacementExceptionsV1.OwnerPath(account, shop, exceptionId), null, []),
                r => r.AccountId == account && r.ExceptionId == exceptionId
                    && (r.OutgoingShopId == shop || r.IncomingShopId == shop), ct);
            return detail.IsError
                ? detail.Errors
                : await exceptionConsents.RunAsync(caller, detail.Value, shop, action, key, ct);
        }
        catch (Exception error) when (error is Npgsql.NpgsqlException or TimeoutException)
        {
            ct.ThrowIfCancellationRequested();
            return Unavailable("CoverageReplacementExceptionStorageUnavailable", error);
        }
    }
}
