using Intelibill.Infrastructure.Options;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Intelibill.Api.Unit.Tests.Options;

public class DatabaseOptionsTests
{
    private static DatabaseOptions PasswordOptions(string password = "s3cret") => new()
    {
        Host = "localhost",
        Port = 5432,
        Database = "intelibill_dev",
        Username = "intelibill",
        Password = password,
    };

    private static DatabaseOptions EntraOptions() => new()
    {
        Host = "intelibill-pg-01.postgres.database.azure.com",
        Port = 5432,
        Database = "intelibill_prod",
        Username = "id-app-prod",
        UseEntraAuth = true,
    };

    [Fact]
    public void ToConnectionString_WithPasswordAuth_IncludesThePassword()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(PasswordOptions().ToConnectionString());

        Assert.Equal("localhost", connectionString.Host);
        Assert.Equal(5432, connectionString.Port);
        Assert.Equal("intelibill_dev", connectionString.Database);
        Assert.Equal("intelibill", connectionString.Username);
        Assert.Equal("s3cret", connectionString.Password);
        Assert.Equal(SslMode.Prefer, connectionString.SslMode);
    }

    [Fact]
    public void ToConnectionString_WithEntraAuth_OmitsThePasswordAndRequiresTls()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(EntraOptions().ToConnectionString());

        Assert.Null(connectionString.Password);
        Assert.Equal(SslMode.Require, connectionString.SslMode);
        Assert.Equal("id-app-prod", connectionString.Username);
    }

    [Fact]
    public void ToConnectionString_WithEntraAuth_IgnoresAnyConfiguredPassword()
    {
        var options = new DatabaseOptions
        {
            Host = "intelibill-pg-01.postgres.database.azure.com",
            Database = "intelibill_prod",
            Username = "id-app-prod",
            UseEntraAuth = true,
            Password = "left-over-from-the-password-era",
        };

        var connectionString = new NpgsqlConnectionStringBuilder(options.ToConnectionString());

        Assert.Null(connectionString.Password);
    }

    [Theory]
    [InlineData("pass;word")]
    [InlineData("pass=word")]
    [InlineData("pass'word")]
    public void ToConnectionString_EscapesPasswordsThatWouldCorruptInterpolation(string password)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(PasswordOptions(password).ToConnectionString());

        Assert.Equal(password, connectionString.Password);
        Assert.Equal("intelibill_dev", connectionString.Database);
    }

    [Fact]
    public void ToConnectionString_AppliesTheConfiguredPoolCeiling()
    {
        var options = new DatabaseOptions
        {
            Host = "localhost",
            Database = "intelibill_dev",
            Username = "intelibill",
            Password = "s3cret",
            MaxPoolSize = 15,
        };

        var connectionString = new NpgsqlConnectionStringBuilder(options.ToConnectionString());

        Assert.Equal(15, connectionString.MaxPoolSize);
    }

    [Fact]
    public void MaxPoolSize_DefaultsWellBelowNpgsqlsHundred()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(PasswordOptions().ToConnectionString());

        Assert.Equal(12, connectionString.MaxPoolSize);
    }

    [Fact]
    public void Validator_AcceptsPasswordAuthWithAPassword()
    {
        var result = new DatabaseOptionsValidator()
            .Validate(Microsoft.Extensions.Options.Options.DefaultName, PasswordOptions());

        Assert.Same(ValidateOptionsResult.Success, result);
    }

    [Fact]
    public void Validator_AcceptsEntraAuthWithoutAPassword()
    {
        var result = new DatabaseOptionsValidator()
            .Validate(Microsoft.Extensions.Options.Options.DefaultName, EntraOptions());

        Assert.Same(ValidateOptionsResult.Success, result);
    }

    [Fact]
    public void Validator_RejectsPasswordAuthWithoutAPassword()
    {
        var options = new DatabaseOptions
        {
            Host = "localhost",
            Database = "intelibill_dev",
            Username = "intelibill",
        };

        var result = new DatabaseOptionsValidator()
            .Validate(Microsoft.Extensions.Options.Options.DefaultName, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("Database:Password", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_RejectsEntraAuthCarryingALeftoverPassword()
    {
        var options = new DatabaseOptions
        {
            Host = "intelibill-pg-01.postgres.database.azure.com",
            Database = "intelibill_prod",
            Username = "id-app-prod",
            UseEntraAuth = true,
            Password = "left-over-from-the-password-era",
        };

        var result = new DatabaseOptionsValidator()
            .Validate(Microsoft.Extensions.Options.Options.DefaultName, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("Database:UseEntraAuth", StringComparison.Ordinal));
    }
}
