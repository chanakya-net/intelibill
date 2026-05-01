using System.Net;
using System.Net.Http;
using System.Text;
using ErrorOr;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Infrastructure.Options;
using Intelibill.Infrastructure.Services.ProductLookup;
using Microsoft.Extensions.Options;

namespace Intelibill.Api.Unit.Tests.Services;

public sealed class ExternalProductLookupServiceTests
{
    [Fact]
    public async Task LookupByBarcodeAsync_WhenLookupSucceeds_ReturnsProductAndForwardsHeaders()
    {
        var handler = new RecordingHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                                            "products": [
                                                { "barcode": "123456", "name": "  Premium Salt  " }
                      ],
                                            "notFound": []
                    }
                    """,
                    Encoding.UTF8,
                    "application/json"),
            }));

        var sut = CreateSut(handler);

        var result = await sut.LookupByBarcodeAsync("123456", "Bearer sample-token", CancellationToken.None);

        Assert.False(result.IsError);
        Assert.NotNull(result.Value);
        Assert.Equal("Premium Salt", result.Value!.ProductName);
        Assert.Equal("Bearer sample-token", handler.LastAuthorizationHeader);
        Assert.Equal("dev-api-key-change-in-production", handler.LastApiKeyHeader);
        Assert.Contains("123456", handler.LastRequestBody ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LookupByBarcodeAsync_WhenBarcodeIsBlank_ReturnsNullWithoutCallingHttp()
    {
        var handler = new RecordingHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"products\":[],\"notFound\":[]}", Encoding.UTF8, "application/json"),
            }));

        var sut = CreateSut(handler);

        var result = await sut.LookupByBarcodeAsync("   ", "Bearer ignored", CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Null(result.Value);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task LookupByBarcodeAsync_WhenApiReturnsBadRequest_ReturnsFailureError()
    {
        var handler = new RecordingHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"error\":\"invalid\"}", Encoding.UTF8, "application/json"),
            }));

        var sut = CreateSut(handler);

        var result = await sut.LookupByBarcodeAsync("123456", "Bearer sample-token", CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("product.lookup.failed", result.FirstError.Code);
    }

    [Fact]
    public async Task LookupByBarcodeAsync_WhenResponsePayloadIsInvalid_ReturnsInvalidResponseError()
    {
        var handler = new RecordingHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("not-json", Encoding.UTF8, "application/json"),
            }));

        var sut = CreateSut(handler);

        var result = await sut.LookupByBarcodeAsync("123456", null, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("product.lookup.invalid_response", result.FirstError.Code);
    }

    [Fact]
    public async Task LookupByBarcodeAsync_WhenApiReportsBarcodeInNotFound_ReturnsNull()
    {
        var handler = new RecordingHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                                            "products": [],
                                            "notFound": ["123456"]
                    }
                    """,
                    Encoding.UTF8,
                    "application/json"),
            }));

        var sut = CreateSut(handler);

        var result = await sut.LookupByBarcodeAsync("123456", null, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task LookupByBarcodeAsync_WhenHttpRequestFails_ReturnsRequestFailedError()
    {
        var handler = new RecordingHttpMessageHandler((_, _) => throw new HttpRequestException("network down"));

        var sut = CreateSut(handler);

        var result = await sut.LookupByBarcodeAsync("123456", null, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("product.lookup.request_failed", result.FirstError.Code);
    }

    private static ExternalProductLookupService CreateSut(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:5209"),
        };

        var factory = new StubHttpClientFactory(client);
        var options = Microsoft.Extensions.Options.Options.Create(new ProductLookupOptions());
        var circuitBreakerOptions = Microsoft.Extensions.Options.Options.Create(new CircuitBreakerOptions
        {
            SamplingDurationSeconds = 1,
            MinimumThroughput = 100,
            BreakDurationSeconds = 1,
        });

        return new ExternalProductLookupService(factory, options, circuitBreakerOptions);
    }

    private sealed class StubHttpClientFactory(HttpClient httpClient) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => httpClient;
    }

    private sealed class RecordingHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        public string? LastAuthorizationHeader { get; private set; }

        public string? LastApiKeyHeader { get; private set; }

        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastAuthorizationHeader = request.Headers.Authorization?.ToString();
            if (request.Headers.TryGetValues("X-API-Key", out var values))
                LastApiKeyHeader = values.FirstOrDefault();

            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return await responder(request, cancellationToken);
        }
    }
}
