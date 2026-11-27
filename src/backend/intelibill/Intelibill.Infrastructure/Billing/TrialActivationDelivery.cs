using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Azure;
using Azure.Identity;
using Intelibill.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Intelibill.Infrastructure.Billing;

internal sealed partial class TrialActivationDelivery(ApplicationDbContext db, IHttpClientFactory clients,
    IOptionsMonitor<MerchantBillingDelegationOptions> options, IHostEnvironment host, TimeProvider clock,
    ILogger<TrialActivationDelivery> logger,
    [FromKeyedServices(ShopTrialsV1.DeliverySection)] IMerchantBillingSigningKeys keys)
{
    private static JsonSerializerOptions Json => TrialActivationConsentStore.Json;

    internal async Task DrainAsync(CancellationToken ct)
    {
        var settings = options.Get(ShopTrialsV1.DeliverySection);
        if (options.CurrentValue.GetOrigin(host) is not { } origin ||
            (settings.LocalPrivateKeyPem is null && settings.KeyVaultKeyId is null))
            return;
        using var key = await keys.ResolveAsync(ct);
        if (key is null)
            return;

        using var client = clients.CreateClient(MerchantBillingRegistration.ClientName);
        var after = Guid.Empty;
        while (!ct.IsCancellationRequested)
        {
            var rows = await db.Database.SqlQuery<PendingRow>(
                $"SELECT * FROM public.pending_shop_trial_consents({after})").ToListAsync(ct);
            if (rows.Count == 0)
                return;
            foreach (var row in rows)
                await TryDeliverAsync(client, origin, key, row, ct);
            after = rows[^1].EventId;
        }
    }

    private async Task TryDeliverAsync(HttpClient client, Uri origin, MerchantBillingSigningKey key, PendingRow row, CancellationToken ct)
    {
        try
        {
            var consent = JsonSerializer.Deserialize<ShopTrialConsentV1>(row.Payload, Json)!;
            if (await SendAsync(client, origin, key, consent, ct) is not { } result)
                return;
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                SELECT set_config('app.active_shop_id', {consent.ShopId.ToString("D")}, true)
                """, ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO public.shop_trial_receipts (event_id, shop_id, status, reason)
                VALUES ({result.EventId}, {consent.ShopId}, {result.Status}, {result.Reason})
                ON CONFLICT (event_id) DO NOTHING
                """, ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception error) when (!ct.IsCancellationRequested && IsRetryable(error))
        {
            LogRetry(logger, error.GetType().Name);
        }
    }

    private async Task<ShopTrialDeliveryResultV1?> SendAsync(HttpClient client, Uri origin, MerchantBillingSigningKey key,
        ShopTrialConsentV1 consent, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var body = JsonSerializer.SerializeToUtf8Bytes(consent, Json);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(origin, ShopTrialsV1.DeliveryPath))
        {
            Content = new ByteArrayContent(body),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await ShopAffiliationDelivery.SignAsync(
            key, body, clock, ShopTrialsV1.DeliveryPath, ShopTrialsV1.DeliveryType, ShopTrialsV1.DeliveryIssuer,
            ShopTrialsV1.DeliveryAudience, timeout.Token));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType != "application/json")
        {
            LogRejected(logger, (int)response.StatusCode);
            return null;
        }
        await response.Content.LoadIntoBufferAsync(ShopTrialsV1.MaximumBodyBytes, timeout.Token);
        var result = await JsonSerializer.DeserializeAsync<ShopTrialDeliveryResultV1>(
            await response.Content.ReadAsStreamAsync(timeout.Token), Json, timeout.Token);
        return result?.EventId == consent.EventId && (result is { Status: "Applied", Reason: null } ||
            result is { Status: "NeedsReview", Reason.Length: > 0 and <= 40 }) ? result : null;
    }

    internal static bool IsRetryable(Exception error) => error is NpgsqlException or HttpRequestException or
        TimeoutException or OperationCanceledException or JsonException or IOException or CryptographicException or
        RequestFailedException or AuthenticationFailedException;

    [LoggerMessage(Level = LogLevel.Warning, Message = "A trial consent remains pending ({Cause}).")]
    internal static partial void LogRetry(ILogger logger, string cause);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Trial delivery returned HTTP {Status}.")]
    private static partial void LogRejected(ILogger logger, int status);

    private sealed record PendingRow(Guid EventId, string Payload);
}
