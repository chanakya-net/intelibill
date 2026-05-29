using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Exports.Sales.Services;
using Intelibill.Application.Features.Items.Queries.GetItems;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Data.Interceptors;
using Intelibill.Infrastructure.Options;
using Intelibill.Infrastructure.Repositories;
using Intelibill.Infrastructure.Services.Auth;
using Intelibill.Infrastructure.Services.Auth.ExternalAuth;
using Intelibill.Infrastructure.Services.Exports;
using Intelibill.Application.Features.Exports.Sales;
using Intelibill.Infrastructure.Services.ProductLookup;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Postgres;

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

        services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            var dbOptions = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            var sessionInterceptor = sp.GetRequiredService<PostgresSessionContextInterceptor>();
            options.UseNpgsql(dbOptions.ToConnectionString(), npgsql =>
                npgsql.MigrationsAssembly(typeof(DependencyInjection).Assembly.FullName))
                .AddInterceptors(sessionInterceptor)
                .UseSnakeCaseNamingConvention();
        });

        services.AddScoped<PostgresSessionContextInterceptor>();

        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // ── Auth options ─────────────────────────────────────────────────────
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

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
        services.AddScoped<IItemCatalogRepository, ItemRepository>();
        services.AddScoped<IInventoryRepository, InventoryRepository>();
        services.AddScoped<IInventoryBatchRepository, InventoryBatchRepository>();
        services.AddScoped<IInventoryAdjustmentRepository, InventoryAdjustmentRepository>();
        services.AddScoped<IStockTransactionRepository, StockTransactionRepository>();
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

        // ── Auth services ─────────────────────────────────────────────────────
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

        // ── External auth providers ───────────────────────────────────────────
        // Named HttpClients for providers that call external HTTP APIs.
        services.AddMemoryCache();

        services.AddOptions<DistributedCacheOptions>()
            .Bind(configuration.GetSection(DistributedCacheOptions.SectionName))
            .ValidateOnStart();

        services.AddDistributedPostgresCache(options =>
        {
            var dbOptions = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>()
                ?? throw new InvalidOperationException("DatabaseOptions not configured.");
            var cacheOptions = configuration.GetSection(DistributedCacheOptions.SectionName).Get<DistributedCacheOptions>()
                ?? new DistributedCacheOptions();

            options.ConnectionString = dbOptions.ToConnectionString();
            options.SchemaName = cacheOptions.SchemaName;
            options.TableName = cacheOptions.TableName;
            options.CreateIfNotExists = cacheOptions.CreateIfNotExists;
            options.ExpiredItemsDeletionInterval = cacheOptions.ExpiredItemsDeletionInterval;
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
        services.AddScoped<IExternalOAuthStateStore, InMemoryExternalOAuthStateStore>();
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

        return services;
    }

    private static readonly Action<ILogger, Exception?> LogApplyingMigrations =
        LoggerMessage.Define(LogLevel.Information, new EventId(1, nameof(ApplyMigrationsAsync)), "Applying migrations...");

    private static readonly Action<ILogger, Exception?> LogMigrationsApplied =
        LoggerMessage.Define(LogLevel.Information, new EventId(2, nameof(ApplyMigrationsAsync)), "Migrations applied successfully.");

    private static readonly Action<ILogger, Exception?> LogMigrationError =
        LoggerMessage.Define(LogLevel.Error, new EventId(3, nameof(ApplyMigrationsAsync)), "An error occurred while applying migrations.");

    public static async Task ApplyMigrationsAsync(this IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var services = scope.ServiceProvider;
        var logger = services.GetRequiredService<ILogger<ApplicationDbContext>>();

        try
        {
            var context = services.GetRequiredService<ApplicationDbContext>();
            LogApplyingMigrations(logger, null);
            await context.Database.MigrateAsync();
            LogMigrationsApplied(logger, null);
        }
        catch (Exception ex)
        {
            LogMigrationError(logger, ex);
            throw;
        }
    }
}
