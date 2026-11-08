using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Auth.Services;
using Intelibill.Application.Features.CreditNotes.Services;
using Intelibill.Application.Features.Discounts.Services;
using Intelibill.Application.Features.Exports.Accounting;
using Intelibill.Application.Features.Exports.Sales;
using Intelibill.Application.Features.Exports.Sales.Services;
using Intelibill.Application.Features.Sales.Queries.GetProfitLossReport;
using Intelibill.Application.Features.Inventory.Services;
using Intelibill.Application.Features.Items.Services;
using Intelibill.Application.Features.OfflineReconciliation;
using Intelibill.Application.Features.OfflineSalesSnapshot.Services;
using Intelibill.Application.Features.Payments.Services;
using Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;
using Intelibill.Application.Features.Sales.Commands.RecordSale;
using Intelibill.Application.Features.Sales.Services;
using Intelibill.Application.Features.Sales.Services.Pricing;
using Intelibill.Application.Features.Sales.Services.Returns;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
namespace Intelibill.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);
        // Registered here rather than in Infrastructure: the session protocol is Application's,
        // and only this assembly can see the internal implementation.
        services.AddScoped<IAuthSessionService, AuthSessionService>();

        services.AddScoped<IItemCatalogStreamingService, ItemCatalogStreamingService>();
        services.AddScoped<IOfflineSalesSnapshotStreamingService, OfflineSalesSnapshotStreamingService>();

        services.AddScoped<Intelibill.Application.Features.SupplierReturns.Services.SupplierReturnService>();
        services.AddScoped<Intelibill.Application.Features.SupplierReturns.Services.SupplierReturnAccess>();
        services.AddScoped<Intelibill.Application.Features.SupplierReturns.Services.PurchaseReceiptReversalPolicy>();
        services.AddScoped<Intelibill.Application.Features.SupplierReturns.Services.PurchaseReceiptReversalService>();
        services.AddScoped<Intelibill.Application.Features.SupplierReturns.Services.SupplierReturnCreditPoster>();

        services.AddScoped<IItemResolver, ItemResolver>();
        services.AddScoped<ISupplierResolver, SupplierResolver>();
        services.AddScoped<IBatchFactory, BatchFactory>();
        services.AddScoped<IBatchRetailTermsReviser, BatchRetailTermsReviser>();
        services.AddScoped<IInventoryUpdater, InventoryUpdater>();
        services.AddScoped<IInboundInventoryLineProcessor, InboundInventoryLineProcessor>();
        services.AddScoped<IInventoryAdjustmentNumberGenerator, InventoryAdjustmentNumberGenerator>();

        services.AddScoped<ISaleLineValidator, SaleLineValidator>();
        services.AddScoped<ICustomerResolver, CustomerResolver>();
        services.AddScoped<ISaleReturnNumberGenerator, SaleReturnNumberGenerator>();
        services.AddScoped<ISaleReturnCalculator, SaleReturnCalculator>();
        services.AddScoped<ISaleReturnValidator, SaleReturnValidator>();
        services.AddSingleton<IReturnAccountingPolicy, ProportionalLineTaxReturnAccountingPolicy>();
        services.AddSingleton<IAccountingVoucherValidator, AccountingVoucherValidator>();
        services.AddScoped<ReturnAdjustmentReader>();
        services.AddScoped<SaleCancellationAdjustmentReader>();
        services.AddScoped<ISaleReturnMutationCoordinator, SaleReturnMutationCoordinator>();
        services.AddScoped<ISalePricingCalculator, SalePricingCalculator>();

        services.AddScoped<ICreditNoteCodeGenerator, CreditNoteCodeGenerator>();

        services.AddScoped<IPaymentAllocationPlanner, PaymentAllocationPlanner>();
        services.AddScoped<IPaymentTransactionRunner, PaymentTransactionRunner>();
        services.AddScoped<PaymentReceiptDtoBuilder>();

        services.AddScoped<Features.Sales.Services.Cancellations.SaleCancellationPolicy>();
        services.AddScoped<Features.Sales.Services.Cancellations.SaleCancellationService>();
        services.AddScoped<SaleDtoBuilder>();
        services.AddScoped<RecordSaleCommandHandler>();
        services.AddScoped<SyncOfflineSalesCommandHandler>();

        services.AddScoped<ISalesExportDatasetBuilder, SalesExportDatasetBuilder>();
        services.AddScoped<ProfitLossReportBuilder>();

        services.AddScoped<DiscountRuleValidationService>();

        services.AddScoped<IOfflineSaleVarianceAnalyzer, OfflineSaleVarianceAnalyzer>();
        services.AddScoped<IOfflineSalePoster, OfflineSalePoster>();
        services.AddScoped<ReconciliationCaseMapper>();

        return services;
    }
}
