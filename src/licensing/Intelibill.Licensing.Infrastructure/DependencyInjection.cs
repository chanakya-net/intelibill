using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.Bootstrap;
using Intelibill.Licensing.Infrastructure.AdminIdentities;
using Intelibill.Licensing.Infrastructure.Bootstrap;
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
        services.AddSingleton<IAdminInvitationMailer, SmtpAdminInvitationMailer>();
        services.AddScoped<IAdminInvitationStore, AdminInvitationStore>();
        return services.AddBootstrapPersistence();
    }

    public static IServiceCollection AddBootstrapPersistence(this IServiceCollection services)
    {
        services.AddScoped<IBootstrapStore, BootstrapStore>();
        services.AddSingleton<IBootstrapSecrets, BootstrapSecrets>();
        services.AddSingleton<IPasswordHashing, BcryptPasswordHashing>();
        services.AddSingleton<OpaqueTokenPrimitives>();
        return services;
    }
}
