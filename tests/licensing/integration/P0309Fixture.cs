using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

internal static class P0309Fixture
{
    /// <summary>Prepare a V1 quote before acceptance for a populated predecessor migration test.</summary>
    internal static async Task<CheckoutQuoteV1> RepresentLegacyQuoteAsync(NpgsqlConnectionStringBuilder database,
        CheckoutQuoteV1 written)
    {
        var legacy = written with { FreshStart = null };
        legacy = legacy with { SnapshotHash = CheckoutOrdersV1.SnapshotHash(legacy) };
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            BEGIN;
            ALTER TABLE public.checkout_orders DISABLE TRIGGER checkout_retained;
            UPDATE public.checkout_orders SET snapshot = @snapshot::jsonb, snapshot_hash = @hash WHERE order_id = @order;
            ALTER TABLE public.checkout_orders ENABLE TRIGGER checkout_retained;
            COMMIT;
            """, connection);
        command.Parameters.AddWithValue("snapshot", JsonSerializer.Serialize(legacy, JsonSerializerOptions.Web));
        command.Parameters.AddWithValue("hash", legacy.SnapshotHash);
        command.Parameters.AddWithValue("order", legacy.OrderId);
        await command.ExecuteNonQueryAsync();
        return legacy;
    }

    /// <summary>Use the paid term and links produced by checkout, retaining only the original V1 row layout.</summary>
    internal static Task RepresentLegacyFulfilmentAsync(NpgsqlConnectionStringBuilder database, Guid order) =>
        LicensingPostgreSqlFixture.ExecuteAsync(database, $"""
            BEGIN;
            ALTER TABLE public.paid_fresh_starts DISABLE TRIGGER retain_rows;
            DELETE FROM public.paid_fresh_starts WHERE order_id = '{order:D}';
            ALTER TABLE public.paid_fresh_starts ENABLE TRIGGER retain_rows;
            COMMIT;
            """);
}
