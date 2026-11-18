using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0311Tests
{
    [Fact]
    public async Task SnapshotUsesUtcEvenWhenTheDatabaseSessionUsesAnotherTimeZone()
    {
        await using var fixture = await P0302Fixture.StartAsync(database, new() { Now = WholeSecondStart });
        var purchase = await VerifiedAsync(fixture);
        var receipt = Assert.Single((await purchase.Payer.Client.GetFromJsonAsync<PaymentConfirmationV1[]>(PaymentPath(purchase.Quote) + "/confirmations"))!);
        await using var connection = new NpgsqlConnection(fixture.Hosts.LicensingOwner.ConnectionString);
        await connection.OpenAsync();
        await using var timezone = new NpgsqlCommand("SET TIME ZONE 'Asia/Kolkata'", connection);
        await timezone.ExecuteNonQueryAsync();
        await using var command = new NpgsqlCommand("""
            SELECT public.payment_confirmation_snapshot(confirmation_id, account_id, order_id, attempt_id,
                manual_receipt_id, issued_at, 'Retained')::text FROM public.payment_confirmations
            """, connection);
        var snapshot = JsonSerializer.Deserialize<PaymentConfirmationSnapshotV1>((string)(await command.ExecuteScalarAsync())!, JsonSerializerOptions.Web)!;
        Assert.Equal(TimeSpan.Zero, snapshot.Receipt.VerifiedAt.Offset);
        Assert.Equal(TimeSpan.Zero, snapshot.Receipt.IssuedAt.Offset);
        Assert.Equal(TimeSpan.Zero, snapshot.EffectiveAt.Offset);
        Assert.Equal(TimeSpan.Zero, snapshot.Terms.AcceptedAt.Offset);
        Assert.Equal(receipt.VerifiedAt, snapshot.Receipt.VerifiedAt);
    }
}
