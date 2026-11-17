using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Intelibill.Licensing.Application.Payments;
using Intelibill.Shared.Contracts.V1;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0302Tests
{
    [Fact]
    public async Task AttemptIntentCommitsBeforeDispatchAndNoTransactionSpansGatewayCalls()
    {
        var probe = new GatewayProbe();
        await using var fixture = await P0302Fixture.StartAsync(database, new P0201Clock { Now = P0210Fixture.Start },
            services: services => probe.Install(services));
        probe.Database = fixture.Hosts.LicensingOwner;
        var (payer, account, _, quote) = await fixture.AcceptedQuoteAsync(1);
        probe.Account = account;

        var attempt = (await StartAsync(payer.Client, quote, HttpStatusCode.Created)).GetProperty("attemptId").GetGuid();
        await CaptureAsync(payer.Client, quote, attempt);
        AssertStatus(await VerifyAsync(payer.Client, quote, attempt), "Verified", "Fulfilled", "Applying");

        Assert.Equal(["create:committed:unlocked", "verify:committed:unlocked"], probe.Observations);
    }

    [Fact]
    public async Task WrongAmountCurrencyOrAccountFailsClosedWithoutPaymentOrTerm()
    {
        var probe = new GatewayProbe();
        await using var fixture = await P0302Fixture.StartAsync(database, new P0201Clock { Now = P0210Fixture.Start },
            services: services => probe.Install(services));
        probe.Database = fixture.Hosts.LicensingOwner;
        var (payer, account, _, quote) = await fixture.AcceptedQuoteAsync(1);
        probe.Account = account;
        var attempt = (await StartAsync(payer.Client, quote, HttpStatusCode.Created)).GetProperty("attemptId").GetGuid();
        await CaptureAsync(payer.Client, quote, attempt);

        foreach (var tamper in new Func<PaymentObservation, PaymentObservation>[]
        {
            observed => observed with { AmountPaise = observed.AmountPaise - 1 },
            observed => observed with { AmountPaise = observed.AmountPaise + 1 },
            observed => observed with { Currency = "USD" },
            observed => observed with { Pin = observed.Pin with { ProviderAccountId = "another-account" } },
            observed => observed with { Pin = observed.Pin with { Environment = "Live" } },
            observed => observed with { State = ObservedPaymentState.Authorized },
        })
        {
            probe.Tamper = tamper;
            AssertStatus(await VerifyAsync(payer.Client, quote, attempt), "PendingVerification", "NotFulfilled", "NotApplicable");
        }
        Assert.Equal(0L, await fixture.CountAsync("verified_payments"));
        Assert.Equal(0L, await fixture.CountAsync("subscription_term_facts"));

        probe.Tamper = null;
        // A later exact read cannot silently settle contradictory evidence already retained for this receipt.
        AssertStatus(await VerifyAsync(payer.Client, quote, attempt), "Verified", "NeedsReview", "NotApplicable");
        Assert.Equal(1L, await fixture.CountAsync("verified_payments"));
        Assert.Equal(0L, await fixture.CountAsync("payment_fulfilments"));
    }

    [Fact]
    public async Task ConcurrentVerificationsFulfilOnceAndEvidenceIsRetained()
    {
        await using var fixture = await P0302Fixture.StartAsync(database, new P0201Clock { Now = P0210Fixture.Start });
        var (payer, _, _, quote) = await fixture.AcceptedQuoteAsync(2);
        var attempt = (await StartAsync(payer.Client, quote, HttpStatusCode.Created)).GetProperty("attemptId").GetGuid();
        await CaptureAsync(payer.Client, quote, attempt);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            using var response = await payer.Client.PostAsJsonAsync(VerifyPath(quote, attempt), new { launcherOutcome = (string?)null });
            return (response.StatusCode, Body: await response.Content.ReadAsStringAsync());
        }));
        Assert.Contains(results, result => result.StatusCode == HttpStatusCode.OK);
        AssertStatus(await StatusAsync(payer.Client, quote), "Verified", "Fulfilled", "Applying");
        await AssertSingleFulfilmentAsync(fixture, quote, 2);

        foreach (var statement in new[]
        {
            "UPDATE public.verified_payments SET amount_paise = 1",
            "DELETE FROM public.payment_fulfilments",
            "TRUNCATE public.payment_fulfilment_shops CASCADE",
            "DELETE FROM public.payment_attempts",
        })
        {
            var refused = await Assert.ThrowsAsync<PostgresException>(() =>
                LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, statement));
            Assert.Equal("55000", refused.SqlState);
        }
        await AssertSingleFulfilmentAsync(fixture, quote, 2);
    }

    [Fact]
    public async Task PaidShopsCannotBePurchasedOrJoinedAgain()
    {
        await using var fixture = await P0302Fixture.StartAsync(database, new P0201Clock { Now = P0210Fixture.Start });
        var (payer, account, shops, quote) = await fixture.AcceptedQuoteAsync(1);
        var attempt = (await StartAsync(payer.Client, quote, HttpStatusCode.Created)).GetProperty("attemptId").GetGuid();
        await CaptureAsync(payer.Client, quote, attempt);
        var term = (await VerifyAsync(payer.Client, quote, attempt)).GetProperty("termId").GetGuid();

        var candidates = await P0209Fixture.ReadAsync(await payer.Client.GetAsync(
            new Uri($"{P0301Tests.OrdersPath(account)}/eligible-shops", UriKind.Relative)));
        var listed = Assert.Single(candidates.GetProperty("items").EnumerateArray());
        Assert.Equal(term, listed.GetProperty("termId").GetGuid());

        using var again = await payer.Client.PostAsJsonAsync(P0301Tests.OrdersPath(account),
            new CreateCheckoutQuoteV1(Guid.NewGuid(), quote.PlanVersionId, "Month", [shops[0].ShopId], 0, 1));
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Equal(CheckoutOrdersV1.Invalid, await P0206Fixture.CodeAsync(again));
        // P03-08: the paid term itself is renewed as a whole package from its paid-through end, never re-bought.
        var renewal = await P0301Tests.CreateQuoteAsync(payer.Client, account,
            new CreateCheckoutQuoteV1(Guid.NewGuid(), quote.PlanVersionId, "Month", [], 0, 1, term));
        Assert.Equal("PriorPaidThrough", renewal.StartRule);
        Assert.Equal(term, renewal.TermId);

        var owner = await P0209Fixture.SignInAsync(fixture.Hosts, Intelibill.Domain.Enums.ShopRole.Owner);
        using var join = await payer.Client.PostAsJsonAsync($"/api/billing/accounts/{account:D}/affiliations",
            new { operationKey = Guid.NewGuid(), shopId = owner.ShopId, withoutCoverage = false, termId = term });
        Assert.False(join.IsSuccessStatusCode);
        Assert.Equal(1L, await fixture.CountAsync("payment_fulfilment_shops"));
    }

    /// <summary>Wraps the registered resolver; observes the database from an independent connection during calls.</summary>
    internal sealed class GatewayProbe
    {
        internal NpgsqlConnectionStringBuilder? Database { get; set; }
        internal Guid Account { get; set; }
        internal Func<PaymentObservation, PaymentObservation>? Tamper { get; set; }
        internal Func<CancellationToken, Task>? BeforeVerify;
        internal bool LoseCreateResponse { get; set; }
        internal bool LookupNotFound { get; set; }
        internal bool DisableNewAttempts { get; set; }
        internal int LookupCalls;
        internal string? OriginalReference { get; private set; }
        internal ConcurrentQueue<string> Observations { get; } = new();

        internal void Install(IServiceCollection services)
        {
            var original = services.Last(service => service.ServiceType == typeof(IPaymentGatewayResolver));
            services.Remove(original);
            services.AddScoped<IPaymentGatewayResolver>(provider => new Resolver(
                (IPaymentGatewayResolver)ActivatorUtilities.CreateInstance(provider, original.ImplementationType!), this));
        }

        internal async Task ObserveAsync(string call, Guid attempt)
        {
            await using var connection = new NpgsqlConnection(Database!.ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                SELECT EXISTS (SELECT FROM public.payment_attempts WHERE attempt_id = $1),
                    pg_try_advisory_lock(hashtextextended('b' || $2::text, 0))
                """, connection);
            command.Parameters.AddWithValue(attempt);
            command.Parameters.AddWithValue(Account.ToString("D"));
            await using var reader = await command.ExecuteReaderAsync();
            await reader.ReadAsync();
            Observations.Enqueue($"{call}:{(reader.GetBoolean(0) ? "committed" : "missing")}:{(reader.GetBoolean(1) ? "unlocked" : "locked")}");
        }

        private sealed class Resolver(IPaymentGatewayResolver inner, GatewayProbe probe) : IPaymentGatewayResolver
        {
            public IPaymentGateway? ForNewAttempts() =>
                !probe.DisableNewAttempts && inner.ForNewAttempts() is { } gateway ? new Gateway(gateway, probe) : null;
            public IReadOnlyList<IPaymentGateway> ForWebhooks(string providerId) => inner.ForWebhooks(providerId);
            public IPaymentGateway? ForPin(PaymentProviderPin pin) => inner.ForPin(pin) is { } gateway ? new Gateway(gateway, probe) : null;
        }

        private sealed class Gateway(IPaymentGateway inner, GatewayProbe probe) : IPaymentGateway
        {
            private Guid _attempt;
            public PaymentProviderPin Pin => inner.Pin;
            public PaymentGatewayCapabilities Capabilities => inner.Capabilities;

            public async Task<GatewayResult<CheckoutSession>> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct)
            {
                _attempt = request.AttemptId;
                await probe.ObserveAsync("create", request.AttemptId);
                var result = await inner.CreateCheckoutAsync(request, ct);
                probe.OriginalReference = result.Value?.ProviderOrderReference;
                return probe.LoseCreateResponse ? GatewayResult.Unavailable<CheckoutSession>() : result;
            }

            public async Task<GatewayResult<PaymentObservation>> VerifyCheckoutAsync(CheckoutVerification request, CancellationToken ct)
            {
                if (probe.Tamper is null)
                    await probe.ObserveAsync("verify", await AttemptAsync(request.ProviderOrderReference));
                var beforeVerify = Interlocked.Exchange(ref probe.BeforeVerify, null);
                if (beforeVerify is not null)
                    await beforeVerify(ct);
                var result = await inner.VerifyCheckoutAsync(request, ct);
                return probe.Tamper is { } tamper && result.Value is { } value ? result with { Value = tamper(value) } : result;
            }

            private async Task<Guid> AttemptAsync(string reference)
            {
                if (_attempt != Guid.Empty) return _attempt;
                var value = await LicensingPostgreSqlFixture.ReadScalarAsync(probe.Database!,
                    $"SELECT attempt_id FROM public.payment_attempt_sessions WHERE provider_order_reference = '{reference}'");
                return (Guid)value!;
            }

            public Task<GatewayResult<CheckoutLookup>> LookupCheckoutAsync(CheckoutRequest request, CancellationToken ct)
            {
                _attempt = request.AttemptId;
                Interlocked.Increment(ref probe.LookupCalls);
                return probe.LookupNotFound
                    ? Task.FromResult(new GatewayResult<CheckoutLookup>(GatewayOutcome.NotFound, null))
                    : inner.LookupCheckoutAsync(request, ct);
            }

            public Task<GatewayResult<PaymentObservation>> GetPaymentAsync(string reference, CancellationToken ct) =>
                inner.GetPaymentAsync(reference, ct);
            public Task<GatewayResult<IReadOnlyList<PaymentObservation>>> GetOrderPaymentsAsync(string reference, CancellationToken ct) =>
                inner.GetOrderPaymentsAsync(reference, ct);
            public Task<GatewayResult<RefundObservation>> RequestRefundAsync(RefundRequest request, CancellationToken ct) =>
                inner.RequestRefundAsync(request, ct);
            public Task<GatewayResult<RefundObservation>> GetRefundAsync(string reference, CancellationToken ct) =>
                inner.GetRefundAsync(reference, ct);
            public GatewayResult<VerifiedGatewayEvent> ValidateWebhook(ReadOnlySpan<byte> body, IReadOnlyDictionary<string, string> headers) =>
                inner.ValidateWebhook(body, headers);
        }
    }
}
