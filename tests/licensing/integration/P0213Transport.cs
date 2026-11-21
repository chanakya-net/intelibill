using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Intelibill.Licensing.Integration.Tests;

internal sealed class P0213Traffic : IStartupFilter
{
    private int _count;
    internal int Count => Volatile.Read(ref _count);

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, pipeline) =>
        {
            if (context.Request.Path == EntitlementSnapshotsV1.SnapshotPath ||
                context.Request.Path == EntitlementSnapshotsV1.AcknowledgementPath ||
                context.Request.Path == EntitlementSnapshotsV1.ReconciliationPath)
                Interlocked.Increment(ref _count);
            return pipeline(context);
        });
        next(app);
    };
}

internal sealed class P0213Transport : IDisposable
{
    private const string SourceKey = "fixture-snapshot";
    private const string MerchantKey = "fixture-merchant";
    private readonly RSA _source = RSA.Create(2048);
    private readonly RSA _merchant = RSA.Create(2048);
    private HttpMessageInvoker? _toMerchant;
    private HttpMessageInvoker? _toLicensing;
    private Uri? _merchantOrigin;
    private Uri? _licensingOrigin;

    internal P0201Clock LeaseClock { get; } = new() { Now = P0202Fixture.WholeSecondNow().AddMinutes(1) };
    internal Action<HttpRequestMessage>? BeforeSend { get; set; }
    internal Func<HttpRequestMessage, CancellationToken, Task>? BeforeDispatch { get; set; }
    internal Func<HttpRequestMessage, HttpResponseMessage, CancellationToken, Task>? AfterCommit { get; set; }

    internal void AddSettings(Dictionary<string, string?> settings, bool licensing)
    {
        var local = licensing ? _source : _merchant;
        var peer = licensing ? _merchant : _source;
        var section = EntitlementSnapshotsV1.Section;
        settings[$"{section}:Enabled"] = "true";
        settings[$"{section}:PeerOrigin"] = licensing ? "https://merchant.test" : "https://licensing.test";
        settings[$"{section}:LocalKeyId"] = licensing ? SourceKey : MerchantKey;
        settings[$"{section}:LocalPrivateKeyPem"] = local.ExportPkcs8PrivateKeyPem();
        settings[$"{section}:PublicKeys:{(licensing ? MerchantKey : SourceKey)}"] = peer.ExportSubjectPublicKeyInfoPem();
    }

    internal void Connect(P0209Fixture.Hosts hosts, Uri merchant, Uri licensing, bool browser)
    {
        _merchantOrigin = merchant;
        _licensingOrigin = licensing;
        _toMerchant = new HttpMessageInvoker(browser ? LocalSockets() : hosts.Merchant.Server.CreateHandler());
        _toLicensing = new HttpMessageInvoker(browser ? LocalSockets() : hosts.Licensing.Server.CreateHandler());
    }

    internal HttpMessageHandler Bridge(bool licensing) => new BridgeHandler(this, licensing);

    private static SocketsHttpHandler LocalSockets() => new() { AllowAutoRedirect = false, UseProxy = false, UseCookies = false };

    private sealed class BridgeHandler(P0213Transport fixture, bool licensing) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var next = licensing ? fixture._toMerchant : fixture._toLicensing;
            var origin = licensing ? fixture._merchantOrigin : fixture._licensingOrigin;
            Assert.NotNull(next);
            request.RequestUri = new Uri(origin!, request.RequestUri!.PathAndQuery);
            fixture.BeforeSend?.Invoke(request);
            if (fixture.BeforeDispatch is { } dispatch)
                await dispatch(request, cancellationToken);
            var response = await next.SendAsync(request, cancellationToken);
            try
            {
                if (fixture.AfterCommit is { } observe)
                    await observe(request, response, cancellationToken);
                return response;
            }
            catch
            {
                response.Dispose();
                throw;
            }
        }
    }

    internal Task<HttpRequestMessage> SignedAsync(EntitlementSnapshotV1 snapshot, TimeProvider clock) =>
        SignedAsync(JsonSerializer.SerializeToUtf8Bytes(snapshot, EntitlementSnapshotsV1.Json), clock);

    internal async Task<HttpRequestMessage> SignedAsync(byte[] body, TimeProvider clock, bool merchant = false,
        string path = EntitlementSnapshotsV1.SnapshotPath, ServiceAssertionPurpose? purpose = null)
    {
        var rsa = merchant ? _merchant : _source;
        var token = await SignedServiceAssertion.SignAsync(purpose ?? (merchant ? SignedServiceAssertion.MerchantEvents :
            SignedServiceAssertion.LicensingDelivery), merchant ? MerchantKey : SourceKey, path, body, clock,
            (input, _) => ValueTask.FromResult(rsa.SignData(input, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)),
            CancellationToken.None);
        return Request(body, path, token);
    }

    internal static HttpRequestMessage Request(byte[] body, string path, string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    public void Dispose()
    {
        _toMerchant?.Dispose();
        _toLicensing?.Dispose();
        _source.Dispose();
        _merchant.Dispose();
    }
}
