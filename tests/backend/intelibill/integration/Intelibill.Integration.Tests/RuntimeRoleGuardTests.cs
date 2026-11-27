using Intelibill.Infrastructure.Data;
using Npgsql;

namespace Intelibill.Integration.Tests;

/// <summary>
/// A runtime secret pointing at a privileged role switches the whole boundary
/// off and nothing in the logs says so: every query simply succeeds, including
/// the ones that should not. The guard turns that into a refusal to start.
/// </summary>
[Collection("Integration Tests")]
public sealed class RuntimeRoleGuardTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public async Task Guard_RefusesAPrivilegedIdentity()
    {
        await using var connection = new NpgsqlConnection(fixture.MigratorConnectionString);
        await connection.OpenAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RuntimeRoleGuard.VerifyAsync(connection, CancellationToken.None));

        Assert.Contains("row-level security", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Guard_AcceptsTheRestrictedRuntimeRole()
    {
        await RestrictedRuntimeRole.EnsureAsync(fixture.MigratorConnectionString);

        await using var connection = new NpgsqlConnection(fixture.RuntimeConnectionString);
        await connection.OpenAsync();

        await RuntimeRoleGuard.VerifyAsync(connection, CancellationToken.None);
    }

    [Theory]
    [InlineData(null, false, true)]   // outside Development the check is on by default
    [InlineData(null, true, false)]   // local Development runs as the owner, so it is off
    [InlineData(true, true, true)]    // explicitly on, even locally
    [InlineData(false, false, false)] // explicitly off, for an environment that cannot provide the role yet
    public void Guard_DefaultsToOnOutsideDevelopment(bool? configured, bool isDevelopment, bool expected)
    {
        Assert.Equal(expected, RuntimeRoleGuard.ShouldVerify(configured, isDevelopment));
    }
}
