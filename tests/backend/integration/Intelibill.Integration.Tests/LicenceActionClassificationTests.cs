using Intelibill.Application.Features.Inventory.Commands.AddInventory;
using Intelibill.Application.Features.Inventory.Commands.AddInventoryBatch;
using Intelibill.Application.Features.FinancialOperations.Queries.GetFinancialOperation;
using Intelibill.Application.Features.Inventory.Commands.CorrectStockCount;
using Intelibill.Application.Features.Inventory.Commands.CreateAdjustment;
using Intelibill.Application.Features.PurchaseReceipts.Commands.CorrectReceiptCost;
using Intelibill.Application.Features.Sales.Commands.RecordSaleReturn;
using Intelibill.Application.Features.Sales.Commands.VoidSaleReturn;
using Intelibill.Application.Features.Sales.Queries.PreviewSaleReturn;
using Intelibill.Application.Features.BankAccounts.Commands.AddBankAccount;
using Intelibill.Application.Features.BankAccounts.Commands.DeleteBankAccount;
using Intelibill.Application.Features.BankAccounts.Commands.UpdateBankAccount;
using Intelibill.Application.Features.Shops.Commands.CreateShop;
using Intelibill.Application.Features.Shops.Commands.DeclareShopTaxProfile;
using Intelibill.Application.Features.Shops.Commands.UpdateShop;
using Intelibill.Application.Features.Shops.Commands.UpdateShopBankDetails;
using Intelibill.Application.Features.Shops.Setup;
using Intelibill.Application.Features.Shops.TillSettings;
using Intelibill.Application.Features.Suppliers.Commands.AddSupplier;
using Intelibill.Application.Features.Suppliers.Commands.EditSupplier;
using System.Reflection;
using Intelibill.Api.Controllers;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Features.Inventory.Services;
using Intelibill.Application.Features.Customers.Commands.AddCustomer;
using Intelibill.Application.Features.Items.Commands.AddItem;
using Intelibill.Application.Features.Items.Queries.GetProductDetails;
using Intelibill.Application.Features.Services.Commands.AddService;
using Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Data;
using Intelibill.Shared.Web.Delivery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine.Runtime.Handlers;
using static Intelibill.Domain.Enums.MembershipStatus;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class LicenceActionClassificationTests(PostgreSqlTestFixture fixture)
{
    [Theory]
    [InlineData(typeof(InventoryController), nameof(InventoryController.CorrectReceiptCost), LicenceAction.SourceCorrection)]
    [InlineData(typeof(CorrectReceiptCostCommandHandler), nameof(CorrectReceiptCostCommandHandler.HandleAsync), LicenceAction.SourceCorrection)]
    [InlineData(typeof(InventoryController), nameof(InventoryController.CountStock), LicenceAction.GeneralStock)]
    [InlineData(typeof(CorrectStockCountCommandHandler), nameof(CorrectStockCountCommandHandler.HandleAsync), LicenceAction.GeneralStock)]
    [InlineData(typeof(InventoryController), nameof(InventoryController.CreateAdjustment), LicenceAction.GeneralStock)]
    [InlineData(typeof(CreateAdjustmentCommandHandler), nameof(CreateAdjustmentCommandHandler.HandleAsync), LicenceAction.GeneralStock)]
    [InlineData(typeof(FinancialOperationsController), nameof(FinancialOperationsController.GetFinancialOperation), LicenceAction.Recovery)]
    [InlineData(typeof(GetFinancialOperationQueryHandler), nameof(GetFinancialOperationQueryHandler.Handle), LicenceAction.Recovery)]
    public void Receipt_correction_and_general_stock_keep_distinct_classifications(Type type, string methodName, LicenceAction action) =>
        Assert.Equal(action, type.GetMethod(methodName)?.GetCustomAttribute<LicenceActionAttribute>()?.Action);

    [Theory]
    [InlineData(typeof(InventoryController), nameof(InventoryController.AddInventory))]
    [InlineData(typeof(InventoryController), nameof(InventoryController.AddInventoryBatch))]
    [InlineData(typeof(AddInventoryCommandHandler), nameof(AddInventoryCommandHandler.HandleAsync))]
    [InlineData(typeof(AddInventoryBatchCommandHandler), nameof(AddInventoryBatchCommandHandler.HandleAsync))]
    public void Intake_entry_points_require_new_trading_access(Type type, string methodName) =>
        Assert.Equal(LicenceAction.NewTrading, type.GetMethod(methodName)?.GetCustomAttribute<LicenceActionAttribute>()?.Action);

    [Fact]
    public async Task Every_protected_entry_point_has_one_classification()
    {
        using var factory = new ApiWebApplicationFactory(fixture);
        await factory.InitializeAsync();

        var missing = Unclassified(factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.Select(endpoint =>
            (endpoint.DisplayName ?? "(unnamed endpoint)", endpoint.Metadata.AsEnumerable())));
        var handlers = factory.Services.GetRequiredService<HandlerGraph>().AllChains()
            .SelectMany(chain => chain.HandlerCalls()).Select(call => call.Method).Distinct();
        var types = MerchantAssemblies.SelectMany(assembly => assembly.GetTypes()).Where(IsWriter);
        missing = missing.Concat(Unclassified(handlers.Cast<MemberInfo>().Concat(types)
            .Select(member => ($"{member.DeclaringType?.FullName ?? (member as Type)?.Namespace}.{member.Name}",
                member.GetCustomAttributes(inherit: false).AsEnumerable())))).Order(StringComparer.Ordinal);

        var failures = missing.ToArray();
        Assert.True(failures.Length == 0,
            $"{failures.Length} missing or ambiguous licence classifications:\n{string.Join("\n", failures)}");
    }

    [Theory]
    [InlineData(typeof(ItemsController), nameof(ItemsController.AddItem), LicenceAction.OperationalCapacity)]
    [InlineData(typeof(ServicesController), nameof(ServicesController.AddService), LicenceAction.OperationalCapacity)]
    [InlineData(typeof(ItemsController), nameof(ItemsController.GetProductDetails), LicenceAction.History)]
    [InlineData(typeof(AddItemCommandHandler), nameof(AddItemCommandHandler.HandleAsync), LicenceAction.OperationalCapacity)]
    [InlineData(typeof(AddServiceCommandHandler), nameof(AddServiceCommandHandler.HandleAsync), LicenceAction.OperationalCapacity)]
    [InlineData(typeof(GetProductDetailsByNameOrBarcodeQueryHandler), nameof(GetProductDetailsByNameOrBarcodeQueryHandler.HandleAsync), LicenceAction.History)]
    [InlineData(typeof(CustomersController), nameof(CustomersController.AddCustomer), LicenceAction.OperationalCapacity)]
    [InlineData(typeof(AddCustomerCommandHandler), nameof(AddCustomerCommandHandler.HandleAsync), LicenceAction.OperationalCapacity)]
    [InlineData(typeof(SuppliersController), nameof(SuppliersController.AddSupplier), LicenceAction.OperationalCapacity)]
    [InlineData(typeof(SuppliersController), nameof(SuppliersController.EditSupplier), LicenceAction.OperationalCapacity)]
    [InlineData(typeof(AddSupplierCommandHandler), nameof(AddSupplierCommandHandler.HandleAsync), LicenceAction.OperationalCapacity)]
    [InlineData(typeof(EditSupplierCommandHandler), nameof(EditSupplierCommandHandler.HandleAsync), LicenceAction.OperationalCapacity)]
    public void Definition_routes_and_messages_keep_their_classifications(Type type, string methodName, LicenceAction action) =>
        Assert.Equal(action, type.GetMethod(methodName)?.GetCustomAttribute<LicenceActionAttribute>()?.Action);

    [Theory]
    [InlineData(typeof(ShopsController), nameof(ShopsController.GetSetup), LicenceAction.History)]
    [InlineData(typeof(ShopsController), nameof(ShopsController.UpdateSetupPreferences), LicenceAction.BasicShopSetup)]
    [InlineData(typeof(ShopsController), nameof(ShopsController.CreateShop), LicenceAction.OperationalCapacity)]
    [InlineData(typeof(ShopsController), nameof(ShopsController.DeclareTaxProfile), LicenceAction.BasicShopSetup)]
    [InlineData(typeof(ShopsController), nameof(ShopsController.UpdateShop), LicenceAction.BasicShopSetup)]
    [InlineData(typeof(ShopsController), nameof(ShopsController.UpdateShopBankDetails), LicenceAction.OperationalCapacity)]
    [InlineData(typeof(ShopsController), nameof(ShopsController.UpdateTillSettings), LicenceAction.OperationalCapacity)]
    [InlineData(typeof(ExpiryPoliciesController), nameof(ExpiryPoliciesController.Put), LicenceAction.OperationalCapacity)]
    [InlineData(typeof(ExpiryPoliciesController), nameof(ExpiryPoliciesController.PutItem), LicenceAction.OperationalCapacity)]
    [InlineData(typeof(BankAccountsController), nameof(BankAccountsController.AddBankAccount), LicenceAction.OperationalCapacity)]
    [InlineData(typeof(BankAccountsController), nameof(BankAccountsController.UpdateBankAccount), LicenceAction.OperationalCapacity)]
    [InlineData(typeof(BankAccountsController), nameof(BankAccountsController.DeleteBankAccount), LicenceAction.OperationalCapacity)]
    [InlineData(typeof(GetShopSetupQueryHandler), nameof(GetShopSetupQueryHandler.HandleAsync), LicenceAction.History)]
    [InlineData(typeof(UpdateShopSetupPreferencesCommandHandler), nameof(UpdateShopSetupPreferencesCommandHandler.HandleAsync), LicenceAction.BasicShopSetup)]
    [InlineData(typeof(CreateShopCommandHandler), nameof(CreateShopCommandHandler.HandleAsync), LicenceAction.OperationalCapacity)]
    [InlineData(typeof(DeclareShopTaxProfileCommandHandler), nameof(DeclareShopTaxProfileCommandHandler.HandleAsync), LicenceAction.BasicShopSetup)]
    [InlineData(typeof(UpdateShopCommandHandler), nameof(UpdateShopCommandHandler.HandleAsync), LicenceAction.BasicShopSetup)]
    [InlineData(typeof(UpdateShopBankDetailsCommandHandler), nameof(UpdateShopBankDetailsCommandHandler.HandleAsync), LicenceAction.OperationalCapacity)]
    [InlineData(typeof(UpdateShopTillSettingsCommandHandler), nameof(UpdateShopTillSettingsCommandHandler.HandleAsync), LicenceAction.OperationalCapacity)]
    [InlineData(typeof(AddBankAccountCommandHandler), nameof(AddBankAccountCommandHandler.HandleAsync), LicenceAction.OperationalCapacity)]
    [InlineData(typeof(UpdateBankAccountCommandHandler), nameof(UpdateBankAccountCommandHandler.HandleAsync), LicenceAction.OperationalCapacity)]
    [InlineData(typeof(DeleteBankAccountCommandHandler), nameof(DeleteBankAccountCommandHandler.HandleAsync), LicenceAction.OperationalCapacity)]
    public void Shop_setup_and_operating_settings_have_distinct_classifications(Type type, string methodName, LicenceAction action) =>
        Assert.Equal(action, type.GetMethod(methodName)?.GetCustomAttribute<LicenceActionAttribute>()?.Action);

    [Theory]
    [InlineData(typeof(SalesController), nameof(SalesController.PreviewSaleReturn), false)]
    [InlineData(typeof(SalesController), nameof(SalesController.RecordSaleReturn), true)]
    [InlineData(typeof(SalesController), nameof(SalesController.VoidSaleReturn), true)]
    [InlineData(typeof(PreviewSaleReturnQueryHandler), nameof(PreviewSaleReturnQueryHandler.Handle), false)]
    [InlineData(typeof(RecordSaleReturnCommandHandler), nameof(RecordSaleReturnCommandHandler.HandleAsync), false)]
    [InlineData(typeof(VoidSaleReturnCommandHandler), nameof(VoidSaleReturnCommandHandler.HandleAsync), false)]
    public void Return_operations_preserve_source_correction_and_check_recovery_at_http_entry(
        Type type, string methodName, bool checksRecovery)
    {
        var method = type.GetMethod(methodName)!;
        Assert.Equal(LicenceAction.SourceCorrection, method.GetCustomAttribute<LicenceActionAttribute>()?.Action);
        Assert.Equal(checksRecovery, method.GetCustomAttribute<SuspensionAtSourceAttribute>()?.CheckRecovery ?? false);
    }

    [Fact]
    public void Every_reconciliation_action_has_a_classification()
    {
        Assert.All(Enum.GetValues<ReconciliationAction>(), action =>
            Assert.Equal(LicenceAction.Recovery, ReconciliationLicenceActions.For(action)));
    }

    public static TheoryData<MembershipStatus, MembershipStatus, LicenceAction> MembershipTransitions => new()
    {
        { Active, Active, LicenceAction.OperationalCapacity },
        { Active, Suspended, LicenceAction.MemberRemoval },
        { Active, Removed, LicenceAction.MemberRemoval },
        { Suspended, Active, LicenceAction.OperationalCapacity },
        { Suspended, Suspended, LicenceAction.OperationalCapacity },
        { Suspended, Removed, LicenceAction.MemberRemoval },
        { Removed, Active, LicenceAction.OperationalCapacity },
        { Removed, Suspended, LicenceAction.MemberRemoval },
        { Removed, Removed, LicenceAction.OperationalCapacity },
    };

    [Theory]
    [MemberData(nameof(MembershipTransitions))]
    public void Membership_transition_classification_keeps_role_changes_under_operational_capacity(
        MembershipStatus current, MembershipStatus requested, LicenceAction statusOnly)
    {
        Assert.Equal(statusOnly, MembershipLicenceActions.For(current, requested, roleChanged: false));
        Assert.Equal(LicenceAction.OperationalCapacity, MembershipLicenceActions.For(current, requested, roleChanged: true));
    }

    [Fact]
    public void Membership_transition_cases_cover_every_status_pair()
    {
        var statuses = Enum.GetValues<MembershipStatus>();
        var pairs = from current in statuses from requested in statuses select (current, requested);
        Assert.Equal(pairs, MembershipTransitions.Select(row => ((MembershipStatus)row[0], (MembershipStatus)row[1])));
    }

    [Fact]
    public void Coverage_rejects_an_unclassified_endpoint_and_handler()
    {
        var endpoint = new Endpoint(null, new EndpointMetadataCollection(new AuthorizeAttribute()), "new protected endpoint");
        var handler = typeof(UnclassifiedHandler).GetMethod(nameof(UnclassifiedHandler.Handle))!;
        Assert.Equal(["new protected endpoint", "Handle"], Unclassified([
            (endpoint.DisplayName!, endpoint.Metadata.AsEnumerable()),
            (handler.Name, handler.GetCustomAttributes(inherit: false).AsEnumerable()),
        ]));
    }

    [Fact]
    public void Licence_policy_and_fence_are_not_request_global_filters()
    {
        var violations = MerchantAssemblies.SelectMany(assembly => assembly.GetTypes())
            .Where(UsesLicenceAtRequestBoundary).Select(type => type.FullName);
        Assert.Empty(violations);
    }

    private static bool UsesLicenceAtRequestBoundary(Type type)
    {
        var methods = type.GetMethods().Where(method => method.Name is "Invoke" or "InvokeAsync");
        var middlewareMethods = methods.Where(method => method.GetParameters()
            .Any(parameter => parameter.ParameterType == typeof(HttpContext))).ToArray();
        var requestBoundary = middlewareMethods.Length > 0 || typeof(IMiddleware).IsAssignableFrom(type) ||
            typeof(IFilterMetadata).IsAssignableFrom(type) || typeof(IEndpointFilter).IsAssignableFrom(type);
        return requestBoundary && type.GetConstructors().Cast<MethodBase>().Concat(middlewareMethods)
            .SelectMany(member => member.GetParameters()).Any(parameter =>
                parameter.ParameterType == typeof(IShopLicenceFence) || parameter.ParameterType == typeof(ILicenceActionPolicy));
    }

    private static readonly Assembly[] MerchantAssemblies =
        [typeof(Program).Assembly, typeof(IInboundRowExecutor).Assembly, typeof(ApplicationDbContext).Assembly];

    private static bool IsWriter(Type type) => type is { IsClass: true, IsAbstract: false } &&
        (typeof(IHostedService).IsAssignableFrom(type) || typeof(ILeasedHttpDestination).IsAssignableFrom(type) ||
            typeof(IOfflineSalePoster).IsAssignableFrom(type) ||
            type.GetInterfaces().Any(contract => contract.Name.EndsWith("RowExecutor", StringComparison.Ordinal)));

    private static IEnumerable<string> Unclassified(IEnumerable<(string Name, IEnumerable<object> Metadata)> entries) =>
        entries.Where(entry => !HasClassification(entry.Metadata)).Select(entry => entry.Name);

    private static bool HasClassification(IEnumerable<object> metadata)
    {
        var classifications = metadata.OfType<LicenceActionAttribute>().ToArray();
        return classifications.Length == 1 && Enum.IsDefined(classifications[0].Action);
    }

    private sealed class UnclassifiedHandler
    {
        public static void Handle() { }
    }
}
