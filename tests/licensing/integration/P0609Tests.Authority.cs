using System.Net;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0609Tests
{
    [Fact]
    public async Task ShopScopeReadsOnlyItsOperationalBoundary()
    {
        await using var fixture = await StartAsync(database, Instant);
        var (package, grouped, independent, request) = await TwoSubscriptionsAsync(fixture);
        var ownTerm = (await TermAsync(grouped.Shops[0])).GetProperty("termId").GetGuid();
        var otherTerm = (await TermAsync(independent.Shops[0])).GetProperty("termId").GetGuid();
        var otherConfirmation = (Guid)(await fixture.ScalarAsync($"""
            SELECT c.confirmation_id FROM public.payment_confirmations c JOIN public.payment_fulfilments p USING (account_id,order_id)
            WHERE p.term_id='{otherTerm:D}'
            """))!;
        var otherOrder = (Guid)(await fixture.ScalarAsync($"SELECT order_id FROM payment_confirmations WHERE confirmation_id='{otherConfirmation:D}'"))!;
        var otherRequest = await RequestRefundAsync(independent, otherOrder, otherConfirmation, 2000);
        using var billing = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        foreach (var (id, end) in new[] { (request, Boundary), (otherRequest, Boundary.AddDays(2)) })
        {
            var preview = await PreviewAsync(billing, package.Account, id, 2000, "EndAt", end);
            await ReadAsync(await ApproveAsync(billing, package.Account, id, Command(preview, 2000, "EndAt", end)),
                HttpStatusCode.Created);
            await P0610Tests.PumpRefundsAsync(fixture);
        }
        foreach (var shop in grouped.Shops)
        {
            await using var connection = new NpgsqlConnection(fixture.Hosts.Database.Login(
                fixture.Hosts.Fixture.Owner.Database!, "licensing_runtime").ConnectionString);
            await connection.OpenAsync();
            await using var tx = await connection.BeginTransactionAsync();
            await using var scope = new NpgsqlCommand("SELECT set_config('app.status_shop_id',$1,true)", connection);
            scope.Parameters.AddWithValue(shop.ShopId.ToString("D"));
            await scope.ExecuteNonQueryAsync();
            await using var read = new NpgsqlCommand("""
                SELECT NOT (rolsuper OR rolbypassrls), (SELECT count(*) FROM public.refund_access_decisions),
                    (SELECT new_access_ends_at FROM public.refund_boundary($1,$2)),
                    (SELECT count(*) FROM public.refund_boundary($1,$3))
                FROM pg_roles WHERE rolname = current_user
                """, connection);
            foreach (var id in new[] { package.Account, ownTerm, otherTerm }) read.Parameters.AddWithValue(id);
            await using var rows = await read.ExecuteReaderAsync();
            Assert.True(await rows.ReadAsync());
            Assert.True(rows.GetBoolean(0));
            Assert.Equal(0, rows.GetInt64(1));
            Assert.Equal(Boundary.UtcDateTime, rows.GetDateTime(2));
            Assert.Equal(0, rows.GetInt64(3));
            Assert.Equal(Boundary, (await TermAsync(shop)).GetProperty("accessEndsAt").GetDateTimeOffset());
        }
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("expired")]
    [InlineData("revoked")]
    [InlineData("mismatched")]
    [InlineData("support")]
    public async Task PrivateFinanceEvidenceRequiresLiveBilling(string scenario)
    {
        await using var fixture = await StartAsync(database, Instant);
        var (package, first, confirmation, order) = await RequestedAsync(fixture, 6000);
        var second = await RequestRefundAsync(package, order, confirmation, 1000);
        using var billing = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        var preview = await PreviewAsync(billing, package.Account, first, 4500, "Preserve");
        var receipt = await ReadAsync(await ApproveAsync(billing, package.Account, first, Command(preview, 4500, "Preserve")),
            HttpStatusCode.Created);
        var session = (Guid)(await fixture.ScalarAsync("SELECT session_id FROM public.refund_approvals"))!;
        var mfa = receipt.GetProperty("mfaVerifiedAt").GetDateTimeOffset();
        if (scenario == "stale") SetCredentialTime(fixture, mfa.AddMinutes(5));
        if (scenario == "expired") SetCredentialTime(fixture, mfa.AddHours(12));
        if (scenario == "support") await fixture.Checkout.Trial.Catalogue.GrantAsync(P0306Tests.BillingEmail, "support");
        if (scenario == "revoked")
            await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner,
                "UPDATE public.admin_sessions SET revoked_at=last_seen_at WHERE revoked_at IS NULL");
        await using var runtime = await SqlScope.OpenAsync(fixture, package.Account,
            actorOverride: scenario == "mismatched" ? Guid.NewGuid() : null, session: session);
        Assert.Equal(0, await runtime.CountAsync("refund_approvals"));
        var refusal = await Assert.ThrowsAsync<PostgresException>(() => runtime.ApproveAsync(second, 1000, 7500, 2));
        Assert.Equal("42501", refusal.SqlState);
        Assert.Equal(1, await fixture.CountAsync("refund_approvals"));
    }
}
