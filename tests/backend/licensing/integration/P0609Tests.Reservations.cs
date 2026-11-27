using System.Net;
using System.Text.Json;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0609Tests
{
    [Fact]
    public async Task ApproveFullPermittedAmountOnAVerifiedManualReceipt()
    {
        await using var fixture = await StartAsync(database, P0210Fixture.Start, P0306Tests.Settings);
        var purchase = await fixture.Checkout.AcceptedQuoteAsync(1);
        using var billing = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        using var confirmed = await P0212Fixture.PostAsync(billing, P0306Tests.Path(purchase.Quote),
            P0306Tests.Receipt(purchase.Quote, P0210Fixture.Start));
        Assert.Equal(HttpStatusCode.Created, confirmed.StatusCode);
        var confirmation = await P0608Tests.ConfirmationIdAsync(purchase.Payer.Client, purchase.Account, purchase.Quote.OrderId);
        var package = new Package(purchase.Payer, purchase.Account, purchase.Shops, Guid.Empty);
        var request = await RequestRefundAsync(package, purchase.Quote.OrderId, confirmation, 6000L);

        var preview = await PreviewAsync(billing, package.Account, request, 6000L, "Preserve");
        Assert.Equal("Manual", preview.GetProperty("paymentSource").GetString());
        Assert.Equal("fixture-bank", preview.GetProperty("providerAccountId").GetString());
        var receipt = await ReadAsync(await ApproveAsync(billing, package.Account, request,
            Command(preview, 6000L, "Preserve")), HttpStatusCode.Created);

        Assert.Equal(6000L, receipt.GetProperty("approvedAmountPaise").GetInt64());
        Assert.Equal(6000L, receipt.GetProperty("requestedAmountPaise").GetInt64());
        Assert.Equal(6000L, receipt.GetProperty("remainingAfterPaise").GetInt64());
        Assert.Equal(1L, await fixture.CountAsync("refund_approvals", "manual_receipt_id IS NOT NULL AND attempt_id IS NULL"));
        await P0610Tests.PumpRefundsAsync(fixture);
        Assert.Equal(1L, await fixture.CountAsync("refund_applications", "disposition='Applied'"));
        Assert.Equal(0L, await fixture.CountAsync("refund_attempts"));
        Assert.Equal(0L, await fixture.CountAsync("refund_sends"));
        // The customer's own request still states what they asked for.
        Assert.Equal(1L, await fixture.CountAsync("refund_requests", "amount_paise = 6000"));
        Assert.Equal(1L, await fixture.CountAsync("payment_fulfilments"));
        Assert.Equal(0L, await fixture.CountAsync("operational_restrictions"));
    }

    [Fact]
    public async Task ConcurrentApprovalsRespectOneVerifiedReceipt()
    {
        await using var fixture = await StartAsync(database, Instant);
        var package = await fixture.PackageAsync(1, ["Contended Shop"]);
        var (purchase, _) = await PurchaseAsync(package, "Month");
        var confirmation = await P0608Tests.ConfirmationIdAsync(package.Payer.Client, package.Account, purchase.OrderId);
        var first = await RequestRefundAsync(package, purchase.OrderId, confirmation, 8000L);
        var second = await RequestRefundAsync(package, purchase.OrderId, confirmation, 7000L);
        var (alpha, beta) = await TwoSessionsAsync(fixture);
        using var sessions = new Disposables(alpha, beta);

        // Both operators review revision 0 of the same 12,000-paise receipt.
        var firstPreview = await PreviewAsync(alpha, package.Account, first, 8000L, "Preserve");
        var secondPreview = await PreviewAsync(beta, package.Account, second, 7000L, "Preserve");
        Assert.Equal(0L, secondPreview.GetProperty("receiptVersion").GetInt64());

        // A third, independent transaction holds the original payment's fence; the writers must wait behind it.
        Task<HttpResponseMessage> one, two;
        long blocked;
        await using (var holder = await HoldReceiptAsync(fixture, confirmation))
        {
            one = ApproveAsync(alpha, package.Account, first, Command(firstPreview, 8000L, "Preserve"));
            two = ApproveAsync(beta, package.Account, second, Command(secondPreview, 7000L, "Preserve"));
            blocked = await holder.WaitForBlockedAsync();
            Assert.True(blocked >= 1, "No approval waited behind the receipt lock.");
            Assert.Equal(0L, await fixture.CountAsync("refund_approvals"));
        }

        using var a = await one;
        using var b = await two;
        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict], new[] { a.StatusCode, b.StatusCode }.Order().ToArray());
        var (loserSession, loserRequest, loserAmount, loserResponse) = a.StatusCode == HttpStatusCode.Conflict
            ? (alpha, first, 8000L, a) : (beta, second, 7000L, b);
        Assert.Equal("Refund.PreviewStale", (await JsonDocument.ParseAsync(await loserResponse.Content.ReadAsStreamAsync()))
            .RootElement.GetProperty("code").GetString());
        Assert.Equal(1L, await fixture.CountAsync("refund_approvals"));

        // Refreshing does not help the loser: its amount no longer fits what the receipt has free.
        var remaining = 12000L - (loserAmount == 8000L ? 7000L : 8000L);
        await AssertCodeAsync(await TryPreviewAsync(loserSession, package.Account, loserRequest, loserAmount, "Preserve"),
            HttpStatusCode.Conflict, "Refund.AmountExceedsRemaining");
        await AssertCodeAsync(await TryPreviewAsync(loserSession, package.Account, loserRequest, remaining + 1, "Preserve"),
            HttpStatusCode.Conflict, "Refund.AmountExceedsRemaining");

        // The exact remainder can be exhausted; one more paisa on the same receipt is refused.
        var exact = await PreviewAsync(loserSession, package.Account, loserRequest, remaining, "Preserve");
        Assert.Equal(1L, exact.GetProperty("receiptVersion").GetInt64());
        Assert.Equal(0L, exact.GetProperty("proposedRemainingAmountPaise").GetInt64());
        var exhausted = await ReadAsync(await ApproveAsync(loserSession, package.Account, loserRequest,
            Command(exact, remaining, "Preserve")), HttpStatusCode.Created);
        Assert.Equal(0L, exhausted.GetProperty("remainingAfterPaise").GetInt64());
        var third = await RequestRefundAsync(package, purchase.OrderId, confirmation, 1L);
        await AssertCodeAsync(await TryPreviewAsync(alpha, package.Account, third, 1L, "Preserve"),
            HttpStatusCode.Conflict, "Refund.AmountExceedsRemaining");
        Assert.Equal(12000L, (decimal)(await fixture.ScalarAsync("SELECT sum(amount_paise) FROM public.refund_approvals"))!);
        await RecordAsync("concurrent-approvals", new
        {
            verifiedPaise = 12000, firstRequestedPaise = 8000, secondRequestedPaise = 7000, backendsBlockedByReceiptHolder = blocked,
            firstStatus = (int)a.StatusCode, secondStatus = (int)b.StatusCode, loserCode = "Refund.PreviewStale",
            loserAfterRefresh = "Refund.AmountExceedsRemaining", exactRemainderApprovedPaise = remaining,
            extraPaisaRefused = true, approvals = await fixture.CountAsync("refund_approvals"), reservedTotalPaise = 12000,
        });
    }

    [Fact]
    public async Task SeparateReceiptsKeepSeparateLimits()
    {
        await using var fixture = await StartAsync(database, Instant);
        var package = await fixture.PackageAsync(1, ["Renewing Shop"]);
        var (purchase, term) = await PurchaseAsync(package, "Month");
        var confirmation = await P0608Tests.ConfirmationIdAsync(package.Payer.Client, package.Account, purchase.OrderId);
        var whole = await RequestRefundAsync(package, purchase.OrderId, confirmation, 12000L);
        using var billing = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        var full = await PreviewAsync(billing, package.Account, whole, 12000L, "Preserve");
        await ReadAsync(await ApproveAsync(billing, package.Account, whole, Command(full, 12000L, "Preserve")), HttpStatusCode.Created);

        // The renewal payment is a separate verified receipt: its own revision 0 and its own untouched limit.
        var (renewal, _) = await RenewAsync(package, term, "Month");
        var other = await P0608Tests.ConfirmationIdAsync(package.Payer.Client, package.Account, renewal.OrderId);
        var fresh = await RequestRefundAsync(package, renewal.OrderId, other, 12000L);
        var separate = await PreviewAsync(billing, package.Account, fresh, 12000L, "Preserve");
        Assert.Equal(12000L, separate.GetProperty("remainingAmountPaise").GetInt64());
        Assert.Equal(0L, separate.GetProperty("receiptVersion").GetInt64());
        // The exhausted first receipt stays exhausted.
        var again = await RequestRefundAsync(package, purchase.OrderId, confirmation, 1L);
        await AssertCodeAsync(await TryPreviewAsync(billing, package.Account, again, 1L, "Preserve"),
            HttpStatusCode.Conflict, "Refund.AmountExceedsRemaining");
    }

    /// <summary>Two independent live sessions of two different Billing operators.</summary>
    internal static async Task<(P0110CookieBrowser Alpha, P0110CookieBrowser Beta)> TwoSessionsAsync(P0308Fixture fixture)
    {
        var sessions = await BillingSessionsAsync(fixture, P0306Tests.BillingEmail, "second.billing@example.test");
        return (sessions[0], sessions[1]);
    }

    /// <summary>Enrols every operator under one owner session, then signs each in (password + TOTP).</summary>
    internal static async Task<P0110CookieBrowser[]> BillingSessionsAsync(P0308Fixture fixture, params string[] emails)
    {
        var catalogue = fixture.Checkout.Trial.Catalogue;
        var seeds = new List<string>();
        using (var owner = await catalogue.SignInOwnerAsync())
            foreach (var email in emails)
            {
                seeds.Add(await catalogue.EnrollOperatorAsync(owner, email));
                await P0206Fixture.GrantAsync(owner, email, email.StartsWith("support", StringComparison.Ordinal) ? "support" : "billing");
            }
        var sessions = new List<P0110CookieBrowser>();
        foreach (var (email, seed) in emails.Zip(seeds))
            sessions.Add(await catalogue.SignInOperatorAsync(seed, email));
        return [.. sessions];
    }

    internal static async Task<ReceiptHold> HoldReceiptAsync(P0308Fixture fixture, Guid confirmation)
    {
        var connection = new NpgsqlConnection(fixture.Hosts.LicensingOwner.ConnectionString);
        await connection.OpenAsync();
        var transaction = await connection.BeginTransactionAsync();
        await using var take = new NpgsqlCommand("SELECT public.lock_refund_receipt($1), pg_backend_pid()", connection, transaction);
        take.Parameters.AddWithValue(confirmation);
        await using var reader = await take.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new(fixture, connection, transaction, reader.GetInt32(1));
    }

    /// <summary>An open transaction holding the original payment's receipt lock until disposed.</summary>
    internal sealed class ReceiptHold(P0308Fixture fixture, NpgsqlConnection connection, NpgsqlTransaction transaction, int pid)
        : IAsyncDisposable
    {
        /// <summary>Waits until at least one other backend is blocked by this holder, as PostgreSQL reports it.</summary>
        internal async Task<long> WaitForBlockedAsync()
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                var blocked = (long)(await fixture.ScalarAsync(
                    $"SELECT count(*) FROM pg_stat_activity WHERE {pid} = ANY(pg_blocking_pids(pid))"))!;
                if (blocked > 0) return blocked;
                await Task.Delay(20);
            }
            return 0;
        }

        public async ValueTask DisposeAsync()
        {
            await transaction.RollbackAsync();
            await transaction.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    internal sealed class Disposables(params IDisposable[] items) : IDisposable
    {
        public void Dispose()
        {
            foreach (var item in items) item.Dispose();
        }
    }
}
