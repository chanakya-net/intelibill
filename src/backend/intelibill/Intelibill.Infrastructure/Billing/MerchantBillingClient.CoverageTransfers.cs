using System.Net;
using System.Text.Json;
using ErrorOr;
using Intelibill.Application.Features.Billing;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Infrastructure.Billing;

internal sealed partial class MerchantBillingClient : IMerchantCoverageTransfers
{
    public Task<ErrorOr<CoverageTransferPreviewV1>> PreviewCoverageTransferAsync(MerchantBillingCaller caller, Guid account,
        Guid term, PreviewCoverageTransferV1 query, CancellationToken ct) =>
        TransferAsync<CoverageTransferPreviewV1>(caller, new(CoverageTransfersV1.Preview, HttpMethod.Post,
                CoverageTransfersV1.PreviewPath(account, term), account, JsonSerializer.SerializeToUtf8Bytes(query, Json)),
            row => row.SourceAccountId == account && row.TermId == term && row.ShopId == query.ShopId
                && row.DestinationAccountId == query.DestinationAccountId, ct);

    public Task<ErrorOr<CoverageTransferV1>> RequestCoverageTransferAsync(MerchantBillingCaller caller, Guid account,
        Guid term, RequestCoverageTransferV1 command, CancellationToken ct) =>
        TransferAsync<CoverageTransferV1>(caller, new(CoverageTransfersV1.Request, HttpMethod.Post,
                CoverageTransfersV1.RequestPath(account, term), account, JsonSerializer.SerializeToUtf8Bytes(command, Json)),
            row => row.SourceAccountId == account && row.ShopId == command.ShopId
                && row.DestinationAccountId == command.DestinationAccountId, ct);

    public Task<ErrorOr<CoverageTransferV1>> RecoverCoverageTransferAsync(MerchantBillingCaller caller, Guid account,
        Guid term, Guid key, CancellationToken ct) =>
        TransferAsync<CoverageTransferV1>(caller, new(CoverageTransfersV1.Recover, HttpMethod.Get,
            CoverageTransfersV1.RecoverPath(account, term, key), account, []), row => row.SourceAccountId == account, ct);

    public Task<ErrorOr<CoverageTransferListV1>> CoverageTransfersAsync(MerchantBillingCaller caller, Guid account,
        CancellationToken ct) =>
        TransferAsync<CoverageTransferListV1>(caller, new(CoverageTransfersV1.List, HttpMethod.Get,
                CoverageTransfersV1.ListPath(account), account, []),
            list => list.AccountId == account && list.Items is { Length: <= CoverageTransfersV1.PageSize } items
                && items.All(row => IsParty(row, account)), ct);

    public Task<ErrorOr<CoverageTransferV1>> CoverageTransferAsync(MerchantBillingCaller caller, Guid account, Guid transfer,
        CancellationToken ct) =>
        TransferAsync<CoverageTransferV1>(caller, new(CoverageTransfersV1.Read, HttpMethod.Get,
            CoverageTransfersV1.ReadPath(account, transfer), account, []),
            row => row.TransferId == transfer && IsParty(row, account), ct);

    public Task<ErrorOr<CoverageTransferV1>> DecideCoverageTransferAsync(MerchantBillingCaller caller, Guid account,
        Guid transfer, string purpose, string kind, CoverageTransferDecisionV1 decision, CancellationToken ct) =>
        TransferAsync<CoverageTransferV1>(caller, new(CoverageTransfersV1.Consent, HttpMethod.Post,
                CoverageTransfersV1.ConsentPath(account, transfer, purpose, kind), account,
                JsonSerializer.SerializeToUtf8Bytes(decision, Json)),
            row => row.TransferId == transfer && IsParty(row, account), ct);

    public Task<ErrorOr<CoverageTransferV1>> RecoverCoverageTransferDecisionAsync(MerchantBillingCaller caller, Guid account,
        Guid transfer, string purpose, Guid key, CancellationToken ct) =>
        TransferAsync<CoverageTransferV1>(caller, new(CoverageTransfersV1.ConsentRecover, HttpMethod.Get,
            CoverageTransfersV1.ConsentRecoverPath(account, transfer, purpose, key), account, []),
            row => row.TransferId == transfer && IsParty(row, account), ct);

    public async Task<ErrorOr<CoverageTransferShopOwnerV1>> CoverageTransferShopConsentAsync(MerchantBillingCaller caller,
        Guid account, Guid shop, Guid transfer, CoverageTransferShopAction action, CoverageTransferDecisionV1? decision,
        CancellationToken ct)
    {
        if (action != CoverageTransferShopAction.Read && decision?.IsValid() != true)
            return MerchantBillingErrors.OperationKeyInvalid;
        try
        {
            // The narrow owner preview is released only to the live owner of the active, named shop.
            if (!await consents.IsOwnerAsync(caller, shop, ct))
                return ShopAffiliationConsentErrors.OwnerRequired;
            var terms = await TransferAsync<CoverageTransferOwnerTermsV1>(caller, new(CoverageTransfersV1.OwnerRead,
                    HttpMethod.Get, CoverageTransfersV1.OwnerPath(account, shop, transfer), null, []),
                row => row.Transfer is { } t && t.TransferId == transfer && t.SourceAccountId == account && t.ShopId == shop
                    && row.SourceAffiliationId != Guid.Empty, ct);
            return terms.IsError ? terms.Errors : await transferConsents.RunAsync(caller, terms.Value, action, decision, ct);
        }
        catch (Exception error) when (error is Npgsql.NpgsqlException or TimeoutException)
        {
            ct.ThrowIfCancellationRequested();
            return Unavailable("CoverageTransferStorageUnavailable", error);
        }
    }

    private static bool IsParty(CoverageTransferV1 row, Guid account) =>
        row.SourceAccountId == account || row.DestinationAccountId == account;

    private Task<ErrorOr<T>> TransferAsync<T>(MerchantBillingCaller caller, BillingDelegationIntent intent,
        Func<T, bool> valid, CancellationToken ct) where T : class =>
        RunAsync<T>(caller, intent, CoverageTransfersV1.ResponseLimit, async (response, token) =>
        {
            var result = await ReadJsonAsync<T>(response, token);
            return result is not null && valid(result) ? result : Unavailable("InvalidCoverageTransferContract");
        }, ct);

    private static Error MapTransferRequestFailure(HttpStatusCode status, string? code) => code switch
    {
        "Coverage.TransferInvalid" when status == HttpStatusCode.BadRequest => Error.Validation(code),
        "Coverage.TransferNotFound" when status == HttpStatusCode.NotFound => Error.NotFound(code),
        "Coverage.TransferChanged" or "Coverage.TransferConflict" or "Coverage.TransferConsentInvalid"
            or "Coverage.TransferAuthorityChanged" when status == HttpStatusCode.Conflict => Error.Conflict(code),
        "Billing.OperationPayloadMismatch" when status == HttpStatusCode.Conflict => MerchantBillingErrors.PayloadMismatch,
        _ => MerchantBillingErrors.Unavailable,
    };
}
