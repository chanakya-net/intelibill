using System.Data.Common;
using Intelibill.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Intelibill.Infrastructure.Data.Interceptors;

internal sealed class PostgresSessionContextInterceptor(ICurrentSessionContext currentSessionContext)
    : DbConnectionInterceptor
{
    private const string SetSessionContextSql = """
        select set_config('app.current_user_id', @user_id, false);
        select set_config('app.active_shop_id', @active_shop_id, false);
        """;

    public override void ConnectionOpened(
        DbConnection connection,
        ConnectionEndEventData eventData)
    {
        ApplySessionContext(connection);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await ApplySessionContextAsync(connection, cancellationToken);
    }

    private void ApplySessionContext(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = SetSessionContextSql;
        AddParameters(command);
        command.ExecuteNonQuery();
    }

    private async Task ApplySessionContextAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = SetSessionContextSql;
        AddParameters(command);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private void AddParameters(DbCommand command)
    {
        var userId = command.CreateParameter();
        userId.ParameterName = "@user_id";
        userId.Value = currentSessionContext.UserId?.ToString() ?? string.Empty;
        command.Parameters.Add(userId);

        var activeShopId = command.CreateParameter();
        activeShopId.ParameterName = "@active_shop_id";
        activeShopId.Value = currentSessionContext.ActiveShopId?.ToString() ?? string.Empty;
        command.Parameters.Add(activeShopId);
    }
}