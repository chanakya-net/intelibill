using System.Security.Cryptography;
using Intelibill.Licensing.Application.Payments;
using Npgsql;

namespace Intelibill.Licensing.Api.Payments;

internal static partial class GatewayWebhookEndpoints
{
    private const int Limit = 64 * 1024;

    internal static void MapGatewayWebhooks(this WebApplication app) =>
        app.MapPost("/api/payments/webhooks/{provider}", ReceiveAsync).AllowAnonymous();

    private static async Task<IResult> ReceiveAsync(HttpContext context, string provider,
        ReceiveGatewayWebhookHandler handler, ILogger<ReceiveGatewayWebhookHandler> logger)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (context.Request.ContentLength > Limit)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

        var ct = context.RequestAborted;
        try
        {
            var body = new byte[Limit + 1];
            var length = await context.Request.Body.ReadAtLeastAsync(body, body.Length, false, ct);
            if (length > Limit)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            var headers = context.Request.Headers.Where(pair => pair.Value.Count == 1)
                .ToDictionary(pair => pair.Key, pair => pair.Value[0]!, StringComparer.OrdinalIgnoreCase);
            return await handler.HandleAsync(provider, body.AsMemory(0, length), headers, ct) switch
            {
                GatewayWebhookReceipt.Disabled => Results.NotFound(),
                GatewayWebhookReceipt.Rejected => Rejected(logger),
                _ => Results.NoContent()
            };
        }
        catch (Exception error) when (error is NpgsqlException or TimeoutException or CryptographicException)
        {
            ct.ThrowIfCancellationRequested();
            StorageUnavailable(logger);
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static IResult Rejected(ILogger logger)
    {
        AuthenticationRejected(logger);
        return Results.Unauthorized();
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Payment webhook authentication rejected.")]
    private static partial void AuthenticationRejected(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Payment webhook receipt unavailable.")]
    private static partial void StorageUnavailable(ILogger logger);
}
