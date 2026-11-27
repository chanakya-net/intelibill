using System.Net;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0609Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0610Tests
{
    [Fact]
    public async Task LostSendResponseAndLeaseExpiryNeverCauseAnotherPost()
    {
        await using var fixture = await P0609Tests.StartAsync(database, Instant);
        var (package, request, _, _) = await RequestedAsync(fixture, 6000);
        using var billing = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        var review = await PreviewAsync(billing, package.Account, request, 4500, "Preserve");
        await ReadAsync(await ApproveAsync(billing, package.Account, request, Command(review, 4500, "Preserve")), HttpStatusCode.Created);

        var runtime = fixture.Hosts.Database.Login(fixture.Hosts.Fixture.Owner.Database!, "licensing_runtime");
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var later = Instant.AddMinutes(2);
        Assert.Equal(1L, await ScalarAsync(runtime, "SELECT count(*) FROM public.claim_refund($1,$2)", Instant, first));
        Assert.Equal(0L, await ScalarAsync(runtime, "SELECT count(*) FROM public.claim_refund($1,$2)", Instant, second));
        Assert.Equal(true, await ScalarAsync(runtime, "SELECT public.start_refund_send($1,$2)", first, Instant));
        Assert.Equal(false, await ScalarAsync(runtime, "SELECT public.start_refund_send($1,$2)", first, Instant));
        Assert.Equal(1L, await ScalarAsync(runtime, "SELECT count(*) FROM public.claim_refund($1,$2)", later, second));
        Assert.Equal(false, await ScalarAsync(runtime, "SELECT public.start_refund_send($1,$2)", second, later));
        Assert.Equal(false, await ScalarAsync(runtime, "SELECT public.start_refund_send($1,$2)", first, later));
        Assert.Equal(1L, await fixture.CountAsync("refund_sends"));
    }

    [Fact]
    public async Task UnverifiedPostReferenceCannotPreventRecoveryByClientReceipt()
    {
        await using var fixture = await P0609Tests.StartAsync(database, Instant);
        var (package, request, _, _) = await RequestedAsync(fixture, 6000);
        using var finance = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        await ApproveEndAsync(finance, package.Account, request, Instant.AddDays(2));
        var runtime = fixture.Hosts.Database.Login(fixture.Hosts.Fixture.Owner.Database!, "licensing_runtime");
        var lease = Guid.NewGuid();
        Assert.Equal(1L, await ScalarAsync(runtime, "SELECT count(*) FROM claim_refund($1,$2)", Instant, lease));
        Assert.Equal(true, await ScalarAsync(runtime, "SELECT start_refund_send($1,$2)", lease, Instant));
        Assert.Equal(0L, await ScalarAsync(runtime,
            "SELECT count(*) FROM finish_refund($1,$2,'Unknown','LookupUnproven','rfnd_unverified')", lease, Instant));
        Assert.Equal(0L, await fixture.CountAsync("refund_bindings"));
        Assert.Equal(1L, await fixture.CountAsync("refund_observations", "refund_reference='rfnd_unverified'"));
        var next = Guid.NewGuid();
        Assert.Equal(true, await ScalarAsync(runtime,
            "SELECT sent AND refund_reference IS NULL FROM claim_refund($1,$2)", Instant.AddHours(1), next));
    }

    [Fact]
    public async Task OneProviderMovementCannotSettleTwoApprovedReservations()
    {
        await using var fixture = await P0609Tests.StartAsync(database, Instant);
        var (package, first, confirmation, order) = await RequestedAsync(fixture, 6000);
        var second = await RequestRefundAsync(package, order, confirmation, 6000);
        using var finance = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        var runtime = fixture.Hosts.Database.Login(fixture.Hosts.Fixture.Owner.Database!, "licensing_runtime");
        foreach (var request in new[] { first, second })
        {
            var preview = await PreviewAsync(finance, package.Account, request, 4500, "Preserve");
            await ReadAsync(await ApproveAsync(finance, package.Account, request, Command(preview, 4500, "Preserve")),
                HttpStatusCode.Created);
            var lease = Guid.NewGuid();
            Assert.Equal(1L, await ScalarAsync(runtime, "SELECT count(*) FROM claim_refund($1,$2)", Instant, lease));
            Assert.Equal(true, await ScalarAsync(runtime, "SELECT start_refund_send($1,$2)", lease, Instant));
            // Feed the same authenticated-read identity through the worker's persistence boundary.
            Assert.Equal(0L, await ScalarAsync(runtime,
                "SELECT count(*) FROM finish_refund($1,$2,'Confirmed','OriginalProviderRead','rfnd_fixture_shared')", lease, Instant));
        }
        Assert.Equal(1L, await fixture.CountAsync("refund_bindings"));
        Assert.Equal(1L, await fixture.CountAsync("refund_work", "state='Confirmed'"));
        Assert.Equal(1L, await fixture.CountAsync("refund_work", "state='Unknown' AND reason='ConflictingReference'"));
        Assert.Equal(1L, await fixture.CountAsync("refund_applications"));
        Assert.Equal(2L, await fixture.CountAsync("refund_approvals", "amount_paise=4500"));
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnectionStringBuilder runtime, string sql, params object[] values)
    {
        await using var connection = new NpgsqlConnection(runtime.ConnectionString);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, connection);
        foreach (var value in values) cmd.Parameters.AddWithValue(value);
        return await cmd.ExecuteScalarAsync();
    }
}
