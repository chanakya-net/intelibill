using System.Text;
using BarcodeAPI.Api.Authentication;
using BarcodeAPI.Api.Authorization;
using BarcodeAPI.Domain.Interfaces.Clients;
using BarcodeAPI.Domain.Interfaces.Repositories;
using BarcodeAPI.Infrastructure.Data;
using BarcodeAPI.Infrastructure.OpenFoodFacts;
using BarcodeAPI.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Wolverine;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.Configure<OpenFoodFactsOptions>(
    builder.Configuration.GetSection("OpenFoodFacts"));

builder.Services.AddHttpClient<IOpenFoodFactsClient, OpenFoodFactsClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<OpenFoodFactsOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
});

builder.Services.AddAuthentication()
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        var jwt = builder.Configuration.GetSection("Jwt");
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Secret"]!)),
            ValidateIssuer = true,
            ValidIssuer = jwt["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwt["Audience"],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
        };
    })
    .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
        "ApiKey", _ => { });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("DualAuth", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.AddAuthenticationSchemes(
            JwtBearerDefaults.AuthenticationScheme,
            "ApiKey");
        policy.Requirements.Add(new DualAuthRequirement());
    });
});

builder.Services.AddSingleton<IAuthorizationHandler, DualAuthHandler>();

builder.Host.UseWolverine();

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program;
