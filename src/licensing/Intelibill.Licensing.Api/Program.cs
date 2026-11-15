using Intelibill.Licensing.Api.AdminIdentities;
using Intelibill.Licensing.Api.Audit;
using Intelibill.Licensing.Api.Bootstrap;
using Intelibill.Licensing.Api.Observability;
using Intelibill.Licensing.Application;
using Intelibill.Licensing.Infrastructure;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Web;
using Intelibill.Shared.Web.Middleware;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authorization;

namespace Intelibill.Licensing.Api;

public sealed class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args is ["bootstrap-admin", ..])
                return await BootstrapAdminCommand.RunAsync(args);

            await BuildApplication(args).RunAsync();
            return 0;
        }
        // Host discovery (for example, WebApplicationFactory) uses HostAbortedException to stop the entry point.
        catch (Exception exception) when (exception is not HostAbortedException)
        {
            // Host construction can fail before Serilog is available. Keep the process fatal
            // without letting the runtime print the exception and its configuration values.
            Console.Error.WriteLine($"Licensing host failed ({exception.GetType().Name}).");
            return 1;
        }
    }

    private static WebApplication BuildApplication(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddLicensingSerilog();
        builder.Services.AddLicensingTelemetry();
        builder.Services.AddSafeErrorHandling();
        builder.Services.AddLicensingApplication();
        builder.Services.AddLicensingInfrastructure(builder.Configuration);

        builder.Services.AddBootstrapAuthorization();
        builder.Services.AddScoped<BootstrapRequestGate>();
        builder.Services.AddProviderSessions(builder.Environment);
        builder.Services.AddScoped<InvitationRequestGate>();
        builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, AdminAuditAccessDeniedHandler>();
        var protection = builder.Services.AddDataProtection().SetApplicationName("InteliBill.Licensing.Admin");
        if (builder.Configuration["AdminBootstrap:DataProtectionKeyDirectory"] is { Length: > 0 } keyDirectory)
            protection.PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));
        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-Bootstrap-CSRF";
            options.SuppressReadingTokenFromFormBody = true;
            options.Cookie.Name = BootstrapHttp.CsrfCookieName;
            options.Cookie.Path = BootstrapHttp.Path;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });

        var app = builder.Build();
        // First, so every failure — probes included — answers the shared safe envelope.
        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.MapGet("/health/live", (HttpContext context) => Probe(context, "live")).AllowAnonymous();
        app.MapGet("/health/ready", async (HttpContext context, LicensingDatabaseReadiness database) =>
        {
            var ready = await database.IsReadyAsync(context.RequestAborted);
            return ready ? Probe(context, "ready") : Probe(context, "unavailable", StatusCodes.Status503ServiceUnavailable);
        }).AllowAnonymous();

        app.UseRouting();
        app.UseMiddleware<AdminRequestBoundary>();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapBootstrap();
        app.MapInvitations();
        app.MapAdminMfa();
        app.MapAdminSignIn();
        app.MapAdminRefresh();
        app.MapAdminSessions();
        app.MapAdminRecovery();
        app.MapAdminAudit();
        var business = app.MapGroup("/api/admin").RequireAuthorization(BootstrapAuthorization.BusinessPolicy);
        business.MapGet("/access", () => Results.NoContent());
        return app;
    }

    private static IResult Probe(HttpContext context, string status, int statusCode = StatusCodes.Status200OK)
    {
        context.Response.Headers.CacheControl = "no-store";
        return Results.Json(new { service = "intelibill-licensing", status }, statusCode: statusCode);
    }
}
