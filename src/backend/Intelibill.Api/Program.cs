using System.Text;
using Intelibill.Api.Extensions;
using Intelibill.Api.Middleware;
using Intelibill.Api.Options;
using Intelibill.Application;
using Intelibill.Application.Common.Behaviours;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Infrastructure;
using Scalar.AspNetCore;
using Intelibill.Infrastructure.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Wolverine;

var builder = WebApplication.CreateBuilder(args);

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
                "http://localhost:4200", "https://localhost:4200",  // local dev (ng serve)
                "http://localhost:4000", "https://localhost:4000"   // Docker frontend
            )
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// ── App options ───────────────────────────────────────────────────────────────
builder.Services.AddOptions<AppOptions>()
    .Bind(builder.Configuration.GetSection(AppOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// ── JWT Authentication ────────────────────────────────────────────────────────
// Configure JwtBearerOptions via the Options Pattern so the JWT secret comes
// from the validated JwtOptions rather than raw IConfiguration.
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
});

// ── Wolverine ─────────────────────────────────────────────────────────────────
builder.Services.AddScoped<ValidationMiddleware>();

builder.Host.UseWolverine(opts =>
{
    opts.Discovery.IncludeAssembly(typeof(Intelibill.Application.DependencyInjection).Assembly);
    // ValidationMiddleware is registered as scoped - Wolverine will auto-discover its BeforeAsync method
});

var app = builder.Build();

// Apply migrations
await app.Services.ApplyMigrationsAsync();

app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();
app.UseCors("FrontendDev");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program;
