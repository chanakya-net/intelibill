using ErrorOr;
using Intelibill.Application.Features.Billing;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Infrastructure.Billing;

internal sealed partial class MerchantBillingClient : IMerchantPaymentConfirmations
{
    public Task<ErrorOr<PaymentConfirmationV1[]>> ListConfirmationsAsync(MerchantBillingCaller caller, Guid account, Guid order, CancellationToken ct) =>
        RunAsync<PaymentConfirmationV1[]>(caller, new(CheckoutPaymentsV1.Confirmations, HttpMethod.Get,
            CheckoutPaymentsV1.ConfirmationsPath(account, order), account, []), PaymentConfirmationsV1.MaxListBytes,
            async (response, token) =>
            {
                var rows = await ReadJsonAsync<PaymentConfirmationV1[]>(response, token);
                return rows is not null && rows.All(row => PaymentConfirmationsV1.IsValid(row, account, order)) &&
                    rows.Select(row => row.ConfirmationId).Distinct().Count() == rows.Length
                    ? rows : Unavailable("InvalidConfirmationList");
            }, ct);

    public Task<ErrorOr<MerchantBillingDocument>> DownloadConfirmationAsync(MerchantBillingCaller caller, Guid account, Guid order, Guid id, CancellationToken ct) =>
        RunAsync<MerchantBillingDocument>(caller, new(CheckoutPaymentsV1.ConfirmationDocument, HttpMethod.Get,
            CheckoutPaymentsV1.ConfirmationDocumentPath(account, order, id), account, []), PaymentConfirmationsV1.MaxDocumentBytes,
            async (response, token) => IsValidDocument(response, id)
                ? new MerchantBillingDocument(await response.Content.ReadAsByteArrayAsync(token), PaymentConfirmationsV1.FileName(id))
                : Unavailable("InvalidConfirmationDocument"), ct, "text/plain");

    internal static bool IsValidDocument(HttpResponseMessage response, Guid id) =>
        response.Content.Headers.ContentType?.MediaType == "text/plain" &&
        string.Equals(response.Content.Headers.ContentType.CharSet, "utf-8", StringComparison.OrdinalIgnoreCase) &&
        response.Content.Headers.ContentLength is > 0 and <= PaymentConfirmationsV1.MaxDocumentBytes &&
        response.Content.Headers.ContentDisposition is { DispositionType: "attachment" } disposition &&
        (disposition.FileNameStar ?? disposition.FileName?.Trim('"')) == PaymentConfirmationsV1.FileName(id) &&
        response.Headers.CacheControl?.NoStore == true &&
        response.Headers.TryGetValues("X-Content-Type-Options", out var values) && values.SequenceEqual(["nosniff"], StringComparer.Ordinal);
}
