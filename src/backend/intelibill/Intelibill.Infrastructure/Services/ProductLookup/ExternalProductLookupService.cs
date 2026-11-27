using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ErrorOr;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Infrastructure.Options;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace Intelibill.Infrastructure.Services.ProductLookup;

internal sealed class ExternalProductLookupService(
    IHttpClientFactory httpClientFactory,
    IOptions<ProductLookupOptions> options,
    IOptions<CircuitBreakerOptions> circuitBreakerOptions)
    : IExternalProductLookupService
{
    public const string HttpClientName = "ExternalProductLookup";

    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly ProductLookupOptions _options = options.Value;

    private readonly ResiliencePipeline<HttpResponseMessage> _resiliencePipeline =
        new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = 2,
                Delay = TimeSpan.FromMilliseconds(150),
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
                    .HandleResult(static response =>
                        response.StatusCode == HttpStatusCode.RequestTimeout
                        || response.StatusCode == HttpStatusCode.TooManyRequests
                        || (int)response.StatusCode >= 500),
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
            {
                FailureRatio = 0.5,
                SamplingDuration = TimeSpan.FromSeconds(circuitBreakerOptions.Value.SamplingDurationSeconds),
                MinimumThroughput = circuitBreakerOptions.Value.MinimumThroughput,
                BreakDuration = TimeSpan.FromSeconds(circuitBreakerOptions.Value.BreakDurationSeconds),
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
                    .HandleResult(static response =>
                        response.StatusCode == HttpStatusCode.RequestTimeout
                        || response.StatusCode == HttpStatusCode.TooManyRequests
                        || (int)response.StatusCode >= 500),
            })
            .Build();

    public async Task<ErrorOr<ExternalProductLookupResult?>> LookupByBarcodeAsync(
        string barcode,
        string? authorizationHeader,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(barcode))
            return (ExternalProductLookupResult?)null;

        HttpResponseMessage response;
        try
        {
            var httpClient = _httpClientFactory.CreateClient(HttpClientName);
            response = await _resiliencePipeline.ExecuteAsync(
                async token =>
                {
                    using var request = CreateLookupRequest(barcode, authorizationHeader);
                    return await httpClient.SendAsync(request, token);
                },
                cancellationToken);
        }
        catch (BrokenCircuitException)
        {
            return Error.Failure(
                code: "product.lookup.circuit_open",
                description: "External product lookup is temporarily unavailable due to repeated failures.");
        }
        catch (HttpRequestException)
        {
            return Error.Failure(
                code: "product.lookup.request_failed",
                description: "External product lookup request failed.");
        }
        catch (TaskCanceledException)
        {
            return Error.Failure(
                code: "product.lookup.timeout",
                description: "External product lookup timed out.");
        }

        using var responseDisposable = response;

        if (!response.IsSuccessStatusCode)
        {
            return Error.Failure(
                code: "product.lookup.failed",
                description: $"External product lookup failed with status code {(int)response.StatusCode}.");
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        try
        {
            using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
            return ParseLookupResponse(document.RootElement, barcode);
        }
        catch (JsonException)
        {
            return Error.Failure(
                code: "product.lookup.invalid_response",
                description: "External product lookup returned an invalid response.");
        }
    }

    private static ErrorOr<ExternalProductLookupResult?> ParseLookupResponse(JsonElement root, string barcode)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return Error.Failure(
                code: "product.lookup.invalid_response",
                description: "External product lookup returned an invalid response shape.");
        }

        var normalizedBarcode = barcode.Trim();
        if (!TryGetProductsProperty(root, out var productsArray))
        {
            return Error.Failure(
                code: "product.lookup.invalid_response",
                description: "External product lookup response does not contain products array.");
        }

        foreach (var product in productsArray.EnumerateArray())
        {
            if (product.ValueKind != JsonValueKind.Object)
                continue;

            var candidateBarcode = GetStringProperty(product, "barcode");
            if (!string.Equals(candidateBarcode, normalizedBarcode, StringComparison.OrdinalIgnoreCase))
                continue;

            var productName = GetStringProperty(product, "name");
            if (string.IsNullOrWhiteSpace(productName))
                return (ExternalProductLookupResult?)null;

            return new ExternalProductLookupResult(
                productName.Trim(),
                Description: null,
                Uom: null);
        }

        if (TryGetNotFoundProperty(root, out var notFoundArray))
        {
            foreach (var notFound in notFoundArray.EnumerateArray())
            {
                if (notFound.ValueKind == JsonValueKind.String
                    && string.Equals(notFound.GetString(), normalizedBarcode, StringComparison.OrdinalIgnoreCase))
                {
                    return (ExternalProductLookupResult?)null;
                }
            }
        }

        return (ExternalProductLookupResult?)null;
    }

    private static bool TryGetProductsProperty(JsonElement root, out JsonElement products)
    {
        products = default;
        foreach (var property in root.EnumerateObject())
        {
            if (!string.Equals(property.Name, "products", StringComparison.OrdinalIgnoreCase))
                continue;

            if (property.Value.ValueKind != JsonValueKind.Array)
                return false;

            products = property.Value;
            return true;
        }

        return false;
    }

    private static bool TryGetNotFoundProperty(JsonElement root, out JsonElement notFound)
    {
        notFound = default;
        foreach (var property in root.EnumerateObject())
        {
            if (!string.Equals(property.Name, "notFound", StringComparison.OrdinalIgnoreCase))
                continue;

            if (property.Value.ValueKind != JsonValueKind.Array)
                return false;

            notFound = property.Value;
            return true;
        }

        return false;
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                continue;

            return property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
        }

        return null;
    }

    private HttpRequestMessage CreateLookupRequest(string barcode, string? authorizationHeader)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, _options.LookupPath)
        {
            Content = JsonContent.Create(new LookupRequest([barcode])),
        };

        if (!string.IsNullOrWhiteSpace(authorizationHeader))
            request.Headers.TryAddWithoutValidation("Authorization", authorizationHeader);

        request.Headers.TryAddWithoutValidation("X-API-Key", _options.ApiKey);
        return request;
    }

    private sealed record LookupRequest(IReadOnlyList<string> Barcodes);
}
