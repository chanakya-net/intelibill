using System.Net;
using System.Text.Json;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0609Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0614Tests
{
    private const long Verified = 12000L;
    private const long Held = 4500L;

    /// <summary>
    /// Stories 7–9: an approved refund whose provider outcome is unknown keeps occupying the original receipt while the
    /// active payment profile switches and two operators compete for what remains. Recovery reads only the original
    /// provider, settles that reservation once, and the receipt can never be refunded beyond its verified amount.
    /// </summary>
    [Fact]
    public async Task UnknownRefundKeepsReceiptBoundAcrossSwitchAndContention()
    {
        await using var fixture = await P0609Tests.StartAsync(database, Instant, P0310Tests.Settings());
        using var owner = await fixture.Checkout.Trial.Catalogue.SignInOwnerAsync();
        await P0310Tests.SelectProfileAsync(owner, "a", 0);
        var (package, first, confirmation, order) = await RequestedAsync(fixture, 6000L);
        var second = await RequestRefundAsync(package, order, confirmation, 5000L);
        var third = await RequestRefundAsync(package, order, confirmation, 4000L);
        var deadline = (await ShopTermAsync(package, package.Shops[0])).GetProperty("paidThrough").GetDateTimeOffset();
        var (alpha, beta) = await TwoSessionsAsync(fixture);
        using var sessions = new Disposables(alpha, beta);

        var key = Guid.NewGuid();
        var held = await ApproveHeldUnknownAsync(fixture, package, alpha, first, key);
        await P0310Tests.SelectProfileAsync(owner, "b", 1);
        var winner = await CompeteForRemainderAsync(fixture, package, confirmation, alpha, beta, (second, 5000L), (third, 4000L));

        using var processed = await P0212Fixture.PostAsync(alpha, RequestPath(package.Account, first) + "/simulator",
            new { outcome = "Processed" });
        Assert.Equal(HttpStatusCode.OK, processed.StatusCode);
        fixture.At(Instant.AddMinutes(1));
        await P0610Tests.PumpRefundsAsync(fixture);
        await P0610Tests.PumpRefundsAsync(fixture);

        var recovered = await ReadAsync(await alpha.SendAsync(HttpMethod.Get,
            $"{RequestPath(package.Account, first)}/approvals/operations/{key:D}"), HttpStatusCode.OK);
        Assert.Equal(held, recovered.GetProperty("reservationId").GetGuid());
        Assert.Equal("Confirmed", (await CustomerRefundAsync(package, first)).GetProperty("status").GetString());
        await AssertReceiptBoundAsync(fixture, confirmation, Held + winner.Amount);
        await AssertOriginalProviderOnlyAsync(fixture, held);
        Assert.Equal(deadline, (await ShopTermAsync(package, package.Shops[0])).GetProperty("paidThrough").GetDateTimeOffset());
        var exhausted = Verified - Held - winner.Amount;
        await AssertCodeAsync(await TryPreviewAsync(alpha, package.Account, winner.Loser, exhausted + 1, "Preserve"),
            HttpStatusCode.Conflict, "Refund.AmountExceedsRemaining");
        var exact = await PreviewAsync(alpha, package.Account, winner.Loser, exhausted, "Preserve");
        Assert.Equal(Held + winner.Amount, exact.GetProperty("occupiedAmountPaise").GetInt64());
        Assert.Equal(exhausted, exact.GetProperty("remainingAmountPaise").GetInt64());
    }

    /// <summary>Approves the first request with Preserve, then times its only provider send out under profile A.</summary>
    private static async Task<Guid> ApproveHeldUnknownAsync(P0308Fixture fixture, Package package,
        P0110CookieBrowser billing, Guid request, Guid key)
    {
        var preview = await PreviewAsync(billing, package.Account, request, Held, "Preserve");
        var receipt = await ReadAsync(await ApproveAsync(billing, package.Account, request,
            Command(preview, Held, "Preserve", key: key)), HttpStatusCode.Created);
        using var timeout = await P0212Fixture.PostAsync(billing, RequestPath(package.Account, request) + "/simulator",
            new { outcome = "Timeout" });
        Assert.Equal(HttpStatusCode.OK, timeout.StatusCode);
        await P0610Tests.PumpRefundsAsync(fixture);
        Assert.Equal("Unknown", (await CustomerRefundAsync(package, request)).GetProperty("status").GetString());
        Assert.Equal(0L, await fixture.CountAsync("refund_applications"));
        Assert.Equal(1L, await fixture.CountAsync("refund_sends"));
        return receipt.GetProperty("reservationId").GetGuid();
    }

    private sealed record Winner(long Amount, Guid Loser);

    /// <summary>Two independently authenticated operators race fresh previews behind one held receipt lock.</summary>
    private static async Task<Winner> CompeteForRemainderAsync(P0308Fixture fixture, Package package, Guid confirmation,
        P0110CookieBrowser alpha, P0110CookieBrowser beta, (Guid Request, long Paise) a, (Guid Request, long Paise) b)
    {
        var aPreview = await PreviewAsync(alpha, package.Account, a.Request, a.Paise, "Preserve");
        var bPreview = await PreviewAsync(beta, package.Account, b.Request, b.Paise, "Preserve");
        foreach (var preview in new[] { aPreview, bPreview })
        {
            Assert.Equal(Held, preview.GetProperty("occupiedAmountPaise").GetInt64());
            Assert.Equal(Verified - Held, preview.GetProperty("remainingAmountPaise").GetInt64());
        }
        Task<HttpResponseMessage> one, two;
        await using (var holder = await HoldReceiptAsync(fixture, confirmation))
        {
            one = ApproveAsync(alpha, package.Account, a.Request, Command(aPreview, a.Paise, "Preserve"));
            two = ApproveAsync(beta, package.Account, b.Request, Command(bPreview, b.Paise, "Preserve"));
            Assert.True(await holder.WaitForBlockedAsync() >= 1, "No approval waited behind the receipt lock.");
        }
        using var first = await one;
        using var second = await two;
        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict],
            new[] { first.StatusCode, second.StatusCode }.Order().ToArray());
        var (won, lost, loserSession) = first.StatusCode == HttpStatusCode.Created ? (a, b, beta) : (b, a, alpha);
        await AssertCodeAsync(await TryPreviewAsync(loserSession, package.Account, lost.Request, lost.Paise, "Preserve"),
            HttpStatusCode.Conflict, "Refund.AmountExceedsRemaining");
        return new(won.Paise, lost.Request);
    }

    /// <summary>Occupied paise equals each unique reservation counted once and never exceeds the verified receipt.</summary>
    private static async Task AssertReceiptBoundAsync(P0308Fixture fixture, Guid confirmation, long expected)
    {
        var occupied = await fixture.ScalarAsync($"""
            SELECT coalesce(sum(a.amount_paise), 0) FROM (SELECT DISTINCT reservation_id, amount_paise
                FROM public.refund_approvals WHERE confirmation_id = '{confirmation:D}') a
            """);
        Assert.Equal(expected, Convert.ToInt64(occupied, System.Globalization.CultureInfo.InvariantCulture));
        Assert.True(expected <= Verified);
        Assert.Equal(2L, await fixture.CountAsync("refund_approvals", $"confirmation_id = '{confirmation:D}'"));
    }

    /// <summary>The held reservation sent once and settled once, all at its original simulator A pin.</summary>
    private static async Task AssertOriginalProviderOnlyAsync(P0308Fixture fixture, Guid reservation)
    {
        var only = $"reservation_id = '{reservation:D}'";
        Assert.Equal(1L, await fixture.CountAsync("refund_sends", only));
        Assert.Equal(1L, await fixture.CountAsync("refund_attempts", $"""
            {only} AND provider_account_id = '{P0310Tests.A.ProviderAccountId}' AND environment = '{P0310Tests.A.Environment}'
                AND configuration_version = '{P0310Tests.A.ConfigurationVersion}'
            """));
        Assert.Equal(1L, await fixture.CountAsync("refund_work", $"{only} AND state = 'Confirmed'"));
        Assert.Equal(1L, await fixture.CountAsync("refund_applications a", $"""
            disposition = 'Applied' AND EXISTS (SELECT FROM public.refund_attempts t
                WHERE t.decision_id = a.decision_id AND t.{only})
            """));
        Assert.Equal(0L, await fixture.CountAsync("development_gateway_refunds",
            $"provider_account_id = '{P0310Tests.B.ProviderAccountId}'"));
        Assert.Equal(0L, await fixture.CountAsync("refund_attempts",
            $"provider_account_id = '{P0310Tests.B.ProviderAccountId}'"));
        Assert.Equal(1L, await fixture.CountAsync("development_gateway_refunds",
            $"provider_account_id = '{P0310Tests.A.ProviderAccountId}' AND amount_paise = {Held}"));
    }

    private static async Task<JsonElement> CustomerRefundAsync(Package package, Guid request) =>
        (await ReadAsync(await package.Payer.Client.GetAsync(P0608Tests.RefundsPath(package.Account)), HttpStatusCode.OK))
            .GetProperty("items").EnumerateArray().Single(item => item.GetProperty("refundRequestId").GetGuid() == request);
}
