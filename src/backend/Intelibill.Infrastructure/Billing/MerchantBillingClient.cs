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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Intelibill.Infrastructure.Billing;

internal sealed partial class MerchantBillingClient(
    HttpClient client, MerchantBillingDelegationIssuer issuer,
    IOptionsMonitor<MerchantBillingDelegationOptions> options, IHostEnvironment environment,
    ILogger<MerchantBillingClient> logger) : IMerchantBillingAccess
{
    private const int MaximumResponseBytes = 4096;

    public async Task<ErrorOr<BillingAccountAccessV1>> GetAsync(
        MerchantBillingAccessRequest access, CancellationToken cancellationToken)
    {
        var origin = options.CurrentValue.GetOrigin(environment);
        if (origin is null)
            return Unavailable("InvalidLicensingOrigin");
        if (access.AccountId == Guid.Empty)
            return MerchantBillingErrors.Denied;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            return await SendAsync(origin, access, timeout.Token);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Unavailable("DelegationTimeout", exception);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or IOException
            or NotSupportedException or NpgsqlException or TimeoutException or RequestFailedException
            or AuthenticationFailedException or CryptographicException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Unavailable(FailureReason(exception), exception);
        }
    }

    private async Task<ErrorOr<BillingAccountAccessV1>> SendAsync(
        Uri origin, MerchantBillingAccessRequest access, CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid();
        var token = await issuer.IssueAsync(access, requestId, cancellationToken);
        if (token.IsError)
            return token.FirstError == MerchantBillingErrors.Unavailable
                ? Unavailable("SigningKeyUnavailable") : token.Errors;

        using var request = new HttpRequestMessage(HttpMethod.Get,
            new Uri(origin, MerchantBillingDelegationV1.AccountAccessPath(access.AccountId)));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value.DelegationToken);
        request.Headers.Add(MerchantBillingDelegationV1.RequestIdHeader, requestId.ToString("D"));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Forbidden)
            return MerchantBillingErrors.Denied;
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            return Unavailable("UpstreamAuthentication");
        if (response.StatusCode != HttpStatusCode.OK)
            return Unavailable("UpstreamStatus");
        if (response.Content.Headers.ContentType?.MediaType != "application/json")
            return Unavailable("InvalidResponseContentType");
        if (response.Content.Headers.ContentLength > MaximumResponseBytes)
            return Unavailable("ResponseTooLarge");

        await response.Content.LoadIntoBufferAsync(MaximumResponseBytes, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<BillingAccountAccessV1>(cancellationToken);
        return result?.AccountId == access.AccountId && result.Actions is [MerchantBillingDelegationV1.ReadAccount]
            ? new BillingAccountAccessV1(result.AccountId, [MerchantBillingDelegationV1.ReadAccount])
            : Unavailable("InvalidResponseContract");
    }

    private static string FailureReason(Exception exception) => exception switch
    {
        AuthenticationFailedException => "VaultAuthentication",
        RequestFailedException => "VaultRequestFailed",
        CryptographicException => "SigningFailed",
        NpgsqlException => "MerchantStorageUnavailable",
        TimeoutException => "DependencyTimeout",
        JsonException or NotSupportedException => "InvalidResponseJson",
        HttpRequestException { HttpRequestError: HttpRequestError.ConfigurationLimitExceeded } => "ResponseLimitExceeded",
        _ => "TransportFailed",
    };

    private Error Unavailable(string reason, Exception? exception = null)
    {
        LogUnavailable(logger, reason, exception?.GetType().Name);
        return MerchantBillingErrors.Unavailable;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Merchant billing delegation failed ({Reason}); cause={Cause}.")]
    private static partial void LogUnavailable(ILogger logger, string reason, string? cause);
}
