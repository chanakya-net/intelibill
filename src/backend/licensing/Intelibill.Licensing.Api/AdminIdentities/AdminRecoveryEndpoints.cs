using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ErrorOr;
using Intelibill.Licensing.Api.Audit;
using Intelibill.Licensing.Api.Bootstrap;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.Bootstrap;
using Intelibill.Licensing.Domain.Entities;
using Microsoft.AspNetCore.Http.Features;

namespace Intelibill.Licensing.Api.AdminIdentities;

/// <summary>
/// Recovery-code and reviewed-reset redemption (anonymous, password plus one-use proof) and the
/// owner's reviewed reset. Origin and CSRF precede the budget, the budget precedes any credential check.
/// </summary>
internal static partial class AdminRecoveryEndpoints
{
    internal const string RecoveryPath = "/api/admin/auth/recovery";
    internal const string ResetPath = "/api/admin/recovery/resets";
    private const int MaxRequestBytes = 4096;

    internal static void MapAdminRecovery(this WebApplication app)
    {
        var recovery = app.MapGroup(RecoveryPath).AllowAnonymous();
        recovery.MapPost("/code", (HttpContext context, AdminRecoveryHandler handler, AdminSignInHttp http,
            IAdminSignInBudget budget, TimeProvider clock, ILogger<AdminRecoveryHandler> logger) =>
            RedeemAsync(context, http, budget, clock, logger, request =>
                handler.RedeemCodeAsync(request?.Email, request?.Password, request?.RecoveryCode,
                    request?.ContinuationKey, CancellationToken.None)));
        recovery.MapPost("/reset", (HttpContext context, AdminRecoveryHandler handler, AdminSignInHttp http,
            IAdminSignInBudget budget, TimeProvider clock, ILogger<AdminRecoveryHandler> logger) =>
            RedeemAsync(context, http, budget, clock, logger, request =>
                handler.RedeemResetAsync(request?.Email, request?.Password, request?.ResetToken, CancellationToken.None)));
        recovery.MapPost("/continue", (HttpContext context, AdminRecoveryHandler handler, AdminSignInHttp http,
            IAdminSignInBudget budget, TimeProvider clock, ILogger<AdminRecoveryHandler> logger) =>
            RedeemAsync(context, http, budget, clock, logger, request =>
                handler.ContinueEnrollmentAsync(request?.Email, request?.Password, request?.ContinuationKey, CancellationToken.None)));
        app.MapPost(ResetPath, ReviewAsync).RequireAuthorization(ProviderSessions.RecoveryPolicy)
            .WithMetadata(new AuditDenial(BootstrapAuditAction.RecoveryResetDenied));
    }

    private static async Task<IResult> RedeemAsync(HttpContext context, AdminSignInHttp http, IAdminSignInBudget budget,
        TimeProvider clock, ILogger logger, Func<RedeemRequest?, Task<ErrorOr<EnrollmentGrant>>> redeem)
    {
        var origin = await http.ReadOriginAsync(context);
        if (origin is null)
            return BootstrapHttp.Problem(context, 503, "Admin.SignInUnavailable", "Provider sign-in is unavailable.");
        if (!http.AcceptsMutation(context, origin)) return InvitationRequestGate.Refused(context);
        if (Oversized(context) is { } tooLarge) return tooLarge;

        var request = await ReadAsync<RedeemRequest>(context);
        var now = clock.GetUtcNow();
        var subject = "recovery:" + (BootstrapValidation.NormalizeEmail(request?.Email) ?? "");
        if (await budget.TakeAsync(Digest(subject), now, context.RequestAborted) is { } retryAt)
            return BootstrapHttp.Throttled(context, retryAt, now, "Admin.Throttled");

        // Once accepted, the attempt completes even if the browser disconnects: a consumed proof is never lost.
        var result = await redeem(request);
        if (result.IsError) return Problem(context, result.FirstError, logger);
        LogRecovered(logger);
        AdminSignInHttp.ClearSession(context, origin);
        return BootstrapHttp.Enrollment(context, origin, result.Value);
    }

    private static async Task<IResult> ReviewAsync(HttpContext context, AdminRecoveryHandler handler, AdminSignInHttp http,
        IAdminSignInBudget budget, TimeProvider clock, ILogger<AdminRecoveryHandler> logger)
    {
        var origin = await http.ReadOriginAsync(context);
        var caller = ProviderSessions.ReadCaller(context.User);
        if (origin is null || caller is null || !http.AcceptsMutation(context, origin))
        {
            await AdminAuditAccessDeniedHandler.RecordAsync(context, BootstrapAuditAction.RecoveryResetDenied, AuditReason.RequestRejected);
            return InvitationRequestGate.Refused(context);
        }
        if (Oversized(context) is { } tooLarge) return tooLarge;
        var now = clock.GetUtcNow();
        if (await budget.TakeAsync(Digest("recovery-review:" + caller.AdminId), now, context.RequestAborted) is { } retryAt)
            return BootstrapHttp.Throttled(context, retryAt, now, "Admin.Throttled");

        var request = await ReadAsync<ReviewRequest>(context);
        var review = new AdminRecoveryReview(request?.Email, request?.Reason, request?.Verification,
            request?.EvidenceReference, request?.Confirmed);
        var result = await handler.ReviewResetAsync(caller, review, CancellationToken.None);
        if (result.IsError) return Problem(context, result.FirstError, logger);
        LogReviewed(logger, result.Value.RecoveryId);
        return Results.Json(new
        {
            recoveryId = result.Value.RecoveryId,
            resetToken = result.Value.ResetToken,
            expiresAt = result.Value.ExpiresAt,
        }, statusCode: StatusCodes.Status201Created);
    }

    private static IResult? Oversized(HttpContext context)
    {
        if (context.Request.ContentLength > MaxRequestBytes)
            return BootstrapHttp.Problem(context, 413, "Admin.RequestTooLarge", "The request is too large.");
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } size)
            size.MaxRequestBodySize = MaxRequestBytes;
        return null;
    }

    private static string Digest(string subject) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(subject)));

    private static IResult Problem(HttpContext context, Error error, ILogger logger)
    {
        LogRefused(logger, error.Code);
        return BootstrapHttp.Problem(context, error.Type switch
        {
            ErrorType.Unauthorized => 401,
            ErrorType.Forbidden => 403,
            ErrorType.NotFound => 404,
            ErrorType.Conflict => 409,
            ErrorType.Failure => 503,
            _ => 400,
        }, error.Code, error.Description);
    }

    private static async Task<T?> ReadAsync<T>(HttpContext context) where T : class
    {
        if (!context.Request.HasJsonContentType()) return null;
        try
        {
            return await context.Request.ReadFromJsonAsync<T>(context.RequestAborted);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Outcome codes and identifiers only: never the email, password, code, token or evidence.
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Admin recovery accepted; MFA enrollment pending.")]
    private static partial void LogRecovered(ILogger logger);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Admin recovery reset {RecoveryId} reviewed.")]
    private static partial void LogReviewed(ILogger logger, Guid recoveryId);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Admin recovery request refused ({Outcome}).")]
    private static partial void LogRefused(ILogger logger, string outcome);

    private sealed class RedeemRequest
    {
        public string? Email { get; init; }
        public string? Password { get; init; }
        public string? RecoveryCode { get; init; }
        public string? ResetToken { get; init; }
        public string? ContinuationKey { get; init; }
    }

    private sealed class ReviewRequest
    {
        public string? Email { get; init; }
        public string? Reason { get; init; }
        public string? Verification { get; init; }
        public string? EvidenceReference { get; init; }
        public bool? Confirmed { get; init; }
    }
}
