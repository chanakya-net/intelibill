using Intelibill.Api.HealthChecks;
using Intelibill.Application.Common.Interfaces;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Npgsql;
using NSubstitute;

namespace Intelibill.Api.Unit.Tests.HealthChecks;

public class IdentityMailReadinessCheckTests
{
    private static async Task<HealthStatus> CheckDisabledAsync(string environment)
    {
        var outbox = Substitute.For<IIdentityMailOutbox>();
        outbox.Enabled.Returns(false);
        await using var dataSource = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1");
        var check = new IdentityMailReadinessCheck(outbox, dataSource, new StubEnvironment(environment));
        return (await check.CheckHealthAsync(new HealthCheckContext())).Status;
    }

    [Fact]
    public async Task Production_MailNotConfigured_IsUnhealthy() =>
        Assert.Equal(HealthStatus.Unhealthy, await CheckDisabledAsync(Environments.Production));

    [Fact]
    public async Task Development_MailNotConfigured_IsDegraded() =>
        Assert.Equal(HealthStatus.Degraded, await CheckDisabledAsync(Environments.Development));

    [Fact]
    public void EmptyQueue_IsHealthy() =>
        Assert.Equal(HealthStatus.Healthy, IdentityMailReadinessCheck.Evaluate(0, TimeSpan.Zero).Status);

    [Fact]
    public void ExhaustedMessages_AreDegradedNotUnhealthy() =>
        Assert.Equal(HealthStatus.Degraded, IdentityMailReadinessCheck.Evaluate(1, TimeSpan.Zero).Status);

    [Fact]
    public void Backlog_PastThreshold_IsDegraded()
    {
        Assert.Equal(HealthStatus.Healthy, IdentityMailReadinessCheck.Evaluate(0, TimeSpan.FromMinutes(4)).Status);
        Assert.Equal(HealthStatus.Degraded, IdentityMailReadinessCheck.Evaluate(0, TimeSpan.FromMinutes(6)).Status);
    }

    private sealed class StubEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Intelibill.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
