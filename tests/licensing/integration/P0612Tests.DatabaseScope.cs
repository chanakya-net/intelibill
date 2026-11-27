using Intelibill.Domain.Enums;
using Intelibill.Shared.Contracts.V1;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0612Tests
{
    [Fact]
    public async Task RuntimeRoleCannotReadBytesWithoutLiveMemberAndConsumedDelegation()
    {
        await using var fixture = await P0611Tests.StartAsync(database);
        var purchase = await P0311Tests.VerifiedAsync(fixture);
        var otherAccount = await P0209Fixture.CreateAccountAsync(purchase.Payer.Client, "Foreign account");
        var outsider = await P0209Fixture.SignInAsync(fixture.Hosts, ShopRole.Owner);
        using var billing = await P0611Tests.SignInBillingAsync(fixture);
        var pdf = P0611Tests.Pdf();
        await P0611Tests.AttachAsync(billing, P0611Tests.Path(purchase.Quote), pdf);

        // Right account + live member actor, but no delegation use consumed in this transaction -> 0 rows.
        await AssertMemberReadAsync(fixture, purchase.Account, purchase.Payer.UserId, action: null, expectedCount: 0, expectedBytes: null);
        // Right account + outsider actor -> 0 rows.
        await AssertMemberReadAsync(fixture, purchase.Account, outsider.UserId, action: null, expectedCount: 0, expectedBytes: null);
        // Foreign account scope + member actor -> 0 rows.
        await AssertMemberReadAsync(fixture, otherAccount, purchase.Payer.UserId, action: null, expectedCount: 0, expectedBytes: null);
        // Right account + no actor -> 0 rows.
        await AssertMemberReadAsync(fixture, purchase.Account, actor: null, action: null, expectedCount: 0, expectedBytes: null);
        // Right account + live member + unrelated delegation action in same tx -> 0 rows.
        await AssertMemberReadAsync(fixture, purchase.Account, purchase.Payer.UserId, MerchantBillingDelegationV1.ReadAccount, expectedCount: 0, expectedBytes: null);
        // Right account + live member + consumed billing.document.download in same tx -> 1 row with exact bytes.
        await AssertMemberReadAsync(fixture, purchase.Account, purchase.Payer.UserId, BillingDocumentsV1.Download, expectedCount: 1, expectedBytes: pdf);

        Assert.Equal(1L, await fixture.CountAsync("billing_document_versions"));
    }

    private static async Task AssertMemberReadAsync(P0302Fixture fixture, Guid accountScope, Guid? actor,
        string? action, long expectedCount, byte[]? expectedBytes)
    {
        await using var connection = new NpgsqlConnection(fixture.Hosts.Database.Login(
            fixture.Hosts.Fixture.Owner.Database!, "licensing_runtime").ConnectionString);
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        await using (var set = new NpgsqlCommand(
            "SELECT set_config('app.billing_account_id', $1, true), set_config('app.billing_actor_id', $2, true)",
            connection, tx))
        {
            set.Parameters.AddWithValue(accountScope.ToString("D"));
            set.Parameters.AddWithValue(actor?.ToString("D") ?? "");
            await set.ExecuteNonQueryAsync();
        }
        if (action is not null && actor is { } actorId)
        {
            await using var use = new NpgsqlCommand("""
                INSERT INTO public.merchant_billing_delegation_uses
                    (issuer, replay_id, account_id, actor_id, action, request_id, key_id, issued_at, expires_at, consumed_at)
                VALUES ($1, $2, $3, $4, $5, $6, 'fixture-key', $7, $8, $7)
                """, connection, tx);
            use.Parameters.AddWithValue(MerchantBillingDelegationV1.Issuer);
            use.Parameters.AddWithValue(Guid.NewGuid());
            use.Parameters.AddWithValue(accountScope);
            use.Parameters.AddWithValue(actorId);
            use.Parameters.AddWithValue(action);
            use.Parameters.AddWithValue(Guid.NewGuid());
            use.Parameters.AddWithValue(P0611Tests.Instant);
            use.Parameters.AddWithValue(P0611Tests.Instant.AddSeconds(30));
            await use.ExecuteNonQueryAsync();
        }
        await using (var count = new NpgsqlCommand("SELECT count(*) FROM public.billing_document_versions", connection, tx))
            Assert.Equal(expectedCount, (long)(await count.ExecuteScalarAsync())!);
        await using (var read = new NpgsqlCommand("SELECT content FROM public.billing_document_versions", connection, tx))
        {
            var raw = await read.ExecuteScalarAsync();
            if (expectedBytes is null) Assert.Null(raw);
            else Assert.Equal(expectedBytes, (byte[])raw!);
        }
        await tx.RollbackAsync();
    }
}
