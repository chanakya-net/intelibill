using Azure.Identity;
using Azure;
using Intelibill.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;

namespace Intelibill.Infrastructure.Billing;

internal sealed partial class ShopAffiliationDelivery(ApplicationDbContext db, IHttpClientFactory clients,
    IOptionsMonitor<MerchantBillingDelegationOptions> options, IHostEnvironment host, TimeProvider clock,
    ILogger<ShopAffiliationDelivery> logger,
    [FromKeyedServices(ShopAffiliationDeliveryV1.Section)] IMerchantBillingSigningKeys keys)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string Path = ShopAffiliationsV1.DeliveryPath;

    internal async Task DrainAsync(CancellationToken ct)
    {
        var delivery = options.Get(ShopAffiliationDeliveryV1.Section);
        if (options.CurrentValue.GetOrigin(host) is not { } origin ||
            (delivery.LocalPrivateKeyPem is null && delivery.KeyVaultKeyId is null))
            return;
        using var key = await keys.ResolveAsync(ct);
        if (key is null)
            return;
        using var client = clients.CreateClient(MerchantBillingRegistration.ClientName);

        var after = Guid.Empty;
        while (!ct.IsCancellationRequested)
        {
            var events = await db.Database.SqlQuery<ShopAffiliationDecisionV1>($"""
                SELECT * FROM public.pending_shop_affiliation_decisions({after})
                """).ToListAsync(ct);
            if (events.Count == 0)
                return;
            foreach (var item in events)
                await TryDeliverAsync(client, origin, key, item, ct);
            after = events[^1].EventId;
        }
    }

    private async Task TryDeliverAsync(HttpClient client, Uri origin, MerchantBillingSigningKey key,
        ShopAffiliationDecisionV1 item, CancellationToken ct)
    {
        try
        {
            await DeliverAsync(client, origin, key, item, ct);
        }
        catch (Exception exception) when (!ct.IsCancellationRequested &&
            exception is NpgsqlException or HttpRequestException or TimeoutException or OperationCanceledException
                or JsonException or IOException or CryptographicException or RequestFailedException or AuthenticationFailedException)
        {
            LogRetry(logger, exception.GetType().Name);
        }
    }

    private async Task DeliverAsync(HttpClient client, Uri origin, MerchantBillingSigningKey key,
        ShopAffiliationDecisionV1 item, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // This lock coordinates delivery replicas only. No merchant authority row is held over HTTP.
        if (!await LockPendingAsync(item, ct))
            return;

        var result = await SendAsync(client, origin, key, item, ct);
        if (result is null)
            return;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.shop_affiliation_delivery_receipts (event_id, shop_id, status)
            VALUES ({result.EventId}, {item.ShopId}, {result.Status})
            ON CONFLICT (event_id) DO NOTHING
            """, ct);
        await transaction.CommitAsync(ct);
    }

    private async Task<bool> LockPendingAsync(ShopAffiliationDecisionV1 item, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT set_config('app.active_shop_id', {item.ShopId.ToString("D")}, true)
            """, ct);
        var locked = await db.Database.SqlQuery<bool>($"""
            SELECT pg_try_advisory_xact_lock(hashtextextended({'a' + item.EventId.ToString("D")}, 0)) AS "Value"
            """).SingleAsync(ct);
        return locked && !await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT FROM public.shop_affiliation_delivery_receipts WHERE event_id = {item.EventId}) AS "Value"
            """).SingleAsync(ct);
    }

    private async Task<ShopAffiliationAcknowledgementV1?> SendAsync(HttpClient client, Uri origin,
        MerchantBillingSigningKey key, ShopAffiliationDecisionV1 item, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var body = JsonSerializer.SerializeToUtf8Bytes(item, Json);
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(origin, Path));
        message.Content = new ByteArrayContent(body);
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await SignAsync(key, body, clock, Path, ShopAffiliationDeliveryV1.TokenType,
            ShopAffiliationDeliveryV1.Issuer, ShopAffiliationDeliveryV1.Audience, timeout.Token));
        using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType != "application/json")
            return null;
        await response.Content.LoadIntoBufferAsync(1024, timeout.Token);
        var result = await JsonSerializer.DeserializeAsync<ShopAffiliationAcknowledgementV1>(
            await response.Content.ReadAsStreamAsync(timeout.Token), Json, timeout.Token);
        return result?.EventId == item.EventId && result.Status is "Applied" or "NeedsReview" ? result : null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "An affiliation decision remains pending ({Cause}).")]
    private static partial void LogRetry(ILogger logger, string cause);

    internal static async Task<string> SignAsync(MerchantBillingSigningKey key, byte[] body, TimeProvider clock,
        string path, string type, string issuer, string audience, CancellationToken ct)
    {
        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        var header = new { alg = "RS256", typ = type, kid = key.Id };
        var payload = new
        {
            v = 1, iss = issuer, aud = audience,
            jti = Guid.NewGuid(), iat = now, nbf = now, exp = now + ShopAffiliationDeliveryV1.LifetimeSeconds,
            method = "POST", path, body_sha256 = Convert.ToHexStringLower(SHA256.HashData(body)),
        };
        var input = Base64UrlEncoder.Encode(JsonSerializer.SerializeToUtf8Bytes(header)) + "." +
            Base64UrlEncoder.Encode(JsonSerializer.SerializeToUtf8Bytes(payload));
        return input + "." + Base64UrlEncoder.Encode(await key.SignAsync(Encoding.ASCII.GetBytes(input), ct));
    }
}
