using Intelibill.Shared.Web.Logging;
using Intelibill.Shared.Web.PublicErrors;

namespace Intelibill.Shared.Web;

public static class DependencyInjection
{
    /// <summary>
    /// Registers what <see cref="Middleware.ExceptionHandlingMiddleware"/> needs to answer a failure
    /// with the public error envelope and record it as a safe diagnostic.
    /// </summary>
    public static IServiceCollection AddSafeErrorHandling(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<ISafeErrorFactory, SafeErrorFactory>();
        services.AddSingleton<ISafeDiagnosticWriter, SafeDiagnosticWriter>();
        return services;
    }
}
