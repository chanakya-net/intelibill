using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.Bootstrap;
using Intelibill.Licensing.Infrastructure.AdminIdentities;
using Intelibill.Licensing.Infrastructure.Bootstrap;
using Intelibill.Licensing.Infrastructure.AdminIdentities.Mfa;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Licensing.Infrastructure.Options;
using Intelibill.Shared.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Intelibill.Licensing.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddLicensingInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<LicensingDatabaseOptions>()
            .Bind(configuration.GetSection(LicensingDatabaseOptions.SectionName))
            .Validate(settings => settings.IsValid(), "LicensingDatabase settings are missing or invalid.")
            .ValidateOnStart();

        services.AddSingleton(provider =>
            NpgsqlDataSource.Create(provider.GetRequiredService<IOptions<LicensingDatabaseOptions>>()
                .Value.ToConnectionString()));
        services.AddDbContext<LicensingDbContext>((provider, options) => options
            .UseNpgsql(provider.GetRequiredService<NpgsqlDataSource>(),
                npgsql => npgsql.MigrationsAssembly(typeof(LicensingDbContext).Assembly.GetName().Name))
            .UseSnakeCaseNamingConvention());
        services.AddSingleton<LicensingDatabaseReadiness>();
        services.AddSingleton<IBootstrapRateBudget, PostgresBootstrapRateBudget>();
        services.AddOptions<AdminMailOptions>().Bind(configuration.GetSection(AdminMailOptions.SectionName));
        services.AddOptions<AdminMfaOptions>().Bind(configuration.GetSection("AdminMfa"))
            .Validate(options => options.IsValid(), "Admin MFA key ring is invalid.").ValidateOnStart();
        services.AddSingleton<AdminMfaKeys>();
        services.AddScoped<IAdminSignInBudget, PostgresAdminSignInBudget>();
        services.AddScoped<IAdminSessionStore, AdminSessionStore>();
        services.AddScoped<AdminRefreshCredentialService>();
        services.AddScoped<IAdminRefreshCredentials>(provider => provider.GetRequiredService<AdminRefreshCredentialService>());
        services.AddScoped<AdminMfaStore>();
        services.AddScoped<IAdminMfaEnrollment, AdminMfaEnrollmentService>();
        services.AddScoped<IAdminMfaVerifier, AdminMfaVerifier>();
        return services.AddBootstrapPersistence();
    }

    public static IServiceCollection AddBootstrapPersistence(this IServiceCollection services)
    {
        services.AddScoped<IBootstrapStore, BootstrapStore>();
        // The bootstrap CLI host validates the whole application graph, invitation handlers
        // included. Without a bound AdminMail section the mailer stays unusable (fail closed).
        services.AddScoped<IAdminInvitationStore, AdminInvitationStore>();
        services.AddSingleton<IAdminInvitationMailer, SmtpAdminInvitationMailer>();
        services.AddSingleton<IBootstrapSecrets, BootstrapSecrets>();
        services.AddSingleton<IPasswordHashing, BcryptPasswordHashing>();
        services.AddSingleton<OpaqueTokenPrimitives>();
        return services;
    }
}
