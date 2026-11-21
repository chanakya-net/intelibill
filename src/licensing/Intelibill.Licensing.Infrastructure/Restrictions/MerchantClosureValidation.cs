using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Azure;
using Azure.Identity;
using ErrorOr;
using Intelibill.Licensing.Application.Restrictions;
using Intelibill.Licensing.Infrastructure.Entitlements;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Web.Delivery;

namespace Intelibill.Licensing.Infrastructure.Restrictions;

internal sealed class MerchantClosureValidation(LicensingEntitlementTransport transport, IHttpClientFactory clients)
    : IMerchantClosureValidation
{
    public async Task<ErrorOr<ClosureValidatedV1>> CheckAsync(ClosureValidationV1 validation, CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(4));
        try
        {
            return await ExchangeAsync(validation, budget.Token);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException or
            OperationCanceledException or RequestFailedException or AuthenticationFailedException or
            System.Security.Cryptography.CryptographicException)
        {
            ct.ThrowIfCancellationRequested();
            return Unavailable;
        }
    }

    private async Task<ErrorOr<ClosureValidatedV1>> ExchangeAsync(ClosureValidationV1 validation, CancellationToken ct)
    {
        var path = ShopClosuresV1.ValidationPath;
        var body = JsonSerializer.SerializeToUtf8Bytes(validation, EntitlementSnapshotsV1.Json);
        if (transport.Origin is not { } origin || await transport.SignAsync(body, ct, path) is not { } signature)
            return Unavailable;

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(origin, path)) { Content = new ByteArrayContent(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", signature);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var client = clients.CreateClient(LeasedHttpDeliveryPump.ClientName);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.Conflict)
            return Error.Conflict("Closure.ReviewChanged", "Each affected shop owner must confirm a current review.");
        if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentType?.MediaType != "application/json" ||
            !response.Headers.TryGetValues(ShopClosuresV1.ProofHeader, out var signatures) || signatures.ToArray() is not [var proof])
            return Unavailable;

        await response.Content.LoadIntoBufferAsync(ShopClosuresV1.BodyLimit, ct);
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        if (transport.Verify(path, proof, bytes) is null ||
            JsonSerializer.Deserialize<ClosureValidatedV1>(bytes, EntitlementSnapshotsV1.Json) is not { } saved ||
            !Matches(validation, saved))
            return Unavailable;
        return saved;
    }

    private static bool Matches(ClosureValidationV1 expected, ClosureValidatedV1 actual) =>
        expected.AccountId == actual.AccountId && expected.ProposalId == actual.ProposalId &&
        expected.Challenge == actual.Challenge && expected.ScopeDigest == actual.ScopeDigest &&
        actual.Targets is not null && actual.Targets.Select(row => row.ShopId).Order()
            .SequenceEqual(expected.Targets.Select(row => row.ShopId).Order()) &&
        actual.Targets.All(row => row.ConsentId != Guid.Empty && ShopClosuresV1.IsDigest(row.WorkDigest));

    private static Error Unavailable => Error.Failure("Closure.MerchantUnavailable", "Closure validation is temporarily unavailable.");
}
