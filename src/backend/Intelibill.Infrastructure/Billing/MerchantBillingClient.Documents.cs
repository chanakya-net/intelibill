using System.Security.Cryptography;
using ErrorOr;
using Intelibill.Application.Features.Billing;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Infrastructure.Billing;

internal sealed partial class MerchantBillingClient : IMerchantBillingDocuments
{
    public Task<ErrorOr<BillingDocumentListV1>> ListDocumentsAsync(MerchantBillingCaller caller, Guid account, Guid order,
        CancellationToken ct) =>
        RunAsync<BillingDocumentListV1>(caller, new(BillingDocumentsV1.List, HttpMethod.Get,
            BillingDocumentsV1.ListPath(account, order), account, []), BillingDocumentsV1.MaxListBytes,
            async (response, token) =>
            {
                var list = await ReadJsonAsync<BillingDocumentListV1>(response, token);
                return BillingDocumentsV1.IsValidList(list, account, order)
                    ? list! : Unavailable("InvalidBillingDocumentList");
            }, ct);

    public Task<ErrorOr<MerchantBillingDocument>> DownloadDocumentAsync(MerchantBillingCaller caller, Guid account, Guid order,
        Guid version, CancellationToken ct) =>
        RunAsync<MerchantBillingDocument>(caller, new(BillingDocumentsV1.Download, HttpMethod.Get,
            BillingDocumentsV1.ContentPath(account, order, version), account, []), BillingDocumentsV1.MaxDocumentBytes,
            async (response, token) =>
            {
                var bytes = await response.Content.ReadAsByteArrayAsync(token);
                return IsValidBillingDocument(response, version, bytes)
                    ? new MerchantBillingDocument(bytes, BillingDocumentsV1.FileName(version))
                    : Unavailable("InvalidBillingDocumentContent");
            }, ct, BillingDocumentsV1.MediaType);

    internal static bool IsValidBillingDocument(HttpResponseMessage response, Guid version, byte[] bytes) =>
        response.Content.Headers.ContentType?.MediaType == BillingDocumentsV1.MediaType &&
        response.Content.Headers.ContentLength is { } declared && declared == bytes.Length &&
        bytes.Length is > 0 and <= BillingDocumentsV1.MaxDocumentBytes &&
        bytes.AsSpan().StartsWith("%PDF-"u8) &&
        response.Content.Headers.ContentDisposition is { DispositionType: "attachment" } disposition &&
        (disposition.FileNameStar ?? disposition.FileName?.Trim('"')) == BillingDocumentsV1.FileName(version) &&
        response.Headers.CacheControl?.NoStore == true &&
        response.Headers.TryGetValues("X-Content-Type-Options", out var nosniff) &&
        nosniff.SequenceEqual(["nosniff"], StringComparer.Ordinal) &&
        response.Headers.TryGetValues(BillingDocumentsV1.DigestHeader, out var digests) &&
        digests.ToArray() is [var digest] &&
        digest == Convert.ToHexStringLower(SHA256.HashData(bytes));
}
