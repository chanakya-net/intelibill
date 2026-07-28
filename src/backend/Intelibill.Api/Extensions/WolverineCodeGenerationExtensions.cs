using JasperFx;
using JasperFx.CodeGeneration;

namespace Intelibill.Api.Extensions;

internal static class WolverineCodeGenerationExtensions
{
    public static IServiceCollection AddProductionWolverineCodeGeneration(
        this IServiceCollection services)
    {
        services.CritterStackDefaults(ConfigureForProduction);
        return services;
    }

    internal static void ConfigureForProduction(JasperFxOptions options)
    {
        options.Production.GeneratedCodeMode = TypeLoadMode.Static;
        options.Production.AssertAllPreGeneratedTypesExist = true;
    }
}
