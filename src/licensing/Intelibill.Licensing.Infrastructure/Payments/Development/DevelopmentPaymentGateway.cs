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
            INSERT INTO public.development_gateway_sessions (provider_order_reference, account_id, attempt_id, amount_paise,
                currency, created_at)
            VALUES ({reference}, {request.AccountId}, {request.AttemptId}, {request.AmountPaise}, {request.Currency}, {Now()})
            """, token), ct);
        return GatewayResult.Success<CheckoutSession>(new(reference, CheckoutPaymentsV1.SimulatorFlow, new Dictionary<string, string>()));
    }

    public async Task<GatewayResult<PaymentObservation>> VerifyCheckoutAsync(CheckoutVerification request, CancellationToken ct)
    {
        var payments = await GetOrderPaymentsAsync(request.ProviderOrderReference, ct);
        if (payments.Value is not { } observed)
            return GatewayResult.Unavailable<PaymentObservation>();
        return GatewayResult.Success<PaymentObservation>(
            observed.FirstOrDefault(p => p.State == ObservedPaymentState.Captured) ??
            new(Pin, request.ProviderOrderReference, null, ObservedPaymentState.Pending, 0, request.ExpectedCurrency, null));
    }

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

    /// <summary>Records one full capture at the session amount; a repeat keeps the first capture.</summary>
    public async Task<bool> CaptureAsync(string providerOrderReference, CancellationToken ct) =>
        await InGatewayAsync(token => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.development_gateway_payments (provider_payment_reference, provider_order_reference,
                amount_paise, currency, state, captured_at)
            SELECT {"dev_pay_" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12))}, provider_order_reference,
                amount_paise, currency, 'Captured', {Now()}
            FROM public.development_gateway_sessions WHERE provider_order_reference = {providerOrderReference}
            ON CONFLICT (provider_order_reference) DO NOTHING
            """, token), ct) == 1;

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

    private sealed record PaymentRow(string ProviderPaymentReference, string ProviderOrderReference, long AmountPaise,
        string Currency, string State, DateTimeOffset CapturedAt)
    {
        internal PaymentObservation ToObservation() => new(LocalPin, ProviderOrderReference, ProviderPaymentReference,
            State == "Captured" ? ObservedPaymentState.Captured : ObservedPaymentState.Failed, AmountPaise, Currency,
            CapturedAt.ToUniversalTime());
    }
}
