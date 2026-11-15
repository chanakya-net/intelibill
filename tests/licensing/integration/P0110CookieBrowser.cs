using System.Net.Http.Json;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// A cookie jar the test controls: it can be copied to replay exactly what a browser held,
/// including onto another replica, without the framework handler merging or dropping cookies.
/// </summary>
internal sealed class P0110CookieBrowser(HttpClient client) : IDisposable
{
    internal const string Origin = "https://localhost";
    private readonly Dictionary<string, string> _jar = new(StringComparer.Ordinal);

    internal string? this[string name] => _jar.GetValueOrDefault(name);

    internal P0110CookieBrowser Fork(HttpClient other)
    {
        var copy = new P0110CookieBrowser(other);
        foreach (var (name, value) in _jar) copy._jar[name] = value;
        return copy;
    }

    internal void Forget(string name) => _jar.Remove(name);

    internal void Set(string name, string value) => _jar[name] = value;

    internal async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null,
        string? csrf = null, string origin = Origin, string csrfHeader = "X-Admin-CSRF")
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (body is not null) request.Content = body as HttpContent ?? JsonContent.Create(body);
        if (method != HttpMethod.Get) request.Headers.Add("Origin", origin);
        if (csrf is not null) request.Headers.Add(csrfHeader, csrf);
        if (_jar.Count > 0) request.Headers.Add("Cookie", string.Join("; ", _jar.Select(pair => pair.Key + "=" + pair.Value)));
        var response = await client.SendAsync(request);
        Absorb(response);
        return response;
    }

    private void Absorb(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var headers)) return;
        foreach (var header in headers)
        {
            var pair = header.Split(';', 2)[0].Split('=', 2);
            var expired = pair[1].Length == 0
                || header.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase)
                || header.Contains("max-age=0", StringComparison.OrdinalIgnoreCase);
            if (expired) _jar.Remove(pair[0]);
            else _jar[pair[0]] = pair[1];
        }
    }

    public void Dispose() => client.Dispose();
}
