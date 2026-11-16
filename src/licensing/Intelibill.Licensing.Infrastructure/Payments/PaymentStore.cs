using ErrorOr;
using Intelibill.Licensing.Application.Entitlements;
using Intelibill.Licensing.Application.Payments;
using Intelibill.Licensing.Infrastructure.Billing;
using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Intelibill.Licensing.Infrastructure.Payments;

/// <summary>
/// SQL-owned payment evidence under forced row-level security. Settings are transaction-local; the attempt scope
/// (<c>app.payment_attempt_id</c>) is the only write authority for sessions, events, verified payments and
/// fulfilments. An attempt-scoped transaction refuses to start inside another, so no gateway call can sit in one.
/// </summary>
internal sealed partial class PaymentStore(LicensingDbContext db, IEntitlementPublisher publisher, ILogger<PaymentStore> logger)
    : IPaymentStore
{
    public async Task<ErrorOr<T>> InAttemptScopeAsync<T>(Guid account, Guid attempt, bool lockAccount,
        Func<CancellationToken, Task<ErrorOr<T>>> operation, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("An attempt-scoped payment transaction cannot nest.");
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                SELECT set_config('app.billing_account_id', {account.ToString("D")}, true),
                       set_config('app.payment_attempt_id', {attempt.ToString("D")}, true)
                """, ct);
            if (lockAccount)
                await BillingAccountLock.TakeAsync(db, account, ct);
            var result = await operation(ct);
            if (!result.IsError)
                await transaction.CommitAsync(ct);
            return result;
        }
        catch (Exception error) when (SqlState(error) is PostgresErrorCodes.LockNotAvailable or PostgresErrorCodes.DeadlockDetected
            or PostgresErrorCodes.UniqueViolation)
        {
            return CheckoutPaymentErrors.Busy;
        }
        catch (Exception error) when (error is NpgsqlException or TimeoutException or DbUpdateException { InnerException: NpgsqlException })
        {
            ct.ThrowIfCancellationRequested();
            LogUnavailable(logger, SqlState(error) ?? error.GetType().Name);
            return CheckoutPaymentErrors.Unavailable;
        }
    }

    public Task UseAttemptAsync(Guid attempt, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync(
        $"SELECT set_config('app.payment_attempt_id', {attempt.ToString("D")}, true)", ct);

    public async Task<StoredPaymentAttempt?> FindAttemptAsync(Guid account, Guid id, bool byOperation, CancellationToken ct) =>
        (await Attempts(account).Where(row => (byOperation && row.OperationKey == id) || (!byOperation && row.AttemptId == id))
            .SingleOrDefaultAsync(ct))?.ToStored();

    public async Task<StoredPaymentAttempt?> FindOrderAttemptAsync(Guid account, Guid order, CancellationToken ct) =>
        (await Attempts(account).Where(row => row.OrderId == order).SingleOrDefaultAsync(ct))?.ToStored();

    private IQueryable<AttemptRow> Attempts(Guid account) => db.Database.SqlQuery<AttemptRow>($"""
        SELECT a.attempt_id, a.account_id, a.order_id, a.operation_key, a.actor_id, a.request_hash, a.provider_id,
            a.provider_account_id, a.environment, a.configuration_version, a.amount_paise, a.currency, a.created_at,
            s.provider_order_reference, s.launcher_flow
        FROM public.payment_attempts a LEFT JOIN public.payment_attempt_sessions s USING (account_id, attempt_id)
        WHERE a.account_id = {account}
        """);

    public Task<bool> HasOverlappingPaymentAsync(Guid account, Guid order, CancellationToken ct) =>
        db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT FROM public.checkout_order_shops requested
                JOIN public.checkout_order_shops covered USING (account_id, shop_id)
                JOIN public.payment_attempts a ON a.account_id = covered.account_id AND a.order_id = covered.order_id
                WHERE requested.account_id = {account} AND requested.order_id = {order} AND a.order_id <> {order}
                  AND NOT EXISTS (SELECT FROM public.payment_fulfilments f WHERE f.attempt_id = a.attempt_id)
                  AND (NOT EXISTS (SELECT FROM public.payment_events e WHERE e.attempt_id = a.attempt_id AND e.kind = 'CheckoutFailed')
                       OR EXISTS (SELECT FROM public.verified_payments v WHERE v.attempt_id = a.attempt_id))) AS "Value"
            """).SingleAsync(ct);

    public Task<bool> AnyTrialAsync(Guid account, Guid[] shops, CancellationToken ct) =>
        db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT FROM public.trial_history WHERE account_id = {account} AND shop_id = ANY({shops})) AS "Value"
            """).SingleAsync(ct);

    public Task InsertAttemptAsync(NewPaymentAttempt a, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO public.payment_attempts (attempt_id, account_id, order_id, operation_key, actor_id, request_hash,
            replay_id, provider_id, provider_account_id, environment, configuration_version, amount_paise, currency,
            snapshot_hash, created_at)
        SELECT {a.AttemptId}, {a.AccountId}, {a.OrderId}, {a.OperationKey},
            nullif(current_setting('app.billing_actor_id', true), '')::uuid, {a.RequestHash}, {a.ReplayId},
            {a.Pin.ProviderId}, {a.Pin.ProviderAccountId}, {a.Pin.Environment}, {a.Pin.ConfigurationVersion},
            {a.AmountPaise}, {a.Currency}, {a.SnapshotHash}, {a.CreatedAt}
        """, ct);

    public async Task<bool> RecordSessionAsync(StoredPaymentAttempt attempt, CheckoutSession session, DateTimeOffset at, CancellationToken ct) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.payment_attempt_sessions (attempt_id, account_id, provider_order_reference, launcher_flow, recorded_at)
            VALUES ({attempt.AttemptId}, {attempt.AccountId}, {session.ProviderOrderReference}, {session.LauncherFlow}, {at})
            ON CONFLICT (attempt_id) DO NOTHING
            """, ct) == 1;

    public Task AppendEventAsync(StoredPaymentAttempt attempt, string kind, Guid? actor, DateTimeOffset at, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.payment_events (event_id, account_id, attempt_id, order_id, kind, actor_id, recorded_at)
            VALUES ({Guid.NewGuid()}, {attempt.AccountId}, {attempt.AttemptId}, {attempt.OrderId}, {kind}, {actor}, {at})
            """, ct);

    public Task<bool> HasDefinitiveFailureAsync(Guid attempt, CancellationToken ct) =>
        db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT FROM public.payment_events WHERE attempt_id = {attempt} AND kind = 'CheckoutFailed') AS "Value"
            """).SingleAsync(ct);

    public Task<bool> HasVerifiedPaymentAsync(Guid attempt, CancellationToken ct) =>
        db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT FROM public.verified_payments WHERE attempt_id = {attempt}) AS "Value"
            """).SingleAsync(ct);

    public async Task<PaymentStatusRow> ReadStatusAsync(Guid account, Guid order, CancellationToken ct)
    {
        var attempt = await FindOrderAttemptAsync(account, order, ct);
        if (attempt is null)
            return new(null, false, null, false, false, null, null, null, []);
        var row = await db.Database.SqlQuery<StatusRow>($"""
            SELECT EXISTS (SELECT FROM public.payment_events e WHERE e.attempt_id = a.attempt_id
                    AND e.kind = 'VerificationRequested') AS verification_requested,
                EXISTS (SELECT FROM public.payment_events e WHERE e.attempt_id = a.attempt_id
                    AND e.kind = 'CheckoutFailed') AS definitively_failed,
                v.verified_at, EXISTS (SELECT FROM public.payment_events e WHERE e.attempt_id = a.attempt_id
                    AND e.kind = 'PaidNeedsReview') AS needs_review,
                f.term_id, t.starts_at AS term_starts_at, t.ends_at AS term_ends_at
            FROM public.payment_attempts a
            LEFT JOIN public.verified_payments v ON v.attempt_id = a.attempt_id
            LEFT JOIN public.payment_fulfilments f ON f.account_id = a.account_id AND f.order_id = a.order_id
            LEFT JOIN public.subscription_coverage_terms t ON t.account_id = f.account_id AND t.term_id = f.term_id
            WHERE a.account_id = {account} AND a.attempt_id = {attempt.AttemptId}
            """).SingleAsync(ct);
        var deliveries = await db.Database.SqlQuery<PaymentShopDelivery>($"""
            SELECT shop_id, shop_revision FROM public.payment_fulfilment_deliveries
            WHERE account_id = {account} AND order_id = {order} ORDER BY shop_id
            """).ToListAsync(ct);
        return new(attempt, row.VerificationRequested, row.VerifiedAt?.ToUniversalTime(), row.DefinitivelyFailed, row.NeedsReview, row.TermId,
            row.TermStartsAt?.ToUniversalTime(), row.TermEndsAt?.ToUniversalTime(), deliveries);
    }

    private static string? SqlState(Exception error) =>
        (error as PostgresException ?? error.InnerException as PostgresException)?.SqlState;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Payment storage unavailable ({Cause}).")]
    private static partial void LogUnavailable(ILogger logger, string cause);

    private sealed record StatusRow(bool VerificationRequested, DateTimeOffset? VerifiedAt, bool DefinitivelyFailed, bool NeedsReview, Guid? TermId,
        DateTimeOffset? TermStartsAt, DateTimeOffset? TermEndsAt);

    private sealed record AttemptRow(Guid AttemptId, Guid AccountId, Guid OrderId, Guid OperationKey, Guid ActorId,
        string RequestHash, string ProviderId, string ProviderAccountId, string Environment, string ConfigurationVersion,
        long AmountPaise, string Currency, DateTimeOffset CreatedAt, string? ProviderOrderReference, string? LauncherFlow)
    {
        internal StoredPaymentAttempt ToStored() => new(AttemptId, AccountId, OrderId, ActorId, RequestHash,
            new(ProviderId, ProviderAccountId, Environment, ConfigurationVersion), AmountPaise, Currency,
            CreatedAt.ToUniversalTime(), ProviderOrderReference, LauncherFlow);
    }
}
