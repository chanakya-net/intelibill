using Intelibill.Licensing.Application.Payments;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0310Tests
{
    [Fact]
    public async Task SimulatorDatabaseScopeCannotReadAnotherConfiguredIdentity()
    {
        await using var fixture = await P0302Fixture.StartAsync(database, new() { Now = P0302Fixture.WholeSecondStart }, Settings());
        using var scope = fixture.Hosts.Licensing.Services.CreateScope();
        var first = scope.ServiceProvider.GetRequiredService<IPaymentGatewayResolver>().ForPin(A)!;
        await first.CreateCheckoutAsync(new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 12000, "INR"), default);
        await using var connection = new NpgsqlConnection(fixture.Hosts.Fixture.Runtime.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var configure = new NpgsqlCommand("""
            SELECT set_config('app.development_gateway', 'on', true),
                set_config('app.gateway_account', 'simulator-b', true),
                set_config('app.gateway_environment', 'Development', true),
                set_config('app.gateway_configuration', 'b/1', true)
            """, connection, transaction);
        await configure.ExecuteNonQueryAsync();
        await using var read = new NpgsqlCommand("SELECT count(*) FROM public.development_gateway_sessions", connection, transaction);
        Assert.Equal(0L, await read.ExecuteScalarAsync());
    }
}
