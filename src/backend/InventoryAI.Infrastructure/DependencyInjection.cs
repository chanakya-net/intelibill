using InventoryAI.Application.Common.Interfaces;
using InventoryAI.Domain.Interfaces;
using InventoryAI.Domain.Interfaces.Repositories;
using InventoryAI.Infrastructure.Data;
using InventoryAI.Infrastructure.Options;
using InventoryAI.Infrastructure.Repositories;
using InventoryAI.Infrastructure.Services.Auth;
using InventoryAI.Infrastructure.Services.Auth.ExternalAuth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace InventoryAI.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // ── Database ────────────────────────────────────────────────────────
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            var dbOptions = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            options.UseNpgsql(dbOptions.ToConnectionString(), npgsql =>
                npgsql.MigrationsAssembly(typeof(DependencyInjection).Assembly.FullName))
                .UseSnakeCaseNamingConvention();
        });

        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // ── Auth options ─────────────────────────────────────────────────────
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ExternalAuthOptions>()
            .Bind(configuration.GetSection(ExternalAuthOptions.SectionName));

        // ── Repositories ─────────────────────────────────────────────────────
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IPasswordResetTokenRepository, PasswordResetTokenRepository>();

        // ── Auth services ─────────────────────────────────────────────────────
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IPasswordHasher, BcryptPasswordHasher>();
        services.AddSingleton<IEmailService, NoOpEmailService>();

        // ── External auth providers ───────────────────────────────────────────
        // Named HttpClients for providers that call external HTTP APIs.
        services.AddHttpClient(nameof(FacebookAuthProvider));
        services.AddHttpClient(nameof(TwitterAuthProvider));

        // All providers are registered as IExternalAuthProvider so the handler can
        // resolve IEnumerable<IExternalAuthProvider> and pick the right one.
        services.AddScoped<IExternalAuthProvider, GoogleAuthProvider>();
        services.AddScoped<IExternalAuthProvider, MicrosoftAuthProvider>();
        services.AddScoped<IExternalAuthProvider, FacebookAuthProvider>();
        services.AddScoped<IExternalAuthProvider, TwitterAuthProvider>();
        services.AddScoped<IExternalAuthProvider, AppleAuthProvider>();

        return services;
    }
}
