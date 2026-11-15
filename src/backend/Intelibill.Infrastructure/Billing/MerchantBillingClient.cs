using System.Net;
using System.Net.Http.Headers;
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
    private const int MaximumListResponseBytes = 65536;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<ErrorOr<BillingAccountAccessV1>> GetAsync(
        MerchantBillingAccessRequest access, CancellationToken cancellationToken)
    {
        if (access.AccountId == Guid.Empty)
            return Task.FromResult<ErrorOr<BillingAccountAccessV1>>(MerchantBillingErrors.Denied);

        return RunAsync<BillingAccountAccessV1>(new MerchantBillingCaller(access.Session, access.AccessExpiry),
            BillingDelegationIntent.ReadAccount(access.AccountId), MaximumResponseBytes,
            async (response, token) =>
            {
                var result = await ReadJsonAsync<BillingAccountAccessV1>(response, token);
                return result?.AccountId == access.AccountId && result.Actions is [MerchantBillingDelegationV1.ReadAccount]
                    ? new BillingAccountAccessV1(result.AccountId, [MerchantBillingDelegationV1.ReadAccount])
                    : Unavailable("InvalidResponseContract");
            }, cancellationToken);
    }

    public Task<ErrorOr<BillingAccountListV1>> ListAccountsAsync(MerchantBillingCaller caller, Guid? after,
        CancellationToken cancellationToken) =>
        RunAsync<BillingAccountListV1>(caller, BillingDelegationIntent.ListAccounts(after), MaximumListResponseBytes, async (response, token) =>
        {
            var result = await ReadJsonAsync<BillingAccountListV1>(response, token);
            return result?.Accounts is { Length: <= MerchantBillingDelegationV1.MaximumListedAccounts } accounts &&
                accounts.All(IsValidAccount) && ValidContinuation(result, after)
                ? new BillingAccountListV1([.. accounts.Select(Copy)], result.NextCursor)
                : Unavailable("InvalidResponseContract");
        }, cancellationToken);

    private static bool ValidContinuation(BillingAccountListV1 page, Guid? after)
    {
        var previous = after;
        foreach (var account in page.Accounts)
        {
            if (previous is { } id && account.AccountId.CompareTo(id) <= 0)
                return false;
            previous = account.AccountId;
        }
        return page.NextCursor is null ||
            (page.Accounts.Length == MerchantBillingDelegationV1.MaximumListedAccounts && page.NextCursor == previous);
    }

    public Task<ErrorOr<MerchantCreatedBillingAccount>> CreateAccountAsync(MerchantBillingCaller caller,
        MerchantCreateBillingAccount request, CancellationToken cancellationToken)
    {
        if (request.OperationKey == Guid.Empty)
            return Task.FromResult<ErrorOr<MerchantCreatedBillingAccount>>(MerchantBillingErrors.OperationKeyInvalid);
        if (MerchantBillingDelegationV1.NormalizeAccountName(request.Name) is not { } name)
            return Task.FromResult<ErrorOr<MerchantCreatedBillingAccount>>(MerchantBillingErrors.NameInvalid);

        // Serialized here from validated values; these exact bytes are digested, signed and sent.
        var body = JsonSerializer.SerializeToUtf8Bytes(new CreateBillingAccountRequestV1(request.OperationKey, name));
        return RunAsync<MerchantCreatedBillingAccount>(caller, BillingDelegationIntent.CreateAccount(body), MaximumResponseBytes, async (response, token) =>
        {
            var result = await ReadJsonAsync<BillingAccountV1>(response, token);
            return result is not null && IsValidAccount(result) && result.Name == name &&
                result.Role == MerchantBillingDelegationV1.OwnerRole
                ? new MerchantCreatedBillingAccount(Copy(result), response.StatusCode == HttpStatusCode.Created)
                : Unavailable("InvalidResponseContract");
        }, cancellationToken);
    }

    private async Task<ErrorOr<T>> RunAsync<T>(MerchantBillingCaller caller, BillingDelegationIntent intent,
        int responseLimit, Func<HttpResponseMessage, CancellationToken, Task<ErrorOr<T>>> read,
        CancellationToken cancellationToken)
    {
        var origin = options.CurrentValue.GetOrigin(environment);
        if (origin is null)
            return Unavailable("InvalidLicensingOrigin");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            return await SendAsync(origin, caller, intent, responseLimit, read, timeout.Token);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Unavailable("DelegationTimeout", exception);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or IOException
            or NotSupportedException or NpgsqlException or TimeoutException or RequestFailedException
            or AuthenticationFailedException or System.Security.Cryptography.CryptographicException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Unavailable(FailureReason(exception), exception);
        }
    }

    private async Task<ErrorOr<T>> SendAsync<T>(Uri origin, MerchantBillingCaller caller, BillingDelegationIntent intent,
        int responseLimit, Func<HttpResponseMessage, CancellationToken, Task<ErrorOr<T>>> read,
        CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid();
        var token = await issuer.IssueAsync(caller, intent, requestId, cancellationToken);
        if (token.IsError)
            return token.FirstError == MerchantBillingErrors.Unavailable
                ? Unavailable("SigningKeyUnavailable") : token.Errors;

        using var request = new HttpRequestMessage(intent.Method, new Uri(origin, intent.Path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value.DelegationToken);
        request.Headers.Add(MerchantBillingDelegationV1.RequestIdHeader, requestId.ToString("D"));
        if (intent.Body.Length > 0)
        {
            request.Content = new ByteArrayContent(intent.Body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var failure = await MapFailureAsync(response, intent.Action == MerchantBillingDelegationV1.CreateAccount, cancellationToken);
        if (failure is { } error)
            return error;
        if (response.Content.Headers.ContentType?.MediaType != "application/json")
            return Unavailable("InvalidResponseContentType");
        if (response.Content.Headers.ContentLength > responseLimit)
            return Unavailable("ResponseTooLarge");

        await response.Content.LoadIntoBufferAsync(responseLimit, cancellationToken);
        return await read(response, cancellationToken);
    }

    /// <summary>Relay only the allow-listed upstream outcomes; everything else is unavailable.</summary>
    private async Task<Error?> MapFailureAsync(HttpResponseMessage response, bool creation,
        CancellationToken cancellationToken)
    {
        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
            case HttpStatusCode.Created when creation:
                return null;
            case HttpStatusCode.Forbidden:
                return MerchantBillingErrors.Denied;
            case HttpStatusCode.Unauthorized:
                return Unavailable("UpstreamAuthentication");
            case HttpStatusCode.BadRequest or HttpStatusCode.Conflict when creation:
                return await ReadProblemCodeAsync(response, cancellationToken) switch
                {
                    "Billing.AccountNameInvalid" when response.StatusCode == HttpStatusCode.BadRequest => MerchantBillingErrors.NameInvalid,
                    "Billing.OperationKeyInvalid" when response.StatusCode == HttpStatusCode.BadRequest => MerchantBillingErrors.OperationKeyInvalid,
                    "Billing.OperationPayloadMismatch" when response.StatusCode == HttpStatusCode.Conflict => MerchantBillingErrors.PayloadMismatch,
                    _ => Unavailable("UpstreamStatus"),
                };
            default:
                return Unavailable("UpstreamStatus");
        }
    }

    private static async Task<string?> ReadProblemCodeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentType?.MediaType != "application/problem+json" ||
            response.Content.Headers.ContentLength > MaximumResponseBytes)
            return null;
        await response.Content.LoadIntoBufferAsync(MaximumResponseBytes, cancellationToken);
        using var problem = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        return problem.RootElement.ValueKind == JsonValueKind.Object &&
            problem.RootElement.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String
            ? code.GetString() : null;
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await JsonSerializer.DeserializeAsync<T>(await response.Content.ReadAsStreamAsync(cancellationToken), Json,
            cancellationToken);

    private static bool IsValidAccount(BillingAccountV1? account) =>
        account is not null && account.AccountId != Guid.Empty &&
        MerchantBillingDelegationV1.NormalizeAccountName(account.Name) == account.Name &&
        account.Role is MerchantBillingDelegationV1.OwnerRole or MerchantBillingDelegationV1.ManagerRole;

    private static BillingAccountV1 Copy(BillingAccountV1 account) =>
        new(account.AccountId, account.Name, account.Role, account.CreatedAt);

    private static string FailureReason(Exception exception) => exception switch
    {
        AuthenticationFailedException => "VaultAuthentication",
        RequestFailedException => "VaultRequestFailed",
        System.Security.Cryptography.CryptographicException => "SigningFailed",
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
