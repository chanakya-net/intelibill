using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Azure;
using Azure.Identity;
using Intelibill.Licensing.Application.Coverage;
using Intelibill.Licensing.Infrastructure.Entitlements;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Web.Delivery;

namespace Intelibill.Licensing.Infrastructure.Billing;

/// <summary>
/// Asks the merchant service, in one signed request and signed reply, whether the named shop's accepted owner
/// consent is still held by the live owner. No SQL transaction is open across this call. Any transport fault,
/// unsigned or mismatched reply is "unavailable": the caller fails closed.
/// </summary>
internal sealed class MerchantCoverageTransferAuthorityClient(LicensingEntitlementTransport transport, IHttpClientFactory clients)
    : IMerchantCoverageTransferAuthority
{
    private const int BodyLimit = 4096;

    public async Task<CoverageTransferShopAuthorityV1?> CheckAsync(CoverageTransferShopAuthorityCheckV1 check, CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(4));
        try
        {
            return await ExchangeAsync(check, budget.Token);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException or
            OperationCanceledException or RequestFailedException or AuthenticationFailedException or
            System.Security.Cryptography.CryptographicException)
        {
            ct.ThrowIfCancellationRequested();
            return null;
        }
    }

    private async Task<CoverageTransferShopAuthorityV1?> ExchangeAsync(CoverageTransferShopAuthorityCheckV1 check, CancellationToken ct)
    {
        const string path = CoverageTransfersV1.ShopAuthorityPath;
        var body = JsonSerializer.SerializeToUtf8Bytes(check, EntitlementSnapshotsV1.Json);
        if (transport.Origin is not { } origin || await transport.SignAsync(body, ct, path) is not { } signature)
            return null;

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(origin, path)) { Content = new ByteArrayContent(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", signature);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var client = clients.CreateClient(LeasedHttpDeliveryPump.ClientName);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentType?.MediaType != "application/json" ||
            !response.Headers.TryGetValues(CoverageTransfersV1.ProofHeader, out var signatures) ||
            signatures.ToArray() is not [var proof])
            return null;

        await response.Content.LoadIntoBufferAsync(BodyLimit, ct);
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        return transport.Verify(path, proof, bytes) is not null &&
            JsonSerializer.Deserialize<CoverageTransferShopAuthorityV1>(bytes, EntitlementSnapshotsV1.Json) is { } answer &&
            answer.IsValid() && answer.Challenge == check.Challenge &&
            answer.TransferId == check.TransferId && answer.ShopId == check.ShopId &&
            answer.ConsentEventId == check.ExpectedConsentEventId
            ? answer : null;
    }
}
