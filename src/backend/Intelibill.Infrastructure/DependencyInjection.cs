using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Exports.ProfitLoss;
using Intelibill.Application.Features.Exports.Sales.Services;
using Intelibill.Application.Features.Items.Barcodes;
using Intelibill.Application.Features.Items.Queries.GetItems;
using Intelibill.Application.Features.PurchaseOrders.Services;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Data.Interceptors;
using Intelibill.Infrastructure.Options;
using Intelibill.Infrastructure.Observability;
using Intelibill.Infrastructure.Repositories;
using Intelibill.Infrastructure.Services.Auth;
using Intelibill.Infrastructure.Services.Auth.ExternalAuth;
using Intelibill.Infrastructure.Services.Exports;
using Intelibill.Infrastructure.Services.Barcodes;
using Intelibill.Application.Features.Exports.Sales;
using Intelibill.Infrastructure.Services.ProductLookup;
using Intelibill.Infrastructure.Services.PurchaseOrders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Postgres;
using Npgsql;

namespace Intelibill.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // ── Database ────────────────────────────────────────────────────────
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<DatabaseOptions>, DatabaseOptionsValidator>();

        // One data source for the whole process, shared by EF and the distributed
        // cache below. Two of them would mean two connection pools and, under
        // Entra, two independent token refresh loops.
        services.AddSingleton(sp => NpgsqlDataSourceFactory.Create(
            sp.GetRequiredService<IOptions<DatabaseOptions>>().Value,
            sp.GetRequiredService<ILoggerFactory>()));

        services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            var dataSource = sp.GetRequiredService<NpgsqlDataSource>();
            var sessionInterceptor = sp.GetRequiredService<PostgresSessionContextInterceptor>();
            var telemetryInterceptors = sp.GetServices<IInterceptor>();
            options.UseNpgsql(dataSource, npgsql =>
                npgsql.MigrationsAssembly(typeof(DependencyInjection).Assembly.FullName))
                .AddInterceptors(sessionInterceptor)
                .AddInterceptors(telemetryInterceptors)
                .UseSnakeCaseNamingConvention();
        });

        services.AddScoped<PostgresSessionContextInterceptor>();
        services.AddSingleton<RequestDatabaseTelemetryAccessor>();
        services.AddScoped<IInterceptor, RequestDatabaseCommandInterceptor>();

        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // ── Auth options ─────────────────────────────────────────────────────
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();

        services.AddOptions<ExternalAuthOptions>()
            .Bind(configuration.GetSection(ExternalAuthOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<Microsoft.Extensions.Options.IValidateOptions<ExternalAuthOptions>, ExternalAuthOptionsValidator>();

        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<EmailOptions>, EmailOptionsValidator>();

        services.AddOptions<ProductLookupOptions>()
            .Bind(configuration.GetSection(ProductLookupOptions.SectionName));

        services.AddOptions<HsnServiceOptions>()
            .Bind(configuration.GetSection(HsnServiceOptions.SectionName));

        // ── Repositories ─────────────────────────────────────────────────────
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IShopRepository, ShopRepository>();
        services.AddScoped<ISupplierRepository, SupplierRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<ICustomerLedgerEntryRepository, CustomerLedgerEntryRepository>();
        services.AddScoped<IBankAccountRepository, BankAccountRepository>();
        services.AddScoped<IItemRepository, ItemRepository>();
        services.AddScoped<IItemBarcodeSequenceRepository, ItemBarcodeSequenceRepository>();
        services.AddScoped<IBarcodeLabelRepository, BarcodeLabelRepository>();
        services.AddScoped<IItemCatalogRepository, ItemRepository>();
        services.AddScoped<IInventoryRepository, InventoryRepository>();
        services.AddScoped<IInventoryBatchRepository, InventoryBatchRepository>();
        services.AddScoped<IInventoryAdjustmentRepository, InventoryAdjustmentRepository>();
        services.AddScoped<IStockTransactionRepository, StockTransactionRepository>();
        services.AddScoped<ICreditNoteRedemptionRepository, CreditNoteRedemptionRepository>();
        services.AddScoped<ISupplierLedgerEntryRepository, SupplierLedgerEntryRepository>();
        services.AddScoped<ISaleRepository, SaleRepository>();
        services.AddScoped<IReconciliationIssueRepository, ReconciliationIssueRepository>();
        services.AddScoped<ISaleReturnRepository, SaleReturnRepository>();
        services.AddScoped<IInvoiceLeaseRepository, InvoiceLeaseRepository>();
        services.AddScoped<IServiceRepository, ServiceRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IPasswordResetTokenRepository, PasswordResetTokenRepository>();
        services.AddScoped<IExpenseRepository, ExpenseRepository>();
        services.AddScoped<IExpenseCategoryRepository, ExpenseCategoryRepository>();
        services.AddScoped<IDiscountRuleRepository, DiscountRuleRepository>();
        services.AddScoped<IHsnCacheRepository, HsnCacheRepository>();
        services.AddScoped<IPurchaseOrderRepository, PurchaseOrderRepository>();
        services.AddScoped<IPurchaseOrderSequenceRepository, PurchaseOrderSequenceRepository>();
        services.AddScoped<IPurchaseOrderReceiptRepository, PurchaseOrderReceiptRepository>();
        services.AddScoped<IPurchaseOrderReceiptSequenceRepository, PurchaseOrderReceiptSequenceRepository>();
        services.AddScoped<ICreditNoteRepository, CreditNoteRepository>();

        // ── Auth services ─────────────────────────────────────────────────────
        // Singleton: the Key Vault signer caches the resolved key version and the
        // client that signs with it, and neither belongs to a request.
        services.AddSingleton<IJwtSigner>(sp =>
        {
            var jwt = sp.GetRequiredService<IOptions<JwtOptions>>().Value;
            var timeProvider = sp.GetRequiredService<TimeProvider>();

            return jwt.SigningMode switch
            {
                JwtSigningMode.KeyVault => new KeyVaultJwtSigner(sp.GetRequiredService<IOptions<JwtOptions>>(), timeProvider),
                _ => new HmacJwtSigner(sp.GetRequiredService<IOptions<JwtOptions>>()),
            };
        });

        // Only registered in Key Vault mode: constructing it requires a key id,
        // and the HMAC path validates with the same secret it signs with.
        if (configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()?.SigningMode == JwtSigningMode.KeyVault)
        {
            services.AddSingleton<KeyVaultJwtValidationKeyProvider>();
        }

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IPasswordHasher, BcryptPasswordHasher>();
        services.AddSingleton<ISmtpClientFactory, MailKitSmtpClientFactory>();
        services.AddSingleton<IEmailService>(sp =>
        {
            var emailOptions = sp.GetRequiredService<IOptions<EmailOptions>>().Value;
            return emailOptions.Enabled
                ? new SmtpEmailService(
                    sp.GetRequiredService<IOptions<EmailOptions>>(),
                    sp.GetRequiredService<ISmtpClientFactory>(),
                    sp.GetRequiredService<ILogger<SmtpEmailService>>())
                : new NoOpEmailService(sp.GetRequiredService<ILogger<NoOpEmailService>>());
        });

        // ── Export services ────────────────────────────────────────────────────
        services.AddScoped<IExportFileNameBuilder, ExportFileNameBuilder>();
        services.AddScoped<ISalesExcelExportRenderer, Services.Exports.SalesExcelExportRenderer>();
        services.AddScoped<ISalesPdfExportRenderer, Services.Exports.SalesPdfExportRenderer>();
        services.AddScoped<ISalesTallyXmlExportRenderer, Services.Exports.SalesTallyXmlExportRenderer>();
        services.AddScoped<IProfitLossExcelExportRenderer, Services.Exports.ProfitLossExcelExportRenderer>();
        services.AddScoped<IBarcodeLabelPdfRenderer, BarcodeLabelPdfRenderer>();

        // ── External auth providers ───────────────────────────────────────────
        // Named HttpClients for providers that call external HTTP APIs.
        services.AddMemoryCache();

        services.AddOptions<DistributedCacheOptions>()
            .Bind(configuration.GetSection(DistributedCacheOptions.SectionName))
            .ValidateOnStart();

        // Registers the cache itself; the configuration below runs after this one
        // and supplies the shared data source, which cannot be resolved from a
        // plain setup action.
        services.AddDistributedPostgresCache(_ => { });

        services.AddOptions<PostgresCacheOptions>()
            .Configure<NpgsqlDataSource, IOptions<DistributedCacheOptions>>((cache, dataSource, cacheOptions) =>
            {
                var options = cacheOptions.Value;

                cache.DataSource = dataSource;
                cache.SchemaName = options.SchemaName;
                cache.TableName = options.TableName;
                cache.CreateIfNotExists = options.CreateIfNotExists;
                cache.ExpiredItemsDeletionInterval = options.ExpiredItemsDeletionInterval;
            });

        services.AddHttpClient(nameof(GoogleAuthProvider));
        services.AddHttpClient(nameof(FacebookAuthProvider));
        services.AddHttpClient(nameof(TwitterAuthProvider));

        // All providers are registered as IExternalAuthProvider so the handler can
        // resolve IEnumerable<IExternalAuthProvider> and pick the right one.
        services.AddScoped<IExternalAuthProvider, GoogleAuthProvider>();
        services.AddScoped<IExternalAuthProvider, MicrosoftAuthProvider>();
        services.AddScoped<IExternalAuthProvider, FacebookAuthProvider>();
        services.AddScoped<IExternalAuthProvider, TwitterAuthProvider>();
        services.AddScoped<IExternalAuthProvider, AppleAuthProvider>();

        services.AddScoped<IExternalOAuthCodeProvider, GoogleAuthProvider>();
        services.AddScoped<IExternalOAuthCodeProvider, FacebookAuthProvider>();
        services.AddScoped<IExternalOAuthStateStore, DistributedExternalOAuthStateStore>();
        services.AddScoped<IExternalOAuthFlowService, ExternalOAuthFlowService>();

        services.AddHttpClient(ExternalProductLookupService.HttpClientName, (sp, client) =>
        {
            var lookupOptions = sp.GetRequiredService<IOptions<ProductLookupOptions>>().Value;
            client.BaseAddress = new Uri(lookupOptions.BaseUrl);
        });
        services.AddScoped<IExternalProductLookupService, ExternalProductLookupService>();

        services.AddHttpClient("HsnService", (sp, client) =>
        {
            var hsnOptions = sp.GetRequiredService<IOptions<HsnServiceOptions>>().Value;
            client.BaseAddress = new Uri(hsnOptions.BaseUrl);
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Key", hsnOptions.ApiKey);
        })
        .ConfigurePrimaryHttpMessageHandler(sp =>
        {
            var env = sp.GetRequiredService<IHostEnvironment>();
            var handler = new HttpClientHandler();
            if (env.IsDevelopment())
                handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            return handler;
        });
        services.AddScoped<IExternalHsnLookupService, Services.Hsn.ExternalHsnLookupService>();

        services.AddScoped<IPurchaseOrderNumberGenerator, PurchaseOrderNumberGenerator>();
        services.AddScoped<IPurchaseOrderReceiptNumberGenerator, PurchaseOrderReceiptNumberGenerator>();

        return services;
    }
}
