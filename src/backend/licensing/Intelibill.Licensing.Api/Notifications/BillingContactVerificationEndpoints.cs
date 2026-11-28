using System.Text.Json;
using Intelibill.Licensing.Application.Notifications;
using Intelibill.Licensing.Infrastructure.Notifications;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Api.Notifications;

internal static class BillingContactVerificationEndpoints
{
    private const string Path = "/billing-contact/verify";
    private sealed record Submission(Guid AccountId, Guid ContactId, Guid OperationKey,
        string VerifiedEmail, string ProofToken, bool OptedIn, string? Locale);

    internal static void MapBillingContactVerification(this WebApplication app)
    {
        app.MapGet(Path, Show).AllowAnonymous();
        app.MapPost(Path, ConfirmAsync).AllowAnonymous().RequireRateLimiting("contact-proof");
    }

    private static IResult Show(HttpContext http, BillingContactVerificationPage page, string? lang)
    {
        ProtectResponse(http);
        if (!page.Permits($"{http.Request.Scheme}://{http.Request.Host}"))
            return Results.NotFound();
        var (html, nonce) = BillingContactVerificationPage.Render(lang);
        http.Response.Headers.ContentSecurityPolicy =
            $"default-src 'none'; script-src 'nonce-{nonce}'; connect-src 'self'; base-uri 'none'; form-action 'none'; " +
            "frame-ancestors 'none'";
        return Results.Content(html, "text/html; charset=utf-8");
    }

    private static async Task<IResult> ConfirmAsync(
        HttpContext http, BillingContactVerificationPage page, BillingContactConsentHandler handler, CancellationToken ct)
    {
        ProtectResponse(http);
        var request = http.Request;
        if (!page.Permits($"{request.Scheme}://{request.Host}") || !page.Permits(request.Headers.Origin.ToString()))
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (!request.HasJsonContentType() || request.ContentLength is > 2048)
            return Results.BadRequest();
        var buffer = new byte[2049];
        var count = await request.Body.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, ct);
        if (count == buffer.Length)
            return Results.BadRequest();
        Submission? input;
        try
        {
            input = JsonSerializer.Deserialize<Submission>(buffer.AsSpan(0, count), JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            return Results.BadRequest();
        }
        if (input is null || input.ProofToken is not { Length: > 0 and <= 256 })
            return Results.BadRequest();
        var consent = new ConfirmBillingContactConsentRequestV1(
            input.OperationKey, input.VerifiedEmail, input.ProofToken, input.OptedIn, input.Locale);
        var result = await handler.ConfirmStandaloneProofAsync(input.AccountId, input.ContactId, consent, ct);
        return result.IsError ? Results.BadRequest() : Results.NoContent();
    }

    private static void ProtectResponse(HttpContext http)
    {
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers["Referrer-Policy"] = "no-referrer";
        http.Response.Headers.XContentTypeOptions = "nosniff";
    }
}
