using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;
using Intelibill.Licensing.Application.Payments;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0306Tests
{
    [Fact]
    public async Task InvalidOrUnauthorizedReceiptCannotConfirmMoneyOrAccess()
    {
        var now = P0210Fixture.Start;
        await using var fixture = await StartAsync(database, new P0201Clock { Now = now }, Settings);
        var (_, _, _, quote) = await fixture.AcceptedQuoteAsync(1);
        using var billing = await SignInBillingAsync(fixture);
        var path = Path(quote);
        var command = Receipt(quote, now);
        foreach (var invalid in new[]
        {
            command with { AmountPaise = command.AmountPaise - 1 },
            command with { AmountPaise = command.AmountPaise + 1 },
            command with { AmountPaise = command.AmountPaise / 2 },
            Receipt(quote, now) with { AmountPaise = command.AmountPaise / 2 },
            command with { Currency = "USD" }, command with { Verified = false },
            command with { Reference = "" }, command with { Evidence = "invalid" }, command with { Reason = "" },
            command with { ReceivedAt = now.AddSeconds(1) },
        })
            await AssertCodeAsync(billing, path, invalid, "ManualPayment.Invalid");
        var overrideShops = JsonSerializer.SerializeToNode(command, JsonSerializerOptions.Web)!.AsObject();
        overrideShops["shopIds"] = new JsonArray();
        await AssertCodeAsync(billing, path, overrideShops, "ManualPayment.Invalid");

        using var owner = await fixture.Trial.Catalogue.SignInOwnerAsync();
        await AssertCodeAsync(owner, path, command, "ManualPayment.AccessDenied");
        await AssertCodeAsync(fixture.Trial.CatalogueManager!, path, command, "ManualPayment.AccessDenied");
        using var foreign = await P0212Fixture.PostAsync(billing, path, command, "https://foreign.example");
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        using var noCsrf = await billing.SendAsync(HttpMethod.Post, path, command);
        Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);
        // The owner sign-in above advanced the fixture clock by 30 seconds.
        fixture.Trial.Catalogue.Clock.Advance(TimeSpan.FromMinutes(4.5));
        await AssertCodeAsync(billing, path, command, "ManualPayment.RecentMfaRequired");
        Assert.Equal(0L, await fixture.CountAsync("manual_payment_receipts"));
        Assert.Equal(0L, await fixture.CountAsync("payment_fulfilments"));
    }

    [Fact]
    public async Task ReplayAndSecondReceiptRespectFrozenOrderRevision()
    {
        var now = P0210Fixture.Start;
        await using var fixture = await StartAsync(database, new P0201Clock { Now = now }, Settings);
        var (_, _, _, quote) = await fixture.AcceptedQuoteAsync(1);
        using var billing = await SignInBillingAsync(fixture);
        var path = Path(quote);
        var command = Receipt(quote, now);
        using var stale = await P0212Fixture.PostAsync(billing, path, command with { ExpectedRevision = quote.Revision + 1 });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var original = await ConfirmAsync(billing, path, command, "Fulfilled");
        await AssertCodeAsync(billing, path, command with { Reason = "Different reason" }, "ManualPayment.PayloadMismatch");
        using var replay = await P0212Fixture.PostAsync(billing, path, command);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(original.GetProperty("receiptId").GetGuid(), (await P0206Fixture.BodyAsync(replay)).GetProperty("receiptId").GetGuid());
        await AssertCodeAsync(billing, path, command with { OperationKey = Guid.NewGuid() }, "ManualPayment.ReferenceConflict");

        var duplicate = await ConfirmAsync(billing, path, Receipt(quote, now), "NeedsReview");
        Assert.Equal("DuplicateFunds", duplicate.GetProperty("reviewReason").GetString());
        Assert.Equal(1L, await fixture.CountAsync("payment_fulfilments"));
        Assert.Equal(2L, await fixture.CountAsync("manual_payment_receipts"));
        Assert.Equal(0L, await fixture.CountAsync("payment_attempts"));
        var sealedPayload = Assert.IsType<string>(await LicensingPostgreSqlFixture.ReadScalarAsync(fixture.Hosts.LicensingOwner,
            $"SELECT protected_payload FROM public.manual_payment_receipts WHERE operation_key = '{command.OperationKey:D}'"));
        Assert.DoesNotContain(command.Reference, sealedPayload);
        Assert.DoesNotContain(command.Evidence, sealedPayload);
        Assert.DoesNotContain(command.Reason, sealedPayload);
        await AssertRetainedAsync(fixture);

        var secondOrder = await fixture.AcceptedQuoteAsync(1);
        const string otherEmail = "manual.other.billing@example.test";
        fixture.Trial.Catalogue.Clock.Advance(TimeSpan.FromMinutes(15));
        var seed = await EnrollBillingAsync(fixture, otherEmail);
        using var other = await fixture.Trial.Catalogue.SignInOperatorAsync(seed, otherEmail);
        await AssertCodeAsync(other, path, command, "ManualPayment.PayloadMismatch");
        using var hidden = await other.SendAsync(HttpMethod.Get, $"{path}/operations/{command.OperationKey:D}");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        await AssertCodeAsync(other, Path(secondOrder.Quote), Receipt(secondOrder.Quote, now) with { Reference = command.Reference.ToUpperInvariant() },
            "ManualPayment.ReferenceConflict");
    }

    [Fact]
    public async Task FailedEntitlementPublicationRollsBackConfirmedMoneyAndTheWholeTerm()
    {
        var now = P0210Fixture.Start;
        await using var fixture = await StartAsync(database, new P0201Clock { Now = now }, Settings);
        var (_, _, _, quote) = await fixture.AcceptedQuoteAsync(1);
        using var billing = await SignInBillingAsync(fixture);
        var command = Receipt(quote, now);
        var outbox = await fixture.CountAsync("entitlement_outbox");
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner,
            "ALTER TABLE public.entitlement_snapshots ADD CONSTRAINT fixture_refuse_manual CHECK (false) NOT VALID");
        try
        {
            await AssertCodeAsync(billing, Path(quote), command, "ManualPayment.Unavailable");
            Assert.Equal(0L, await fixture.CountAsync("manual_payment_receipts"));
            Assert.Equal(0L, await fixture.CountAsync("payment_fulfilments"));
            Assert.Equal(0L, await fixture.CountAsync("subscription_term_facts", "basis = 'Paid'"));
            Assert.Equal(outbox, await fixture.CountAsync("entitlement_outbox"));
        }
        finally
        {
            await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner,
                "ALTER TABLE public.entitlement_snapshots DROP CONSTRAINT fixture_refuse_manual");
        }
        await ConfirmAsync(billing, Path(quote), command, "Fulfilled");
    }

    private static async Task AssertRetainedAsync(P0302Fixture fixture)
    {
        Assert.Equal(0L, await LicensingPostgreSqlFixture.ReadScalarAsync(fixture.Hosts.Fixture.Runtime,
            "SELECT count(*) FROM public.manual_payment_receipts"));
        foreach (var sql in new[]
        {
            "UPDATE public.manual_payment_receipts SET protected_payload = 'edited'",
            "DELETE FROM public.manual_payment_receipts",
            "TRUNCATE public.manual_payment_receipts CASCADE",
        })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() =>
                LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, sql));
            Assert.Equal("55000", error.SqlState);
        }
    }

    private static async Task AssertCodeAsync(P0110CookieBrowser billing, string path, object command, string code)
    {
        using var response = await P0212Fixture.PostAsync(billing, path, command);
        Assert.Equal(code, await P0206Fixture.CodeAsync(response));
    }

    private static async Task<JsonElement> ConfirmAsync(P0110CookieBrowser billing, string path, object command, string disposition)
    {
        using var response = await P0212Fixture.PostAsync(billing, path, command);
        var body = await P0206Fixture.BodyAsync(response);
        Assert.True(response.StatusCode == HttpStatusCode.Created, body.GetRawText());
        Assert.Equal(disposition, body.GetProperty("fulfilment").GetString());
        return body;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ManualAndOnlineRacePreservesBothReceiptsAndOneTerm(bool manualWins)
    {
        var now = P0210Fixture.Start;
        await using var fixture = await StartAsync(database, new P0201Clock { Now = now }, Settings);
        var (payer, _, _, quote) = await fixture.AcceptedQuoteAsync(1);
        var attemptKey = Guid.NewGuid();
        var attempt = (await StartAsync(payer.Client, quote, HttpStatusCode.Created, attemptKey)).GetProperty("attemptId").GetGuid();
        await CaptureAsync(payer.Client, quote, attempt);
        using var billing = await SignInBillingAsync(fixture);
        var command = Receipt(quote, now);

        await using var fence = await P0302Tests.FenceAsync(fixture, "payment_fulfilments");
        Task<HttpResponseMessage> Manual() => P0212Fixture.PostAsync(billing, Path(quote), command);
        Task<HttpResponseMessage> Online() => payer.Client.PostAsJsonAsync(VerifyPath(quote, attempt), new { launcherOutcome = (string?)null });
        var first = manualWins ? Manual() : Online();
        var firstBlocked = await P0302Tests.WaitForBlockedChainAsync(fixture, fence.Pid, 1);
        var second = manualWins ? Online() : Manual();
        var bothBlocked = await P0302Tests.WaitForBlockedChainAsync(fixture, fence.Pid, 2);
        output.WriteLine($"Manual first={manualWins}, fence PID {fence.Pid}, first blocked PID {Assert.Single(firstBlocked)}, chain PIDs {string.Join(", ", bothBlocked)}.");
        await fence.ReleaseAsync();
        using var manual = await (manualWins ? first : second);
        using var online = await (manualWins ? second : first);
        Assert.Equal(HttpStatusCode.Created, manual.StatusCode);
        Assert.Equal(manualWins ? "Fulfilled" : "NeedsReview", (await P0206Fixture.BodyAsync(manual)).GetProperty("fulfilment").GetString());
        Assert.Equal(HttpStatusCode.OK, online.StatusCode);
        var status = await StatusAsync(payer.Client, quote);
        AssertStatus(status, "Verified", "Fulfilled", "Applying");
        Assert.Equal("Verified", status.GetProperty("manualPayment").GetProperty("payment").GetString());
        Assert.Equal(attempt, status.GetProperty("attemptId").GetGuid());
        await P0302Tests.AssertSingleFulfilmentAsync(fixture, quote, 1);
        Assert.Equal(1L, await fixture.CountAsync("manual_payment_receipts"));
        Assert.Equal(manualWins ? 1L : 0L, await fixture.CountAsync("payment_events", "kind = 'PaidNeedsReview'"));
        Assert.Equal(manualWins ? 1L : 0L, await fixture.CountAsync("payment_fulfilments", "manual_receipt_id IS NOT NULL AND attempt_id IS NULL"));
    }
}
