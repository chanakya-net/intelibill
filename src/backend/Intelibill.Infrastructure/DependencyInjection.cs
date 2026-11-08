using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Operations;
using Intelibill.Application.Features.Exports.ProfitLoss;
using Intelibill.Application.Features.Exports.Sales.Services;
using Intelibill.Application.Features.Inventory.Services;
using Intelibill.Application.Features.Items.Barcodes;
using Intelibill.Application.Features.Items.Queries.GetItems;
using Intelibill.Application.Features.Payments.Services;
using Intelibill.Application.Features.PurchaseOrders.Services;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Data.Interceptors;
using Intelibill.Infrastructure.Options;
using Intelibill.Infrastructure.Observability;
using Intelibill.Infrastructure.Repositories;
using Intelibill.Infrastructure.Services.Auth;
using Intelibill.Infrastructure.Services.Exports;
using Intelibill.Infrastructure.Services.Barcodes;
using Intelibill.Application.Features.Exports.Sales;
using Intelibill.Infrastructure.Services.ProductLookup;
using Intelibill.Infrastructure.Services.PurchaseOrders;
using Intelibill.Infrastructure.Services.Operations;
using Intelibill.Infrastructure.Services.Payments;
using Intelibill.Infrastructure.Services.StockIntake;
using Intelibill.Infrastructure.Services.RateLimiting;
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
            options.UseNpgsql(dataSource, npgsql =>
                npgsql.MigrationsAssembly(typeof(DependencyInjection).Assembly.FullName))
                .AddInterceptors(sp.GetServices<IInterceptor>())
                .UseSnakeCaseNamingConvention();
        });

        // Registered under IInterceptor, not under its own type: a host that rebuilds
        // the DbContext registration (the integration harness does) picks up every
        // interceptor through IInterceptor, and a session interceptor it silently
        // dropped would leave app.active_shop_id unset and every policy inert.
        // The two interceptors hook different events — connection versus command —
        // so the order they are applied in does not matter.
        services.AddScoped<IInterceptor, PostgresSessionContextInterceptor>();
        services.AddSingleton<RequestDatabaseTelemetryAccessor>();
        services.AddScoped<IInterceptor, RequestDatabaseCommandInterceptor>();

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ITenantWriteScope, TenantWriteScope>();

        // Before the revision serves a request: a connection secret pointing at a
        // privileged role would disarm every tenant policy silently.
        services.AddHostedService<RuntimeRoleGuard>();

        // ── Auth options ─────────────────────────────────────────────────────
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();

        services.AddOptions<AuthSessionOptions>()
            .Bind(configuration.GetSection(AuthSessionOptions.SectionName));

        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<EmailOptions>, EmailOptionsValidator>();

        services.AddOptions<PhoneAuthOptions>()
            .Bind(configuration.GetSection(PhoneAuthOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<PhoneAuthOptions>, PhoneAuthOptionsValidator>();

        services.AddOptions<ProductLookupOptions>()
            .Bind(configuration.GetSection(ProductLookupOptions.SectionName));

        services.AddOptions<HsnServiceOptions>()
            .Bind(configuration.GetSection(HsnServiceOptions.SectionName));

        // ── Repositories ─────────────────────────────────────────────────────
        services.AddScoped<ITillRepository, TillRepository>();
        services.AddScoped<Application.Features.Tills.ITillSessionPolicy, Services.Tills.TillSessionPolicy>();
        services.AddOptions<Options.TillSessionOptions>()
            .Bind(configuration.GetSection(Options.TillSessionOptions.SectionName))
            .Validate(o => !o.MixedTenderEnabled, "Mixed tender is not supported.")
            .ValidateOnStart();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IShopRepository, ShopRepository>();
        services.AddScoped<IMembershipAuthorityRepository, MembershipAuthorityRepository>();
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
        services.AddScoped<IStockCountRepository, StockCountRepository>();
        services.AddScoped<ISupplierReturnRepository, SupplierReturnRepository>();
        services.AddScoped<IPurchaseReceiptReversalRepository, PurchaseReceiptReversalRepository>();
        services.AddScoped<Intelibill.Application.Features.SupplierReturns.Services.ISupplierReturnMutationLock, Intelibill.Infrastructure.Services.SupplierReturns.SupplierReturnMutationLock>();
        services.AddScoped<IPurchaseReceiptValueRepository, PurchaseReceiptValueRepository>();
        services.AddScoped<IPaymentReceiptRepository, PaymentReceiptRepository>();
        services.AddScoped<IPostingPeriodRepository, PostingPeriodRepository>();
        services.AddScoped<Application.Common.Posting.IPostingPeriodPolicy, Services.Posting.PostingPeriodPolicy>();
        services.AddScoped<IInvoiceBalanceReader, InvoiceBalanceReader>();
        services.AddScoped<IPurchaseReceiptCostAdjustmentRepository, PurchaseReceiptCostAdjustmentRepository>();
        services.AddScoped<IBatchPriceRevisionRepository, BatchPriceRevisionRepository>();
        services.AddScoped<IStockTransactionRepository, StockTransactionRepository>();
        services.AddScoped<ICreditNoteRedemptionRepository, CreditNoteRedemptionRepository>();
        services.AddScoped<ISupplierLedgerEntryRepository, SupplierLedgerEntryRepository>();
        services.AddScoped<ISaleRepository, SaleRepository>();
        services.AddScoped<IReconciliationIssueRepository, ReconciliationIssueRepository>();
        services.AddScoped<IReconciliationCaseEventRepository, ReconciliationCaseEventRepository>();
        services.AddScoped<IOfflineSubmissionRepository, OfflineSubmissionRepository>();
        services.AddScoped<ISaleReturnRepository, SaleReturnRepository>();
        services.AddScoped<ISaleCancellationRepository, SaleCancellationRepository>();
        services.AddScoped<IInvoiceLeaseRepository, InvoiceLeaseRepository>();
        services.AddScoped<IInvoiceNumberAllocator, Services.Invoicing.InvoiceNumberAllocator>();
        services.AddScoped<IServiceRepository, ServiceRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IAuthSessionRepository, AuthSessionRepository>();
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
        services.AddScoped<IOperationReceiptRepository, OperationReceiptRepository>();
        services.AddScoped<IFinancialOperationExecutor, FinancialOperationExecutor>();
        services.AddScoped<IInboundRowExecutor, InboundRowExecutor>();

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
        services.Configure<Options.SaleCancellationOptions>(configuration.GetSection(Options.SaleCancellationOptions.SectionName));
        services.AddScoped<Application.Features.Sales.Services.Cancellations.ISaleCancellationRollout, Services.Sales.SaleCancellationRollout>();
        services.AddSingleton<Application.Common.Time.IBusinessClock, Services.Time.BusinessClock>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<LiveSessionValidator>();
        services.AddScoped<ILiveSessionAuthority>(sp => sp.GetRequiredService<LiveSessionValidator>());
        services.AddScoped<IRealtimeConnectionRegistry, Services.Realtime.PostgresRealtimeConnectionRegistry>();
        services.AddScoped<IRecoveryEpochProvider, ConfiguredRecoveryEpochProvider>();
        services.AddScoped<IEmailOwnershipRepository, EmailOwnershipChallengeRepository>();
        services.AddScoped<IPhoneChallengeRepository, PhoneChallengeRepository>();
        services.AddScoped<Intelibill.Application.Features.Auth.Services.ReauthenticationProofs>();
        services.AddScoped<IEmailOwnershipService, Intelibill.Application.Features.Auth.Services.EmailOwnershipService>();
        services.AddScoped<IdentityMailOutbox>();
        services.AddScoped<IIdentityMailOutbox>(sp => sp.GetRequiredService<IdentityMailOutbox>());
        services.AddHostedService<IdentityMailWorker>();
        services.AddSingleton<IPhoneAuthPolicy, PhoneAuthPolicy>();
        services.AddSingleton<IPhoneCodeHasher, PhoneCodeHasher>();
        services.AddScoped<IPhoneOwnershipService, Intelibill.Application.Features.Auth.Services.PhoneOwnershipService>();
        services.AddScoped<IdentitySmsOutbox>();
        services.AddScoped<IIdentitySmsOutbox>(sp => sp.GetRequiredService<IdentitySmsOutbox>());
        services.AddHostedService<IdentitySmsWorker>();
        services.AddHttpClient(TwilioSmsSender.HttpClientName);
        services.AddScoped<ISmsSender>(sp =>
        {
            var phoneOptions = sp.GetRequiredService<IOptions<PhoneAuthOptions>>().Value;
            if (phoneOptions.LocalCapture)
                return new LocalCaptureSmsSender(sp.GetRequiredService<ILogger<LocalCaptureSmsSender>>());

            return phoneOptions.Enabled && !string.IsNullOrWhiteSpace(phoneOptions.AccountSid)
                ? new TwilioSmsSender(sp.GetRequiredService<IHttpClientFactory>(), sp.GetRequiredService<IOptions<PhoneAuthOptions>>())
                : new UnconfiguredSmsSender();
        });
        services.AddScoped<IPasswordHasher, BcryptPasswordHasher>();
        services.AddSingleton<ISmtpClientFactory, MailKitSmtpClientFactory>();
        services.AddSingleton<IEmailService>(sp =>
        {
            var emailOptions = sp.GetRequiredService<IOptions<EmailOptions>>();
            return emailOptions.Value.Enabled
                ? new SmtpEmailService(emailOptions, sp.GetRequiredService<ISmtpClientFactory>())
                : new NoOpEmailService();
        });

        // ── Export services ────────────────────────────────────────────────────
        services.AddScoped<IExportFileNameBuilder, ExportFileNameBuilder>();
        services.AddScoped<ISalesExcelExportRenderer, Services.Exports.SalesExcelExportRenderer>();
        services.AddScoped<ISalesPdfExportRenderer, Services.Exports.SalesPdfExportRenderer>();
        services.AddScoped<ISalesTallyXmlExportRenderer, Services.Exports.SalesTallyXmlExportRenderer>();
        services.AddScoped<IProfitLossExcelExportRenderer, Services.Exports.ProfitLossExcelExportRenderer>();
        services.AddScoped<IBarcodeLabelPdfRenderer, BarcodeLabelPdfRenderer>();

        // ── Shared caches ─────────────────────────────────────────────────────
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

        // ── Rate limiting ─────────────────────────────────────────────────────
        // Singleton over the shared data source: every replica, and every request on this one,
        // reads and writes the same rows, so there is no per-request state to hold.
        services.AddOptions<RateLimitStoreOptions>()
            .Bind(configuration.GetSection(RateLimitStoreOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IAtomicRateLimitStore, PostgresRateLimitStore>();
        services.AddHostedService<RateLimitBucketCleanupWorker>();

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
