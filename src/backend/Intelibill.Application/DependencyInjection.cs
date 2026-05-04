using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Inventory.Services;
using Intelibill.Application.Features.Items.Services;
using Intelibill.Application.Features.Sales.Services;
using Intelibill.Application.Features.Sales.Services.Returns;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);
        services.AddScoped<IItemCatalogStreamingService, ItemCatalogStreamingService>();

        services.AddScoped<IItemResolver, ItemResolver>();
        services.AddScoped<ISupplierResolver, SupplierResolver>();
        services.AddScoped<IBatchFactory, BatchFactory>();
        services.AddScoped<IInventoryUpdater, InventoryUpdater>();

        services.AddScoped<ISaleLineValidator, SaleLineValidator>();
        services.AddScoped<ISaleInventoryMutator, SaleInventoryMutator>();
        services.AddScoped<ICustomerResolver, CustomerResolver>();
        services.AddScoped<ISaleAggregator, SaleAggregator>();
        services.AddScoped<ISaleReturnNumberGenerator, SaleReturnNumberGenerator>();
        services.AddScoped<ISaleReturnCalculator, SaleReturnCalculator>();

        return services;
    }
}
