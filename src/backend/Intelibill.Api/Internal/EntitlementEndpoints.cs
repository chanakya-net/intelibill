using ErrorOr;
using System.Text.Json;
using Intelibill.Application.Common.Licensing;
using Intelibill.Infrastructure.Licensing;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Web.Http;
using Npgsql;
using LicenceErrors = Intelibill.Application.Common.Errors.Errors.Licence;

namespace Intelibill.Api.Internal;

internal static class EntitlementEndpoints
{
    internal static void MapEntitlementReceiver(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost(EntitlementSnapshotsV1.SnapshotPath, ApplyAsync).AllowAnonymous()
            .WithMetadata(new LicenceActionAttribute(LicenceAction.InternalService));

    private static async Task<IResult> ApplyAsync(HttpContext context, MerchantEntitlementTransport transport,
        EntitlementProjectionStore projection, LicenceAuthorityNotifications notifications)
    {
        if (await PrivateIntegrationRequest.ReadAsync(context, EntitlementSnapshotsV1.SnapshotPath,
                EntitlementSnapshotsV1.BodyLimit) is not { } request ||
            transport.Verify(request.Token, request.Body) is not { } proof)
            return Results.Unauthorized();
        try
        {
            var snapshot = JsonSerializer.Deserialize<EntitlementSnapshotV1>(request.Body, EntitlementSnapshotsV1.Json);
            if (snapshot is null || !snapshot.HasValidIdentity())
                return Results.BadRequest();
            var result = await projection.ApplyWithChangeAsync(snapshot, request.Body, proof, context.RequestAborted);
            if (!result.IsError)
            {
                if (result.Value.Changed) notifications.Schedule(snapshot.ShopId);
                return Results.Bytes(result.Value.Acknowledgement, "application/json");
            }
            if (result.FirstError.Code == LicenceErrors.BusyCode)
                return Results.StatusCode(503);
            return result.FirstError.Type == ErrorType.Conflict ? Results.Conflict() : Results.Unauthorized();
        }
        catch (JsonException)
        {
            return Results.BadRequest();
        }
        catch (Exception error) when (error is NpgsqlException or TimeoutException or
            Microsoft.EntityFrameworkCore.DbUpdateException { InnerException: NpgsqlException })
        {
            return Results.StatusCode(503);
        }
    }
}
