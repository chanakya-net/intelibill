using System.Net;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Infrastructure.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;

namespace Intelibill.Integration.Tests;

public sealed class ApiWebApplicationFactory(PostgreSqlTestFixture? fixture = null, Action<IServiceCollection>? configureTestServices = null, bool enableIdentityMail = true, bool enablePhoneAuth = true, int? maxPoolSize = null, bool runtimeIdentityOutsideRequests = false) : WebApplicationFactory<Program>, IAsyncLifetime
{
    public new HttpClient CreateClient(WebApplicationFactoryClientOptions options)
    {
        var client = base.CreateClient(options);
        IdentityTestMailbox.Factories.Add(client, this);
        return client;
    }
    private readonly Action<IServiceCollection>? _configureTestServices = configureTestServices;

    private PostgreSqlContainer? _localContainer;
    private PostgreSqlContainer DbContainer => fixture?.DbContainer ?? _localContainer ?? throw new InvalidOperationException("Container not initialized");

    /// <summary>
    /// Owner identity: applies migrations and backs the DbContext a test resolves
    /// outside a request, so assertions about rows the policies hide are possible.
    /// </summary>
    public string MigratorConnectionString => DbContainer.GetConnectionString();

    /// <summary>
    /// What every API request authenticates as: DML only, no ownership, no
    /// BYPASSRLS — the same shape as the deployed runtime identity.
    /// </summary>
    public string RuntimeConnectionString =>
        RestrictedRuntimeRole.ConnectionStringFor(MigratorConnectionString, maxPoolSize);

    public async Task InitializeAsync()
    {
        if (fixture == null)
        {
            _localContainer = new PostgreSqlBuilder()
                .WithImage("postgres:17-alpine")
                .WithDatabase("integration")
                .WithUsername("integration")
                .WithPassword("integration")
                .Build();
            await _localContainer.StartAsync();
        }

        await RestrictedRuntimeRole.EnsureAsync(MigratorConnectionString);

        // Tests in this project share the same PostgreSQL container through a collection fixture.
        // Resetting the database per factory initialization prevents data leakage between tests.
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(MigratorConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();

        // Privileges are database objects: the drop above took the previous set
        // with it, so the runtime role is re-granted after every migration.
        await RestrictedRuntimeRole.GrantAsync(MigratorConnectionString);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        if (_localContainer != null)
        {
            await _localContainer.StopAsync();
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureLogging(logging =>
        {
            logging.AddFilter("Wolverine.Http.HttpGraph", LogLevel.Error);
        });

        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:BaseUrl"] = "https://inventory.test",
                ["Email:Enabled"] = enableIdentityMail.ToString(),
                ["Email:Host"] = "localhost",
                ["Email:Port"] = "1025",
                ["Email:LocalCapture"] = "true",
                ["Email:FromEmail"] = "tests@intelibill.test",
                ["Email:FromName"] = "Integration tests",
                ["Identity:OutboxKey"] = IdentityTestMailbox.Key,
                ["Identity:PhoneCodeKey"] = IdentityTestPhone.CodeKey,
                ["PhoneAuth:Enabled"] = enablePhoneAuth.ToString(),
                ["PhoneAuth:LocalCapture"] = "true",
                ["ConnectionStrings:DefaultConnection"] = RuntimeConnectionString,
                ["Database:Host"] = DbContainer.Hostname,
                ["Database:Port"] = DbContainer.GetMappedPublicPort(5432).ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Database:Database"] = "integration",
                ["Database:Username"] = RestrictedRuntimeRole.Name,
                ["Database:Password"] = RestrictedRuntimeRole.Password,
                ["Jwt:Secret"] = "integration-secret-key-must-be-at-least-32-chars!",
                ["Jwt:Issuer"] = "inventory.ai.integration",
                ["Jwt:Audience"] = "inventory.ai.integration",
                ["Jwt:AccessTokenExpiryMinutes"] = "15",
                ["Jwt:RefreshTokenExpiryDays"] = "7",
                ["RateLimiting:BucketKey"] = RateLimitTestPeers.BucketKey,
            });

            if (maxPoolSize is not null)
            {
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:MaxPoolSize"] = maxPoolSize.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                });
            }
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<ApplicationDbContext>();

            services.AddDbContext<ApplicationDbContext>((sp, options) =>
            {
                // Requests authenticate as the restricted runtime role, so row-level
                // security is enforced over the whole API surface exactly as it is in
                // a deployed environment. A DbContext resolved outside a request — a
                // test's own inspection scope — stays on the owner identity, because
                // proving that a foreign shop's row was *not* read or changed needs a
                // view the policies do not filter. A test that is about the runtime
                // identity itself, rather than about inspecting rows, asks for it
                // everywhere with runtimeIdentityOutsideRequests: without that, it
                // would run against the owner's pool and prove nothing.
                var httpContextAccessor = sp.GetService<IHttpContextAccessor>();
                var connectionString = runtimeIdentityOutsideRequests || httpContextAccessor?.HttpContext is not null
                    ? RuntimeConnectionString
                    : MigratorConnectionString;

                options.UseNpgsql(connectionString)
                    .AddInterceptors(sp.GetServices<IInterceptor>())
                    .UseSnakeCaseNamingConvention()
                    .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
            });

            services.RemoveAll<IDistributedCache>();
            services.AddSingleton<IDistributedCache, NoOpDistributedCache>();

            // What the no-op cache gave every test before the limiter had a store of its own: no
            // limit at all, so a suite that signs in hundreds of times is not measuring the
            // limiter. Tests about rate limiting put PostgresRateLimitStore back.
            services.RemoveAll<IAtomicRateLimitStore>();
            services.AddSingleton<IAtomicRateLimitStore, UnlimitedRateLimitStore>();
            services.AddSingleton<IStartupFilter, RateLimitTestPeers.StartupFilter>();

            services.AddHttpClient("OtlpTraceExporter")
                .ConfigurePrimaryHttpMessageHandler(static () => new NoOpOtlpHandler());
            services.AddHttpClient("OtlpMetricExporter")
                .ConfigurePrimaryHttpMessageHandler(static () => new NoOpOtlpHandler());

            // Tests inspect encrypted durable mail; never connect to real SMTP.
            foreach (var descriptor in services.Where(x => x.ImplementationType?.Name is "IdentityMailWorker" or "IdentitySmsWorker").ToArray())
                services.Remove(descriptor);
            services.RemoveAll<Intelibill.Application.Common.Interfaces.IEmailService>();
            services.AddSingleton<Intelibill.Application.Common.Interfaces.IEmailService, TestEmailService>();
            _configureTestServices?.Invoke(services);
        });
    }
}

internal sealed class NoOpOtlpHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
}

internal sealed class NoOpDistributedCache : IDistributedCache
{
    public byte[]? Get(string key) => null;
    public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult<byte[]?>(null);
    public void Refresh(string key) { }
    public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
    public void Remove(string key) { }
    public Task RemoveAsync(string key, CancellationToken token = default) => Task.CompletedTask;
    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) { }
    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => Task.CompletedTask;
}

internal sealed class UnlimitedRateLimitStore : IAtomicRateLimitStore
{
    public Task<RateDecision> AcquireAsync(IReadOnlyList<RateBudget> budgets, DateTimeOffset now, CancellationToken ct) =>
        Task.FromResult(new RateDecision(true, budgets[0].Limit, budgets[0].Limit, now.AddSeconds(budgets[0].WindowSeconds), null));

    public Task<int> DeleteExpiredAsync(DateTimeOffset now, int batchSize, CancellationToken ct) => Task.FromResult(0);
}

/// <summary>
/// The test server has no socket, so every request arrives from no address at all. A test that
/// needs callers on different addresses names one in <see cref="Header"/>; it becomes the
/// connection's remote address before forwarded-header processing runs, exactly where a real
/// peer address would be.
/// </summary>
internal static class RateLimitTestPeers
{
    public const string Header = "X-Test-Peer";

    // Public fixture key only; not a deployment secret.
    public const string BucketKey = "cmF0ZS1saW1pdC10ZXN0LWJ1Y2tldC1rZXktMzJieXQ=";

    internal sealed class StartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(Header, out var peer) && IPAddress.TryParse(peer, out var address))
                    context.Connection.RemoteIpAddress = address;
                await nextMiddleware();
            });
            next(app);
        };
    }
}

internal sealed class TestEmailService : Intelibill.Application.Common.Interfaces.IEmailService
{
    public Task SendPasswordResetAsync(string toEmail, string resetLink, DateTimeOffset expiresAt, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SendIdentityAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
