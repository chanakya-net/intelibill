using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Azure;
using Azure.Identity;
using ErrorOr;
using Intelibill.Licensing.Application.Changes;
using Intelibill.Licensing.Application.Subscriptions;
using Intelibill.Licensing.Infrastructure.Entitlements;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Web.Delivery;

namespace Intelibill.Licensing.Infrastructure.Subscriptions;

/// <summary>
/// Requests bounded, signed live active-member observations from the merchant service for one term's shops.
/// Each page stays well inside the 8,192-byte signed transport budget and carries a fresh per-call challenge.
/// </summary>
internal sealed class MerchantReductionObserver(
    LicensingEntitlementTransport transport, IHttpClientFactory clients, TimeProvider clock) : IMerchantReductionObserver
{
    public async Task<ErrorOr<IReadOnlyDictionary<Guid, ReductionObservedShopV1>>> ObserveAsync(
        Guid account, Guid term, CapacityChangeSourceShop[] shops, CancellationToken ct)
    {
        if (shops.Length == 0) return CapacityReductionErrors.SourceChanged;
        var ordered = shops.OrderBy(s => s.ShopId).ToArray();
        var pages = (ordered.Length + ReductionTransportV1.ObservePageSize - 1) / ReductionTransportV1.ObservePageSize;
        var challenge = Guid.NewGuid();
        var result = new Dictionary<Guid, ReductionObservedShopV1>(ordered.Length);

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            for (var index = 0; index < pages; index++)
            {
                var slice = ordered.Skip(index * ReductionTransportV1.ObservePageSize)
                    .Take(ReductionTransportV1.ObservePageSize)
                    .Select(s => new ReductionExpectedShopV1(s.ShopId, s.RequestId)).ToArray();
                var expires = EntitlementSnapshotsV1.Instant(clock).AddSeconds(30);
                var request = new ReductionObservationRequestV1(1, account, term, challenge, index, pages, expires, slice);
                var page = await ExchangeAsync(request, budget.Token);
                if (page.IsError) return page.Errors;
                foreach (var item in page.Value.Shops)
                    result[item.ShopId] = item;
            }
            return result.Count == ordered.Length ? result : CapacityReductionErrors.Unavailable;
        }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException or
            OperationCanceledException or RequestFailedException or AuthenticationFailedException or
            System.Security.Cryptography.CryptographicException)
        {
            ct.ThrowIfCancellationRequested();
            return CapacityReductionErrors.Unavailable;
        }
    }

    private async Task<ErrorOr<ReductionObservationResponseV1>> ExchangeAsync(
        ReductionObservationRequestV1 payload, CancellationToken ct)
    {
        var path = ReductionTransportV1.ObservePath;
        var body = JsonSerializer.SerializeToUtf8Bytes(payload, EntitlementSnapshotsV1.Json);
        if (body.Length > ReductionTransportV1.BodyLimit ||
            transport.Origin is not { } origin || await transport.SignAsync(body, ct, path) is not { } signature)
            return CapacityReductionErrors.Unavailable;

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(origin, path)) { Content = new ByteArrayContent(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", signature);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var client = clients.CreateClient(LeasedHttpDeliveryPump.ClientName);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode != HttpStatusCode.OK ||
            response.Content.Headers.ContentType?.MediaType != "application/json" ||
            !response.Headers.TryGetValues(ReductionTransportV1.ProofHeader, out var signatures) ||
            signatures.ToArray() is not [var proof])
            return CapacityReductionErrors.Unavailable;

        await response.Content.LoadIntoBufferAsync(ReductionTransportV1.BodyLimit, ct);
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        if (transport.Verify(path, proof, bytes) is null ||
            JsonSerializer.Deserialize<ReductionObservationResponseV1>(bytes, EntitlementSnapshotsV1.Json) is not { } saved ||
            !Matches(payload, saved))
            return CapacityReductionErrors.Unavailable;
        return saved;
    }

    private static bool Matches(ReductionObservationRequestV1 req, ReductionObservationResponseV1 res) =>
        res.SchemaVersion == 1 && res.AccountId == req.AccountId && res.TermId == req.TermId &&
        res.Challenge == req.Challenge && res.PageIndex == req.PageIndex && res.PageCount == req.PageCount &&
        res.Shops is { Length: > 0 } && res.Shops.Length == req.Shops.Length &&
        res.Shops.Select(s => s.ShopId).SequenceEqual(req.Shops.Select(s => s.ShopId)) &&
        res.Shops.All(s => s.ActiveMembers >= 0 && EntitlementSnapshotsV1.IsDigest(s.SeatHash));
}
