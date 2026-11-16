using System.Security.Cryptography;
using Intelibill.Licensing.Application.Payments;
using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Infrastructure.Payments.Development;

/// <summary>
/// Local simulator adapter. Its provider-side state lives in its own tables (readable only with the transaction-local
/// <c>app.development_gateway</c> flag), so restarts and replicas never orphan a session. It opens its own short
/// transaction and therefore fails fast if a caller still holds one. Registered only in a local simulator runtime.
/// </summary>
internal sealed class DevelopmentPaymentGateway(LicensingDbContext db, TimeProvider clock) : IPaymentGateway, IPaymentSimulator
{
    internal static readonly PaymentProviderPin LocalPin =
        new("development-simulator", "local-simulator", "Development", "development-simulator/1");

    public PaymentProviderPin Pin => LocalPin;
    public PaymentGatewayCapabilities Capabilities { get; } = new(FullPaymentOnly: true, Refunds: false, Webhooks: false);

    public async Task<GatewayResult<CheckoutSession>> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct)
    {
        var reference = "dev_order_" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12));
        await InGatewayAsync(token => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.development_gateway_sessions (provider_order_reference, account_id, attempt_id, order_id, amount_paise,
                currency, created_at)
            VALUES ({reference}, {request.AccountId}, {request.AttemptId}, {request.OrderId}, {request.AmountPaise}, {request.Currency}, {Now()})
            """, token), ct);
        return GatewayResult.Success<CheckoutSession>(new(reference, CheckoutPaymentsV1.SimulatorFlow, new Dictionary<string, string>()));
    }

    public async Task<GatewayResult<CheckoutLookup>> LookupCheckoutAsync(CheckoutRequest request, CancellationToken ct)
    {
        var rows = await InGatewayAsync(token => db.Database.SqlQuery<SessionRow>($"""
            SELECT provider_order_reference, order_id, amount_paise, currency
            FROM public.development_gateway_sessions
            WHERE account_id = {request.AccountId} AND attempt_id = {request.AttemptId}
            """).ToListAsync(token), ct);
        if (rows is not [{ OrderId: { } order } row])
            return new(GatewayOutcome.NotFound, null);
        var session = new CheckoutSession(row.ProviderOrderReference, CheckoutPaymentsV1.SimulatorFlow,
            new Dictionary<string, string>());
        return GatewayResult.Success(new CheckoutLookup(Pin,
            request with { OrderId = order, AmountPaise = row.AmountPaise, Currency = row.Currency }, session));
    }

    public Task<GatewayResult<PaymentObservation>> VerifyCheckoutAsync(CheckoutVerification request, CancellationToken ct) =>
        InGatewayAsync(async token =>
        {
            var reference = request.ProviderOrderReference;
            if (await LockSessionAsync(reference, token) is not { } session)
                return new GatewayResult<PaymentObservation>(GatewayOutcome.NotFound, null);
            var control = await ReadControlAsync(reference, token);
            await MaterializeCaptureAsync(session, control, token);
            var captured = await Payments().SingleOrDefaultAsync(p => p.ProviderOrderReference == reference, token);
            if (captured is not null)
                return GatewayResult.Success(captured.ToObservation());
            if (control?.Outcome == "Timeout")
                return GatewayResult.Unavailable<PaymentObservation>();
            var failed = control?.Outcome == "Failed";
            return GatewayResult.Success(new PaymentObservation(Pin, reference, null,
                failed ? ObservedPaymentState.Failed : ObservedPaymentState.Pending, session.AmountPaise, session.Currency, null,
                CheckoutDefinitivelyFailed: failed));
        }, ct);

    public async Task<GatewayResult<PaymentObservation>> GetPaymentAsync(string providerPaymentReference, CancellationToken ct)
    {
        var rows = await InGatewayAsync(token => Payments().Where(p => p.ProviderPaymentReference == providerPaymentReference)
            .ToListAsync(token), ct);
        return rows is [var row] ? GatewayResult.Success<PaymentObservation>(row.ToObservation())
            : GatewayResult.Rejected<PaymentObservation>();
    }

    public async Task<GatewayResult<IReadOnlyList<PaymentObservation>>> GetOrderPaymentsAsync(string providerOrderReference,
        CancellationToken ct)
    {
        var rows = await InGatewayAsync(token => Payments().Where(p => p.ProviderOrderReference == providerOrderReference)
            .ToListAsync(token), ct);
        return GatewayResult.Success<IReadOnlyList<PaymentObservation>>([.. rows.Select(row => row.ToObservation())]);
    }

    public Task<GatewayResult<RefundObservation>> RequestRefundAsync(RefundRequest request, CancellationToken ct) =>
        Task.FromResult(GatewayResult.Unsupported<RefundObservation>());

    public Task<GatewayResult<RefundObservation>> GetRefundAsync(string providerRefundReference, CancellationToken ct) =>
        Task.FromResult(GatewayResult.Unsupported<RefundObservation>());

    public GatewayResult<VerifiedGatewayEvent> ValidateWebhook(ReadOnlySpan<byte> body, IReadOnlyDictionary<string, string> headers) =>
        GatewayResult.Unsupported<VerifiedGatewayEvent>();

    public Task SimulateAsync(string providerOrderReference, string outcome, CancellationToken ct) =>
        InGatewayAsync(async token =>
        {
            if (await LockSessionAsync(providerOrderReference, token) is not { } session)
                return false;
            var control = await ReadControlAsync(providerOrderReference, token);
            await MaterializeCaptureAsync(session, control, token);
            if (control?.Outcome == "Failed" || await Payments().AnyAsync(p => p.ProviderOrderReference == providerOrderReference, token))
                return false;
            if (outcome == "Captured")
                await CaptureAsync(session, Now(), token);
            else if (outcome != control?.Outcome)
                await SetControlAsync(providerOrderReference, outcome, token);
            return true;
        }, ct);

    private async Task<SessionRow?> LockSessionAsync(string reference, CancellationToken ct) =>
        (await db.Database.SqlQuery<SessionRow>($"""
            SELECT provider_order_reference, order_id, amount_paise, currency
            FROM public.development_gateway_sessions WHERE provider_order_reference = {reference}
            FOR UPDATE
            """).ToListAsync(ct)).SingleOrDefault();

    private Task<ControlRow?> ReadControlAsync(string reference, CancellationToken ct) =>
        db.Database.SqlQuery<ControlRow>($"""
            SELECT outcome, capture_at FROM public.development_gateway_controls WHERE provider_order_reference = {reference}
            """).SingleOrDefaultAsync(ct);

    private Task<int> SetControlAsync(string reference, string outcome, CancellationToken ct)
    {
        DateTimeOffset? captureAt = outcome == "Delayed" ? Now().AddSeconds(30) : null;
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.development_gateway_controls (provider_order_reference, outcome, capture_at)
            VALUES ({reference}, {outcome}, {captureAt})
            ON CONFLICT (provider_order_reference) DO UPDATE SET outcome = excluded.outcome, capture_at = excluded.capture_at
            """, ct);
    }

    private Task MaterializeCaptureAsync(SessionRow session, ControlRow? control, CancellationToken ct) =>
        control?.CaptureAt is { } due && Now() >= due ? CaptureAsync(session, due.ToUniversalTime(), ct) : Task.CompletedTask;

    /// <summary>Records one full capture at the session amount. A repeat keeps the first capture.</summary>
    private Task<int> CaptureAsync(SessionRow session, DateTimeOffset capturedAt, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.development_gateway_payments (provider_payment_reference, provider_order_reference,
                amount_paise, currency, state, captured_at)
            VALUES ({"dev_pay_" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12))}, {session.ProviderOrderReference},
                {session.AmountPaise}, {session.Currency}, 'Captured', {capturedAt})
            ON CONFLICT (provider_order_reference) DO NOTHING
            """, ct);

    private IQueryable<PaymentRow> Payments() => db.Database.SqlQuery<PaymentRow>($"""
        SELECT provider_payment_reference, provider_order_reference, amount_paise, currency, state, captured_at
        FROM public.development_gateway_payments
        """);

    private async Task<T> InGatewayAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.development_gateway', 'on', true)", ct);
        var result = await work(ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    private DateTimeOffset Now()
    {
        var now = clock.GetUtcNow();
        return new(now.UtcTicks - now.UtcTicks % 10, TimeSpan.Zero);
    }

    private sealed record SessionRow(string ProviderOrderReference, Guid? OrderId, long AmountPaise, string Currency);
    private sealed record ControlRow(string Outcome, DateTimeOffset? CaptureAt);

    private sealed record PaymentRow(string ProviderPaymentReference, string ProviderOrderReference, long AmountPaise,
        string Currency, string State, DateTimeOffset CapturedAt)
    {
        internal PaymentObservation ToObservation() => new(LocalPin, ProviderOrderReference, ProviderPaymentReference,
            State == "Captured" ? ObservedPaymentState.Captured : ObservedPaymentState.Failed, AmountPaise, Currency,
            CapturedAt.ToUniversalTime());
    }
}
