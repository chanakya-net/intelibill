using System.Text;
using Intelibill.Api.Extensions;
using Intelibill.Api.Hubs;
using Intelibill.Api.Middleware;
using Intelibill.Api.Middleware.RateLimiting;
using Intelibill.Api.Options;
using Intelibill.Api.Services;
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
builder.Services.AddControllers(options => { options.Filters.AddService<RateLimitFilter>(); })
    .AddJsonOptions(opts =>
        opts.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentSessionContext, HttpCurrentSessionContext>();
builder.Services.AddScoped<RateLimitFilter>();
builder.Services.AddSingleton<IRateLimitPolicyResolver, RateLimitPolicyResolver>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendDev", policy =>
    {
        policy
            .WithOrigins(
                "http://localhost:4200", "https://localhost:4200",
                "http://localhost:4000", "https://localhost:4000")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddApplication();
builder.Services.AddInfrastructure(configuration);
builder.Services.AddSignalR();
builder.Services.AddScoped<IProductHubNotifier, SignalRProductHubNotifier>();
builder.Services.AddScoped<IShopUpdatesNotifier, SignalRShopUpdatesNotifier>();
builder.Services.AddWolverineHttp();

// ── App options ───────────────────────────────────────────────────────────────
builder.Services.AddOptions<AppOptions>()
    .Bind(configuration.GetSection(AppOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<RateLimitingOptions>()
    .Bind(configuration.GetSection(RateLimitingOptions.SectionName))
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

        bearerOptions.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                // SignalR's WebSocket and SSE transports cannot set an Authorization
                // header, so the client passes the token in the query string. Accept
                // it on hub paths only — anywhere else a token in the URL would end
                // up in access logs and referrers.
                var accessToken = context.Request.Query["access_token"].ToString();
                if (!string.IsNullOrEmpty(accessToken)
                    && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            },
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

    options.AddPolicy("OwnerManagerOrStaff", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("active_shop_role", "Owner", "Manager", "Staff");
    });
});

// ── Wolverine ─────────────────────────────────────────────────────────────────
// ExtensionDiscovery.ManualOnly: automatic discovery probes every file in the
// output directory for [WolverineModule], including the Windows native binaries
// that QuestPDF and SkiaSharp ship (runtimes/win-*/native/*.dll), and logs a
// banner for each one it cannot load. Nothing here relies on a discovered
// extension — FluentValidation and HTTP are both registered explicitly below and
// in AddWolverineHttp — so the scan produced noise and startup delay and no
// behaviour.
builder.Services.AddWolverine(ExtensionDiscovery.ManualOnly, opts =>
{
    opts.Discovery.IncludeAssembly(typeof(Intelibill.Application.DependencyInjection).Assembly);
    opts.UseFluentValidation(RegistrationBehavior.ExplicitRegistration);
    // ErrorOrResultMiddleware: IErrorOr-typed After() params are not resolvable in
    // WolverineFx 5.24.0 without a custom variable source — omitted until redesigned.
    opts.Policies.AddMiddleware<W3CInboundEnvelopeMiddleware>();
    opts.Policies.AllSenders(s => s.CustomizeOutgoing(new W3COutboundEnvelopeModifier().Modify));
});

var app = builder.Build();

// Schema is owned by the migration job, not by application startup. Migrating
// here would need DDL rights at runtime, would race between replicas, and would
// apply the new schema while the previous revision is still serving traffic.
// Local setup: dotnet ef database update (see CLAUDE.md).

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
app.MapHub<ProductHub>("/hubs/products");
app.MapHub<ShopUpdatesHub>("/hubs/shop-updates");

app.Run();

public partial class Program;
