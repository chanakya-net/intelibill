using ErrorOr;
using Intelibill.Infrastructure.Billing;
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
    internal static void MapEntitlementReceiver(this IEndpointRouteBuilder endpoints)
    {
        foreach (var (phase, path) in new[] { (CapacityReservationV1.Reserve, CapacityReservationV1.ReservePath),
            (CapacityReservationV1.Consume, CapacityReservationV1.ConsumePath) })
            endpoints.MapPost(path, (HttpContext context, MerchantEntitlementTransport transport, ICapacityReservationReceiver receiver) =>
                CapacityAsync(context, transport, receiver, phase, path)).AllowAnonymous()
                .WithMetadata(new LicenceActionAttribute(LicenceAction.InternalService));
        endpoints.MapPost(ReductionTransportV1.ObservePath, ObserveReductionAsync).AllowAnonymous()
            .WithMetadata(new LicenceActionAttribute(LicenceAction.InternalService));
        endpoints.MapPost(ReductionTransportV1.ManifestPath, ReductionManifestAsync).AllowAnonymous()
            .WithMetadata(new LicenceActionAttribute(LicenceAction.InternalService));
        foreach (var (phase, path) in new[] { (ReductionTransportV1.ReserveKind, ReductionTransportV1.ReservePath),
            (ReductionTransportV1.ConsumeKind, ReductionTransportV1.ConsumePath) })
            endpoints.MapPost(path, (HttpContext context, MerchantEntitlementTransport transport, IReductionReservationReceiver receiver) =>
                ReductionReservationAsync(context, transport, receiver, phase, path)).AllowAnonymous()
                .WithMetadata(new LicenceActionAttribute(LicenceAction.InternalService));
        foreach (var (phase, path) in new[] {
            (CoverageReplacementExceptionsV1.PrepareKind, CoverageReplacementExceptionsV1.PreparePath),
            (CoverageReplacementExceptionsV1.ConsumeKind, CoverageReplacementExceptionsV1.ConsumePath),
            (CoverageReplacementExceptionsV1.AbortKind, CoverageReplacementExceptionsV1.AbortPath) })
            endpoints.MapPost(path, (HttpContext context, MerchantEntitlementTransport transport, ICoverageExceptionReceiver receiver) =>
                CoverageExceptionAsync(context, transport, receiver, phase, path)).AllowAnonymous()
                .WithMetadata(new LicenceActionAttribute(LicenceAction.InternalService));
        endpoints.MapPost(EntitlementSnapshotsV1.SnapshotPath, ApplyAsync).AllowAnonymous()
            .WithMetadata(new LicenceActionAttribute(LicenceAction.InternalService));
        endpoints.MapPost(ShopClosuresV1.ValidationPath, ValidateClosureAsync).AllowAnonymous()
            .WithMetadata(new LicenceActionAttribute(LicenceAction.InternalService));
    }

    private static async Task<IResult> CapacityAsync(HttpContext context, MerchantEntitlementTransport transport,
        ICapacityReservationReceiver receiver, string phase, string path)
    {
        if (await PrivateIntegrationRequest.ReadAsync(context, path, EntitlementSnapshotsV1.BodyLimit) is not { } request ||
            transport.Verify(request.Token, request.Body, path) is not { } proof) return Results.Unauthorized();
        try
        {
            var command = JsonSerializer.Deserialize<CapacityReservationV1>(request.Body, EntitlementSnapshotsV1.Json);
            if (command?.IsValid(phase) != true) return Results.BadRequest();
            var result = await receiver.ReceiveAsync(command, request.Body, proof, context.RequestAborted);
            return !result.IsError ? Results.NoContent() : result.FirstError.Type switch
            {
                ErrorType.Unauthorized => Results.Unauthorized(),
                ErrorType.Conflict when result.FirstError.Code != LicenceErrors.BusyCode => Results.Conflict(),
                _ => Results.StatusCode(503),
            };
        }
        catch (JsonException) { return Results.BadRequest(); }
        catch (NpgsqlException) { return Results.StatusCode(503); }
    }

    private static async Task<IResult> ObserveReductionAsync(HttpContext context, MerchantEntitlementTransport transport,
        IReductionReservationReceiver receiver)
    {
        var path = ReductionTransportV1.ObservePath;
        var ct = context.RequestAborted;
        if (await PrivateIntegrationRequest.ReadAsync(context, path, ReductionTransportV1.BodyLimit) is not { } request ||
            transport.Verify(request.Token, request.Body, path) is not { } proof)
            return Results.Unauthorized();
        try
        {
            var input = JsonSerializer.Deserialize<ReductionObservationRequestV1>(request.Body, EntitlementSnapshotsV1.Json);
            if (input?.IsValid() != true) return Results.BadRequest();
            var result = await receiver.ObserveAsync(input, proof, ct);
            if (result.IsError)
                return result.FirstError.Type == ErrorType.Unauthorized ? Results.Unauthorized() : Results.StatusCode(503);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(result.Value, EntitlementSnapshotsV1.Json);
            if (bytes.Length > ReductionTransportV1.BodyLimit ||
                await transport.SignReductionObservationAsync(bytes, ct) is not { } signature)
                return Results.StatusCode(503);
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers[ReductionTransportV1.ProofHeader] = signature;
            return Results.Bytes(bytes, "application/json");
        }
        catch (JsonException) { return Results.BadRequest(); }
        catch (NpgsqlException) { return Results.StatusCode(503); }
    }

    private static async Task<IResult> ReductionManifestAsync(HttpContext context, MerchantEntitlementTransport transport,
        IReductionReservationReceiver receiver)
    {
        var path = ReductionTransportV1.ManifestPath;
        if (await PrivateIntegrationRequest.ReadAsync(context, path, ReductionTransportV1.BodyLimit) is not { } request ||
            transport.Verify(request.Token, request.Body, path) is not { } proof)
            return Results.Unauthorized();
        try
        {
            var page = JsonSerializer.Deserialize<ReductionManifestPageV1>(request.Body, EntitlementSnapshotsV1.Json);
            if (page?.IsValid() != true) return Results.BadRequest();
            return MapInternalWrite(await receiver.ReceiveManifestAsync(page, request.Body, proof, context.RequestAborted));
        }
        catch (JsonException) { return Results.BadRequest(); }
        catch (NpgsqlException) { return Results.StatusCode(503); }
    }

    private static async Task<IResult> ReductionReservationAsync(HttpContext context, MerchantEntitlementTransport transport,
        IReductionReservationReceiver receiver, string phase, string path)
    {
        if (await PrivateIntegrationRequest.ReadAsync(context, path, ReductionTransportV1.BodyLimit) is not { } request ||
            transport.Verify(request.Token, request.Body, path) is not { } proof)
            return Results.Unauthorized();
        try
        {
            var command = JsonSerializer.Deserialize<ReductionReservationV1>(request.Body, EntitlementSnapshotsV1.Json);
            if (command?.IsValid(phase) != true) return Results.BadRequest();
            return MapInternalWrite(await receiver.ReceiveReservationAsync(command, request.Body, proof, context.RequestAborted));
        }
        catch (JsonException) { return Results.BadRequest(); }
        catch (NpgsqlException) { return Results.StatusCode(503); }
    }

    private static async Task<IResult> CoverageExceptionAsync(HttpContext context, MerchantEntitlementTransport transport,
        ICoverageExceptionReceiver receiver, string phase, string path)
    {
        if (await PrivateIntegrationRequest.ReadAsync(context, path, EntitlementSnapshotsV1.BodyLimit) is not { } request ||
            transport.Verify(request.Token, request.Body, path) is not { } proof)
            return Results.Unauthorized();
        try
        {
            var command = JsonSerializer.Deserialize<CoverageExceptionCommandV1>(request.Body, EntitlementSnapshotsV1.Json);
            if (command?.IsValid(phase) != true) return Results.BadRequest();
            return MapInternalWrite(await receiver.ReceiveAsync(command, request.Body, proof, context.RequestAborted));
        }
        catch (JsonException) { return Results.BadRequest(); }
        catch (NpgsqlException) { return Results.StatusCode(503); }
    }

    private static IResult MapInternalWrite(ErrorOr<Success> result) => !result.IsError ? Results.NoContent()
        : result.FirstError.Type switch
        {
            ErrorType.Unauthorized => Results.Unauthorized(),
            ErrorType.Conflict when result.FirstError.Code != LicenceErrors.BusyCode => Results.Conflict(),
            _ => Results.StatusCode(503),
        };

    private static async Task<IResult> ValidateClosureAsync(HttpContext context, MerchantEntitlementTransport transport,
        IMerchantClosureValidationReceiver consents)
    {
        var path = ShopClosuresV1.ValidationPath;
        var ct = context.RequestAborted;
        if (await PrivateIntegrationRequest.ReadAsync(context, path, ShopClosuresV1.BodyLimit) is not { } request ||
            transport.Verify(request.Token, request.Body, path) is not { } proof)
            return Results.Unauthorized();
        try
        {
            var input = JsonSerializer.Deserialize<ClosureValidationV1>(request.Body, EntitlementSnapshotsV1.Json);
            if (input is null) return Results.BadRequest();
            var validated = await consents.ValidateAsync(input, proof, ct);
            if (validated.IsError)
                return validated.FirstError.Code == ShopClosuresV1.ReviewChangedCode
                    ? Results.Problem(statusCode: 409, extensions: new Dictionary<string, object?>
                        { ["code"] = ShopClosuresV1.ReviewChangedCode })
                    : Results.StatusCode(503);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(validated.Value, EntitlementSnapshotsV1.Json);
            var signature = await transport.SignClosureValidationAsync(bytes, ct);
            if (signature is null) return Results.StatusCode(503);
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers[ShopClosuresV1.ProofHeader] = signature;
            return Results.Bytes(bytes, "application/json");
        }
        catch (JsonException)
        {
            return Results.BadRequest();
        }
    }

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
            if (result.FirstError.Code is CoverageReplacementsV1.AwaitingRelease or CoverageReplacementExceptionsV1.AwaitingRelease)
                return Results.StatusCode(StatusCodes.Status423Locked);
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
