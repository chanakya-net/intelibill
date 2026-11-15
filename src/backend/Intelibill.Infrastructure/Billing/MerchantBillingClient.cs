using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Azure;
using Azure.Identity;
using ErrorOr;
using Intelibill.Application.Features.Billing;
using Intelibill.Shared.Contracts.V1;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Intelibill.Infrastructure.Billing;

internal sealed class MerchantBillingClient(
    HttpClient client, MerchantBillingDelegationIssuer issuer,
    IOptionsMonitor<MerchantBillingDelegationOptions> options, IHostEnvironment environment) : IMerchantBillingAccess
{
    private const int MaximumResponseBytes = 4096;

    public async Task<ErrorOr<BillingAccountAccessV1>> GetAsync(
        MerchantBillingAccessRequest access, CancellationToken cancellationToken)
    {
        var origin = options.CurrentValue.GetOrigin(environment);
        if (origin is null)
            return MerchantBillingErrors.Unavailable;
        if (access.AccountId == Guid.Empty)
            return MerchantBillingErrors.Denied;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            return await SendAsync(origin, access, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return MerchantBillingErrors.Unavailable;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or IOException
            or NotSupportedException or NpgsqlException or TimeoutException or RequestFailedException
            or AuthenticationFailedException or CryptographicException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return MerchantBillingErrors.Unavailable;
        }
    }

    private async Task<ErrorOr<BillingAccountAccessV1>> SendAsync(
        Uri origin, MerchantBillingAccessRequest access, CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid();
        var token = await issuer.IssueAsync(access, requestId, cancellationToken);
        if (token.IsError)
            return token.Errors;

        using var request = new HttpRequestMessage(HttpMethod.Get,
            new Uri(origin, MerchantBillingDelegationV1.AccountAccessPath(access.AccountId)));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value.DelegationToken);
        request.Headers.Add(MerchantBillingDelegationV1.RequestIdHeader, requestId.ToString("D"));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Forbidden)
            return MerchantBillingErrors.Denied;
        if (response.StatusCode != HttpStatusCode.OK ||
            response.Content.Headers.ContentType?.MediaType != "application/json")
            return MerchantBillingErrors.Unavailable;

        await response.Content.LoadIntoBufferAsync(MaximumResponseBytes, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<BillingAccountAccessV1>(cancellationToken);
        return result?.AccountId == access.AccountId && result.Actions is [MerchantBillingDelegationV1.ReadAccount]
            ? new BillingAccountAccessV1(result.AccountId, [MerchantBillingDelegationV1.ReadAccount])
            : MerchantBillingErrors.Unavailable;
    }
}
