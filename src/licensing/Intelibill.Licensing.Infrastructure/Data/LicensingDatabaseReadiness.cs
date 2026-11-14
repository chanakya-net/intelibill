using Npgsql;

namespace Intelibill.Licensing.Infrastructure.Data;

public sealed class LicensingDatabaseReadiness(NpgsqlDataSource dataSource)
{
    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var command = dataSource.CreateCommand(LicensingRuntimeRolePolicy.Query);
            return await command.ExecuteScalarAsync(cancellationToken) is true;
        }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException)
        {
            return false;
        }
    }
}
