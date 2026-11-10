using HsnFinder.Api.Configuration;
using HsnFinder.Api.Middleware;
using HsnFinder.Api.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Polly;
using Polly.Retry;
using Scalar.AspNetCore;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.secrets.json", optional: true, reloadOnChange: true);
builder.Configuration.AddUserSecrets<Program>(optional: true, reloadOnChange: true);

builder.Services.AddControllers();
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.SuppressModelStateInvalidFilter = true;
});
builder.Services.AddOpenApi();
builder.Services.AddMemoryCache();

builder.Services.Configure<AzureAiSettings>(builder.Configuration.GetSection("AzureAi"));
builder.Services.Configure<ApiKeySettings>(builder.Configuration.GetSection("ApiKey"));
builder.Services.Configure<CacheSettings>(builder.Configuration.GetSection("Cache"));
builder.Services.Configure<RateLimitSettings>(builder.Configuration.GetSection("RateLimit"));

var rateLimitSettings = builder.Configuration.GetSection("RateLimit").Get<RateLimitSettings>()
    ?? new RateLimitSettings { PermitLimit = 10, WindowSeconds = 60 };
var permitLimit = rateLimitSettings.PermitLimit > 0 ? rateLimitSettings.PermitLimit : 10;
var windowSeconds = rateLimitSettings.WindowSeconds > 0 ? rateLimitSettings.WindowSeconds : 60;

builder.Services.AddRateLimiter(options =>
{
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsync("Rate limit exceeded. Try again later.", token);
    };

    options.AddPolicy("api-key-policy", context =>
    {
        var apiKey = context.Request.Headers["X-Api-Key"].ToString();

        return RateLimitPartition.GetFixedWindowLimiter(
            apiKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromSeconds(windowSeconds),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            });
    });
});

builder.Services.AddSingleton(sp =>
{
    var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("AzureAiResilience");
    var pipelineBuilder = new ResiliencePipelineBuilder();

    pipelineBuilder.AddRetry(new RetryStrategyOptions
    {
        MaxRetryAttempts = 2,
        BackoffType = DelayBackoffType.Exponential,
        OnRetry = args =>
        {
            logger.LogWarning("Retrying Azure AI call, attempt {Attempt}", args.AttemptNumber + 1);
            return ValueTask.CompletedTask;
        }
    });

    return pipelineBuilder.Build();
});

builder.Services.AddScoped<IHsnLookupService, HsnLookupService>();
builder.Services.AddTransient<ApiKeyMiddleware>();

var app = builder.Build();

app.MapOpenApi();
app.MapScalarApiReference();

app.UseHttpsRedirection();
app.UseApiKeyAuthentication();
app.UseRateLimiter();
app.MapControllers();

app.Run();
