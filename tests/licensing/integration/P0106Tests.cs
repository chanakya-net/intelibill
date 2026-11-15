using System.Net;
using System.Text.Json;
using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0106Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    [Fact]
    public async Task ProductionStartup_DoesNotProvisionBootstrap()
    {
        var runtime = await NewFixtureAsync();
        await using var api = new LicensingApiFactory(runtime, environment: "Production");
        using var client = api.CreateClient(new() { BaseAddress = new Uri("https://localhost") });

        using var response = await client.GetAsync(new Uri("/api/admin/auth/bootstrap/context", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var context = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(context.RootElement.GetProperty("available").GetBoolean());
        Assert.False(context.RootElement.GetProperty("canResume").GetBoolean());
        Assert.False(context.RootElement.TryGetProperty("requestToken", out _));
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(0L, await ScalarAsync(runtime, "SELECT count(*) FROM admin_identities"));
        Assert.Equal(0L, await ScalarAsync(runtime, "SELECT count(*) FROM initial_admin_bootstrap WHERE prepared_at IS NOT NULL"));
    }
    private async Task<NpgsqlConnectionStringBuilder> NewFixtureAsync()
    {
        var name = "p0106_" + Guid.NewGuid().ToString("N");
        await LicensingPostgreSqlFixture.ExecuteAsync(database.Bootstrap(), $"""
            CREATE DATABASE {name} OWNER licensing_migrator;
            """);
        await LicensingPostgreSqlFixture.ExecuteAsync(database.Bootstrap(), $"""
            REVOKE ALL ON DATABASE {name} FROM PUBLIC;
            GRANT CONNECT ON DATABASE {name} TO licensing_runtime;
            """);
        var migrator = database.Login(name, LicensingPostgreSqlFixture.MigratorRole);
        var options = new DbContextOptionsBuilder<LicensingDbContext>()
            .UseNpgsql(migrator.ConnectionString).UseSnakeCaseNamingConvention().Options;
        await using var context = new LicensingDbContext(options);
        await context.Database.MigrateAsync();
        await LicensingPostgreSqlFixture.ExecuteAsync(migrator, """
            REVOKE ALL ON SCHEMA public FROM PUBLIC;
            GRANT USAGE ON SCHEMA public TO licensing_runtime;
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO licensing_runtime;
            """);
        return database.Login(name, LicensingPostgreSqlFixture.RuntimeRole);
    }
}
