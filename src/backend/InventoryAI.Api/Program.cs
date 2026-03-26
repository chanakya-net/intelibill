using System.Text;
using InventoryAI.Api.Middleware;
using InventoryAI.Api.Options;
using InventoryAI.Application;
using InventoryAI.Infrastructure;
using Scalar.AspNetCore;
using InventoryAI.Infrastructure.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Wolverine;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

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

builder.Services.AddAuthorization();

// ── Wolverine ─────────────────────────────────────────────────────────────────
builder.Host.UseWolverine(opts =>
{
    opts.Discovery.IncludeAssembly(typeof(InventoryAI.Application.DependencyInjection).Assembly);
});

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
