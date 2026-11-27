using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ErrorOr;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Infrastructure.Options;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace Intelibill.Infrastructure.Services.Hsn;

internal sealed class ExternalHsnLookupService(
    IHttpClientFactory httpClientFactory,
    IOptions<HsnServiceOptions> options)
    : IExternalHsnLookupService
{
    private const string HttpClientName = "HsnService";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly HsnServiceOptions _options = options.Value;

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
            .Build();

    public async Task<ErrorOr<ApiResponse<ExternalHsnLookupResponse>>> LookupAsync(
        string productName,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            var httpClient = _httpClientFactory.CreateClient(HttpClientName);
            response = await _resiliencePipeline.ExecuteAsync(
                async token => await httpClient.PostAsJsonAsync(_options.LookupPath, new LookupRequest(productName), SerializerOptions, token),
                cancellationToken);
        }
        catch (HttpRequestException)
        {
            return Error.Failure(
                code: "hsn.lookup.request_failed",
                description: "HSN lookup request failed.");
        }
        catch (TaskCanceledException)
        {
            return Error.Failure(
                code: "hsn.lookup.timeout",
                description: "HSN lookup timed out.");
        }

        using var responseDisposable = response;

        if (!response.IsSuccessStatusCode)
        {
            return Error.Failure(
                code: "hsn.lookup.failed",
                description: $"HSN lookup failed with status code {(int)response.StatusCode}.");
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        try
        {
            var apiResponse = await JsonSerializer.DeserializeAsync<ApiResponse<ExternalHsnLookupResponse>>(
                responseStream,
                SerializerOptions,
                cancellationToken);

            if (apiResponse is null)
            {
                return Error.Failure(
                    code: "hsn.lookup.invalid_response",
                    description: "HSN lookup returned an invalid response.");
            }

            return apiResponse;
        }
        catch (JsonException)
        {
            return Error.Failure(
                code: "hsn.lookup.invalid_response",
                description: "HSN lookup returned an invalid response.");
        }
    }

    private sealed record LookupRequest(string ProductName);
}
