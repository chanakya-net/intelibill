using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Discounts.Services;
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
        services.AddScoped<IInventoryAdjustmentNumberGenerator, InventoryAdjustmentNumberGenerator>();

        services.AddScoped<ISaleLineValidator, SaleLineValidator>();
        services.AddScoped<ICustomerResolver, CustomerResolver>();
        services.AddScoped<ISaleReturnNumberGenerator, SaleReturnNumberGenerator>();
        services.AddScoped<ISaleReturnCalculator, SaleReturnCalculator>();
        services.AddScoped<ISaleReturnValidator, SaleReturnValidator>();

        services.AddScoped<DiscountRuleValidationService>();

        return services;
    }
}
