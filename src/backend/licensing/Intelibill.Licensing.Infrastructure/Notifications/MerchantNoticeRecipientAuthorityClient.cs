using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Azure;
using Azure.Identity;
using Intelibill.Licensing.Application.Notifications;
using Intelibill.Licensing.Infrastructure.Entitlements;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Web.Delivery;

namespace Intelibill.Licensing.Infrastructure.Notifications;

/// <summary>
/// Signed request/reply boundary asking the merchant service for live verified recipient identities,
/// languages and active shop memberships. No SQL transaction is open across this call; any transport,
/// signature, or contract fault returns null so planning retries cleanly without fabricating recipients.
/// </summary>
internal sealed class MerchantNoticeRecipientAuthorityClient(
    LicensingEntitlementTransport transport,
    IHttpClientFactory clients) : IMerchantNoticeRecipientAuthority
{
    public bool Enabled => transport.HasPublicationRoute();

    public async Task<NoticeRecipientAuthorityResponseV1?> InspectAsync(
        NoticeRecipientAuthorityRequestV1 request,
        CancellationToken ct)
    {
        if (!request.IsValid() || !Enabled)
            return null;

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(4));
        try
        {
            return await ExchangeAsync(request, budget.Token);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException
            or OperationCanceledException or RequestFailedException or AuthenticationFailedException
            or System.Security.Cryptography.CryptographicException)
        {
            ct.ThrowIfCancellationRequested();
            return null;
        }
    }

    private async Task<NoticeRecipientAuthorityResponseV1?> ExchangeAsync(
        NoticeRecipientAuthorityRequestV1 payload,
        CancellationToken ct)
    {
        const string path = LicenseNoticesV1.RecipientAuthorityPath;
        var body = JsonSerializer.SerializeToUtf8Bytes(payload, EntitlementSnapshotsV1.Json);
        if (transport.Origin is not { } origin || await transport.SignAsync(body, ct, path) is not { } signature)
            return null;

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(origin, path))
        {
            Content = new ByteArrayContent(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", signature);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var client = clients.CreateClient(LeasedHttpDeliveryPump.ClientName);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode != HttpStatusCode.OK
            || response.Content.Headers.ContentType?.MediaType != "application/json"
            || !response.Headers.TryGetValues(LicenseNoticesV1.ProofHeader, out var signatures)
            || signatures.ToArray() is not [var proof])
        {
            return null;
        }

        await response.Content.LoadIntoBufferAsync(LicenseNoticesV1.RecipientAuthorityMaxResponseBytes, ct);
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        if (transport.Verify(path, proof, bytes) is null)
            return null;

        var answer = JsonSerializer.Deserialize<NoticeRecipientAuthorityResponseV1>(bytes, EntitlementSnapshotsV1.Json);
        return answer is not null && answer.IsValid(payload) ? answer : null;
    }
}
