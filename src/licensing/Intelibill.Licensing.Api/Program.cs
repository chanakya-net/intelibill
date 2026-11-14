using Intelibill.Licensing.Application;
using Intelibill.Licensing.Infrastructure;
using Intelibill.Licensing.Infrastructure.Data;

namespace Intelibill.Licensing.Api;

public sealed class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddLicensingApplication();
        builder.Services.AddLicensingInfrastructure(builder.Configuration);

        var app = builder.Build();
        app.MapGet("/health/live", (HttpContext context) => Probe(context, "live")).AllowAnonymous();
        app.MapGet("/health/ready", async (HttpContext context, LicensingDatabaseReadiness database) =>
        {
            var ready = await database.IsReadyAsync(context.RequestAborted);
            return ready ? Probe(context, "ready") : Probe(context, "unavailable", StatusCodes.Status503ServiceUnavailable);
        }).AllowAnonymous();

        app.Run();
    }

    private static IResult Probe(HttpContext context, string status, int statusCode = StatusCodes.Status200OK)
    {
        context.Response.Headers.CacheControl = "no-store";
        return Results.Json(new { service = "intelibill-licensing", status }, statusCode: statusCode);
    }
}
