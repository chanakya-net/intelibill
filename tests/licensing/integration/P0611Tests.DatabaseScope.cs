using Npgsql;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0611Tests
{
    [Fact]
    public async Task RuntimeRoleCannotInsertInvalidEvidenceOrCreateSiblingVersion()
    {
        await using var fixture = await StartAsync(database);
        var purchase = await fixture.AcceptedQuoteAsync(1);
        using var billing = await SignInBillingAsync(fixture);
        var actor = await CurrentActorAsync(fixture);
        foreach (var fault in new[] { "hash", "length", "prefix", "version", "oversize" })
        {
            await using var sql = await DocumentSql.OpenAsync(fixture, purchase.Quote, actor, purchase.Account);
            var denied = await Assert.ThrowsAsync<PostgresException>(() => sql.InsertAsync(purchase.Quote, actor, fault));
            Assert.Equal("23514", denied.SqlState);
        }
        await AttachAsync(billing, Path(purchase.Quote), Pdf());
        await using var second = await DocumentSql.OpenAsync(fixture, purchase.Quote, actor, purchase.Account);
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => second.InsertAsync(purchase.Quote, actor));
        Assert.Equal(("23505", "billing_document_order_lineage"), (duplicate.SqlState, duplicate.ConstraintName));
        Assert.Equal(1L, await fixture.CountAsync("billing_document_versions"));
    }

    [Fact]
    public async Task RuntimeRoleCannotReadOrWriteWithoutMatchingLiveBillingScope()
    {
        await using var fixture = await StartAsync(database);
        var purchase = await fixture.AcceptedQuoteAsync(1);
        var other = await P0209Fixture.CreateAccountAsync(purchase.Payer.Client, "Other account");
        using var billing = await SignInBillingAsync(fixture);
        var finance = await CurrentActorAsync(fixture);
        await AttachAsync(billing, Path(purchase.Quote), Pdf());
        await AssertSqlDenialAsync(fixture, purchase.Quote, finance, other, finance);
        await AssertSqlDenialAsync(fixture, purchase.Quote, finance, purchase.Account, null);
        using var owner = await fixture.Trial.Catalogue.SignInOwnerAsync();
        var ownerEvidence = await CurrentActorAsync(fixture);
        await AssertSqlDenialAsync(fixture, purchase.Quote, ownerEvidence, purchase.Account, ownerEvidence);
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner,
            $"UPDATE public.admin_sessions SET revoked_at = last_seen_at WHERE id = '{finance.Session:D}'");
        await AssertSqlDenialAsync(fixture, purchase.Quote, finance, purchase.Account, finance);
        Assert.Equal(1L, await fixture.CountAsync("billing_document_versions"));
    }

    private static async Task AssertSqlDenialAsync(P0302Fixture fixture, CheckoutQuoteV1 quote, Actor writer,
        Guid accountScope, Actor? actorScope)
    {
        await using var sql = await DocumentSql.OpenAsync(fixture, quote, actorScope, accountScope);
        Assert.Equal(0L, await sql.CountAsync());
        var denied = await Assert.ThrowsAsync<PostgresException>(() => sql.InsertAsync(quote, writer));
        Assert.Equal("42501", denied.SqlState);
    }

    private sealed record Actor(Guid Admin, Guid Session, DateTime Mfa);

    private static async Task<Actor> CurrentActorAsync(P0302Fixture fixture)
    {
        await using var connection = new NpgsqlConnection(fixture.Hosts.LicensingOwner.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT admin_id, id, mfa_verified_at FROM public.admin_sessions ORDER BY mfa_verified_at DESC LIMIT 1
            """, connection);
        await using var read = await command.ExecuteReaderAsync();
        Assert.True(await read.ReadAsync());
        return new(read.GetGuid(0), read.GetGuid(1), read.GetDateTime(2));
    }

    private sealed class DocumentSql(NpgsqlConnection connection, NpgsqlTransaction transaction) : IAsyncDisposable
    {
        internal static async Task<DocumentSql> OpenAsync(P0302Fixture fixture, CheckoutQuoteV1 quote, Actor? actor, Guid account)
        {
            var connection = new NpgsqlConnection(fixture.Hosts.Database.Login(fixture.Hosts.Fixture.Owner.Database!,
                "licensing_runtime").ConnectionString);
            await connection.OpenAsync();
            var transaction = await connection.BeginTransactionAsync();
            await using var scope = new NpgsqlCommand("""
                SELECT set_config('app.billing_account_id', $1, true), set_config('app.manual_order_id', $2, true),
                    set_config('app.manual_admin_id', $3, true), set_config('app.manual_session_id', $4, true)
                """, connection, transaction);
            foreach (var value in new[] { account.ToString("D"), quote.OrderId.ToString("D"),
                actor?.Admin.ToString("D") ?? "", actor?.Session.ToString("D") ?? "" })
                scope.Parameters.AddWithValue(value);
            await scope.ExecuteNonQueryAsync();
            return new(connection, transaction);
        }

        internal async Task<long> CountAsync()
        {
            await using var count = new NpgsqlCommand("SELECT count(*) FROM public.billing_document_versions", connection, transaction);
            return (long)(await count.ExecuteScalarAsync())!;
        }

        internal async Task InsertAsync(CheckoutQuoteV1 quote, Actor actor, string? fault = null)
        {
            var bytes = fault switch { "prefix" => "not a pdf"u8.ToArray(), "oversize" => Pdf(MaxBytes + 1), _ => Pdf() };
            await using var insert = new NpgsqlCommand("""
                INSERT INTO public.billing_document_versions
                    (account_id, order_id, version_id, document_id, version_number, supersedes_version_id,
                    kind, media_type, file_name, byte_length, content_sha256, content, issuer_name, issuer_reference,
                    issued_on, operation_key, payload_hash, admin_id, session_id, mfa_verified_at, uploaded_at)
                VALUES ($1, $2, gen_random_uuid(), gen_random_uuid(), $3, NULL, 'AccountingInvoice',
                    'application/pdf', 'fixture-invoice.pdf', $4, $5, $6, 'Fixture Accounting', 'DIRECT-INSERT',
                    $7, gen_random_uuid(), repeat('a',64), $8, $9, $10, $11)
                """, connection, transaction);
            foreach (var value in new object[] { quote.AccountId, quote.OrderId, fault == "version" ? 2 : 1,
                bytes.Length + (fault == "length" ? 1 : 0), fault == "hash" ? new string('b', 64) : Hash(bytes),
                bytes, IssueDate, actor.Admin, actor.Session, actor.Mfa, Instant })
                insert.Parameters.AddWithValue(value);
            Assert.Equal(1, await insert.ExecuteNonQueryAsync());
        }

        public async ValueTask DisposeAsync()
        {
            await transaction.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
