using System.Net;
using System.Text;
using BarcodeAPI.Infrastructure.OpenFoodFacts;

namespace BarcodeAPI.Api.Tests;

public sealed class OpenFoodFactsClientTests
{
    [Fact]
    public async Task GetByBarcode_ReturnsNull_WhenProductNameIsBlank()
    {
        using var client = CreateHttpClient("""{"status":1,"product_name":"   "}""");
        var sut = new OpenFoodFactsClient(client);

        var result = await sut.GetByBarcodeAsync(" 123 ", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByBarcode_ReturnsNull_WhenProductNameIsMissing()
    {
        using var client = CreateHttpClient("""{"status":1,"product":{"other":"value"}}""");
        var sut = new OpenFoodFactsClient(client);

        var result = await sut.GetByBarcodeAsync(" 123 ", CancellationToken.None);

        Assert.Null(result);
    }

    private static HttpClient CreateHttpClient(string json)
        => new(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        }))
        {
            BaseAddress = new Uri("https://example.com/")
        };

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}
