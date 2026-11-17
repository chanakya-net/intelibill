using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Intelibill.Licensing.Application.Payments;

namespace Intelibill.Licensing.Infrastructure.Payments.Razorpay;

internal sealed partial class RazorpayGateway
{
    private const int MaxBodyBytes = 262_144;

    private async Task<(GatewayOutcome? Failure, JsonElement Root)> SendAsync(HttpRequestMessage message, CancellationToken ct)
    {
        message.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{config.KeyId}:{config.KeySecret}")));
        using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return (GatewayOutcome.NotFound, default);
        if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests)
            return (GatewayOutcome.Unavailable, default);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxBodyBytes)
            return (GatewayOutcome.Rejected, default);
        byte[]? bytes;
        try
        {
            bytes = await ReadBoundedAsync(response.Content, ct);
        }
        catch (IOException) when (!ct.IsCancellationRequested)
        {
            // The connection ended mid-body (HttpIOException): the provider may have acted, so the outcome is unknown.
            return (GatewayOutcome.Unavailable, default);
        }
        if (bytes is null)
            return (GatewayOutcome.Rejected, default);
        try
        {
            using var document = JsonDocument.Parse(bytes);
            return (null, document.RootElement.Clone());
        }
        catch (JsonException)
        {
            return (GatewayOutcome.Rejected, default);
        }
    }

    /// <summary>Reads at most <see cref="MaxBodyBytes"/> plus one byte, so a lengthless stream stops at the limit.</summary>
    private static async Task<byte[]?> ReadBoundedAsync(HttpContent content, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct);
        var buffer = new byte[MaxBodyBytes + 1];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), ct);
            if (read == 0)
                return buffer.AsSpan(0, total).ToArray();
            total += read;
        }
        return null;
    }
}
