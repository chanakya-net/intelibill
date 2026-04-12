using System.Text;
using Intelibill.Api.Extensions;
using Intelibill.Api.Middleware;
using Intelibill.Api.Options;
using Intelibill.Application;
using Intelibill.Application.Common.Behaviours;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Infrastructure;
using Intelibill.Infrastructure.Extensions;
using Scalar.AspNetCore;
using Intelibill.Infrastructure.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Wolverine;
using Wolverine.FluentValidation;
using Wolverine.Http;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

// ── Observability — must precede AddInteliBillSerilog so IOptions<ObservabilityOptions>
//    is resolvable when Serilog's UseSerilog callback fires at host build time.
builder.Services.AddObservabilityOptions(configuration);
builder.AddInteliBillSerilog();
builder.Services.AddInteliBillOpenTelemetry(configuration);

// ── Core services ─────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentSessionContext, HttpCurrentSessionContext>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendDev", policy =>
    {
        policy
            .WithOrigins(
                "http://localhost:4200", "https://localhost:4200",
                "http://localhost:4000", "https://localhost:4000")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddApplication();
builder.Services.AddInfrastructure(configuration);
builder.Services.AddWolverineHttp();

// ── App options ───────────────────────────────────────────────────────────────
builder.Services.AddOptions<AppOptions>()
    .Bind(configuration.GetSection(AppOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// ── JWT Authentication ────────────────────────────────────────────────────────
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((bearerOptions, jwtOptions) =>
    {
        var jwt = jwtOptions.Value;
        bearerOptions.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)),
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("OwnerOnly", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("active_shop_role", "Owner");
    });

    options.AddPolicy("OwnerOrManager", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("active_shop_role", "Owner", "Manager");
    });
});

// ── Wolverine ─────────────────────────────────────────────────────────────────
builder.Services.AddWolverine(opts =>
{
    opts.Discovery.IncludeAssembly(typeof(Intelibill.Application.DependencyInjection).Assembly);
    opts.UseFluentValidation();
    // ErrorOrResultMiddleware: IErrorOr-typed After() params are not resolvable in
    // WolverineFx 5.24.0 without a custom variable source — omitted until redesigned.
    opts.Policies.AddMiddleware<W3CInboundEnvelopeMiddleware>();
    opts.Policies.AllSenders(s => s.CustomizeOutgoing(new W3COutboundEnvelopeModifier().Modify));
});

var app = builder.Build();

await app.Services.ApplyMigrationsAsync();

app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();
app.UseCors("FrontendDev");
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<JwtContextEnrichmentMiddleware>();
app.UseSerilogRequestLogging(SerilogExtensions.RequestLoggingOptions);
app.MapControllers();
app.MapWolverineEndpoints();

app.Run();

public partial class Program;
