using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Items.Services;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);
        services.AddScoped<IItemCatalogStreamingService, ItemCatalogStreamingService>();

        return services;
    }
}
