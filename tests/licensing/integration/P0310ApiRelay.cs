using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>Transport-only loopback relay keeps browser origins stable while real fixture API hosts restart.</summary>
internal sealed class P0310ApiRelay : IAsyncDisposable
{
    private readonly WebApplication application;
    private readonly HttpClient client = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
    private Uri upstream;
    private P0310ApiRelay(WebApplication application, Uri upstream)
    {
        this.application = application;
        this.upstream = upstream;
    }
    internal Uri Origin { get; private set; } = null!;
    internal Uri Upstream { set => Volatile.Write(ref upstream, value); }

    internal static async Task<P0310ApiRelay> StartAsync(Uri upstream, CancellationToken ct)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(server => server.Listen(System.Net.IPAddress.Loopback, 0));
        var app = builder.Build();
        var relay = new P0310ApiRelay(app, upstream);
        app.Run(relay.ForwardAsync);
        await app.StartAsync(ct);
        relay.Origin = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
        return relay;
    }

    private async Task ForwardAsync(HttpContext context)
    {
        using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method),
            new Uri(Volatile.Read(ref upstream), context.Request.Path + context.Request.QueryString));
        if (context.Request.ContentLength > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
            request.Content = new StreamContent(context.Request.Body);
        foreach (var header in context.Request.Headers)
        {
            if (HopHeader(header.Key)) continue;
            if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
                request.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }
        request.Headers.Host = context.Request.Host.Value;
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
        context.Response.StatusCode = (int)response.StatusCode;
        foreach (var header in response.Headers.Concat(response.Content.Headers))
            if (!HopHeader(header.Key)) context.Response.Headers[header.Key] = header.Value.ToArray();
        await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
    }

    private static bool HopHeader(string name) => name.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Connection", StringComparison.OrdinalIgnoreCase) || name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase);

    public async ValueTask DisposeAsync()
    {
        await application.DisposeAsync();
        client.Dispose();
    }
}

internal sealed class P0310LicensingTransport(Uri origin) : DelegatingHandler(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        request.RequestUri = new(origin, request.RequestUri!.PathAndQuery);
        return base.SendAsync(request, ct);
    }
}
