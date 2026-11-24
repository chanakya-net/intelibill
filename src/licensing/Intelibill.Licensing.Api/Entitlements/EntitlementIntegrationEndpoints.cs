using ErrorOr;
using System.Text.Json;
using Intelibill.Licensing.Infrastructure.Entitlements;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Intelibill.Shared.Web.Http;
using Npgsql;

namespace Intelibill.Licensing.Api.Entitlements;

internal static class EntitlementIntegrationEndpoints
{
    internal static void MapEntitlementIntegration(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(CapacityReservationV1.ReceiptPath, (HttpContext context, LicensingEntitlementTransport transport,
            EntitlementMerchantEvents events) => ReceiveAsync<CapacityReceiptV1>(context, transport,
                CapacityReservationV1.ReceiptPath, events.CapacityAsync)).AllowAnonymous();
        endpoints.MapPost(EntitlementSnapshotsV1.AcknowledgementPath, (HttpContext context,
            LicensingEntitlementTransport transport, EntitlementMerchantEvents events) => ReceiveAsync<EntitlementAcknowledgementV1>(context,
                transport, EntitlementSnapshotsV1.AcknowledgementPath, events.AcknowledgeAsync)).AllowAnonymous();
        endpoints.MapPost(EntitlementSnapshotsV1.ReconciliationPath, (HttpContext context,
            LicensingEntitlementTransport transport, EntitlementMerchantEvents events) => ReceiveAsync<EntitlementReconciliationV1>(context,
                transport, EntitlementSnapshotsV1.ReconciliationPath, events.ReconcileAsync)).AllowAnonymous();
        endpoints.MapPost(OfflineDeviceObservationsV1.Path, (HttpContext context,
            LicensingEntitlementTransport transport, EntitlementMerchantEvents events) => ReceiveAsync<OfflineDeviceObservationV1>(context,
                transport, OfflineDeviceObservationsV1.Path, events.ObserveDeviceAsync)).AllowAnonymous();
    }

    private static async Task<IResult> ReceiveAsync<T>(HttpContext context, LicensingEntitlementTransport transport,
        string path, Func<T, byte[], VerifiedServiceAssertion, CancellationToken, Task<ErrorOr<Success>>> process) where T : class
    {
        if (await PrivateIntegrationRequest.ReadAsync(context, path, EntitlementSnapshotsV1.BodyLimit) is not { } request ||
            transport.Verify(path, request.Token, request.Body) is not { } proof)
            return Results.Unauthorized();
        try
        {
            var body = JsonSerializer.Deserialize<T>(request.Body, EntitlementSnapshotsV1.Json);
            if (body is null) return Results.BadRequest();
            var result = await process(body, request.Body, proof, context.RequestAborted);
            if (!result.IsError) return Results.NoContent();
            return result.FirstError.Type switch
            {
                ErrorType.Validation => Results.BadRequest(),
                ErrorType.Unauthorized => Results.Unauthorized(),
                ErrorType.Conflict => Results.Conflict(),
                _ => Results.StatusCode(503),
            };
        }
        catch (JsonException)
        {
            return Results.BadRequest();
        }
        catch (Exception error) when (error is NpgsqlException or TimeoutException)
        {
            return Results.StatusCode(503);
        }
    }
}
