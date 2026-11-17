using System.Text.Json;
using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.Payments;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Licensing.Infrastructure.AdminIdentities;
using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Intelibill.Licensing.Infrastructure.Payments;

internal sealed partial class PaymentProviderRoutingStore(LicensingDbContext db, IPaymentGatewayResolver gateways,
    TimeProvider clock, PaymentProviderConfiguration registry, PaymentProviderBindings bindings) : IPaymentProviderRoutingStore
{
    public async Task<ErrorOr<T>> InAuthorityAsync<T>(AdminSessionCaller caller, bool selection,
        Func<DateTimeOffset, CancellationToken, Task<ErrorOr<T>>> operation, CancellationToken ct)
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '3s'", ct);
            var actor = (await db.Set<AdminIdentity>().FromSql($"""
                SELECT *, xmin FROM public.admin_identities WHERE id = {caller.AdminId} FOR SHARE
                """).AsNoTracking().ToListAsync(ct)).SingleOrDefault();
            var session = await AdminAuthorityLocks.LockSessionAsync(db, caller, ct);
            if (Authority(caller, actor, session, selection) is { } denied) return denied;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                SELECT set_config('app.provider_admin_id', {caller.AdminId.ToString("D")}, true),
                    set_config('app.provider_session_id', {caller.SessionId.ToString("D")}, true)
                """, ct);
            var result = await operation(session!.MfaVerifiedAt, ct);
            if (result.IsError) return result;
            if (Authority(caller, actor, session, selection) is { } expired) return expired;
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (Exception error) when (error is NpgsqlException or TimeoutException or DbUpdateException)
        {
            ct.ThrowIfCancellationRequested();
            return CheckoutPaymentErrors.Busy;
        }
    }

    private Error? Authority(AdminSessionCaller caller, AdminIdentity? actor, AdminSession? session, bool selection)
    {
        var now = clock.GetUtcNow();
        if (session is null || now >= caller.AccessExpiresAt || !session.IsLiveAt(now) ||
            actor is not { DeactivatedAt: null, MfaEnrolledAt: not null }) return AdminRefreshErrors.SessionEnded;
        var grants = actor.Capabilities;
        if (selection ? !actor.IsPlatformOwner : (grants & (AdminCapability.PlatformOwner | AdminCapability.Billing | AdminCapability.Support)) == 0)
            return PaymentProviderErrors.Denied;
        return selection && !new ProviderActor(caller.AdminId, session.MfaVerifiedAt).HasRecentMfa(now)
            ? PaymentProviderErrors.RecentMfa : null;
    }

    public Task ScopeOrderAsync(Guid account, Guid order, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"""
        SELECT set_config('app.billing_account_id', {account.ToString("D")}, true),
            set_config('app.manual_order_id', {order.ToString("D")}, true),
            set_config('app.manual_admin_id', current_setting('app.provider_admin_id', true), true),
            set_config('app.manual_session_id', current_setting('app.provider_session_id', true), true)
        """, ct);

    public async Task<PaymentProviderSelection> SelectedAsync(bool intention, CancellationToken ct)
    {
        if (!intention)
        {
            var read = await ReadAsync(false, ct);
            return read.Revision == 0 ? Bootstrap() : read;
        }
        var initialized = (await db.Database.SqlQuery<RoutingRow>($"""
            SELECT revision, profile_id, pin::text AS pin FROM public.payment_provider_routing
            WHERE id = 1 AND revision > 0 FOR SHARE
            """).ToListAsync(ct)).SingleOrDefault();
        if (initialized is not null) return initialized.Selection();
        var locked = await LockSelectionAsync(ct);
        if (locked.Revision != 0) return locked;
        var bootstrap = Bootstrap();
        if (bootstrap.Pin is null) return locked;
        await SaveBootstrapAsync(bootstrap, ct);
        return bootstrap with { Revision = 1 };
    }

    private PaymentProviderSelection Bootstrap() => gateways.ForNewAttempts() is { } gateway
        ? new(0, gateway.Pin.ProviderId == "razorpay" ? "legacy-razorpay" : "legacy-simulator", gateway.Pin) : new(0, null, null);

    public Task<PaymentProviderSelection> LockSelectionAsync(CancellationToken ct) => ReadAsync(true, ct);

    private async Task<PaymentProviderSelection> ReadAsync(bool locking, CancellationToken ct)
    {
        var query = locking ? db.Database.SqlQuery<RoutingRow>($"""
            SELECT revision, profile_id, pin::text AS pin FROM public.payment_provider_routing WHERE id = 1 FOR UPDATE
            """) : db.Database.SqlQuery<RoutingRow>($"""
            SELECT revision, profile_id, pin::text AS pin FROM public.payment_provider_routing WHERE id = 1
            """);
        var rows = await query.ToListAsync(ct);
        // An absent persisted selector is a configuration failure, never permission to resurrect legacy selection.
        return rows.SingleOrDefault()?.Selection() ?? new(-1, null, null);
    }

    private async Task SaveBootstrapAsync(PaymentProviderSelection bootstrap, CancellationToken ct)
    {
        foreach (var profile in registry.Profiles.Where(p => gateways.ForPin(p.Pin) is not null))
            await bindings.RetainAsync(profile, ct, bootstrap: true);
        var pin = JsonSerializer.Serialize(bootstrap.Pin);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.payment_provider_selections (revision, profile_id, pin, provenance, reason, selected_at)
            VALUES (1, {bootstrap.ProfileId}, {pin}::jsonb, 'DeploymentConfiguration', 'Legacy configuration bootstrap', {clock.GetUtcNow()});
            UPDATE public.payment_provider_routing SET revision = 1, profile_id = {bootstrap.ProfileId}, pin = {pin}::jsonb WHERE id = 1 AND revision = 0
            """, ct);
    }

    public async Task<StoredProviderSelection?> OperationAsync(Guid key, CancellationToken ct)
    {
        var row = await db.Database.SqlQuery<OperationRow>($"""
            SELECT request_hash, receipt::text AS receipt FROM public.payment_provider_selections WHERE operation_key = {key}
            """).SingleOrDefaultAsync(ct);
        return row is null ? null : new(row.RequestHash, JsonSerializer.Deserialize<PaymentProviderSelectionReceipt>(row.Receipt)!);
    }

    public async Task<bool> SaveAsync(PaymentProviderSelection previous, string hash, string reason,
        PaymentProviderSelectionReceipt receipt, CancellationToken ct)
    {
        var profile = registry.Profiles.SingleOrDefault(p => p.Id == receipt.ProfileId && p.Pin == receipt.Pin);
        if (profile is not null && !await bindings.RetainAsync(profile, ct)) return false;
        foreach (var retained in registry.Profiles.Where(p => p.Id != receipt.ProfileId && gateways.ForPin(p.Pin) is not null))
            await bindings.RetainAsync(retained, ct);
        var pin = JsonSerializer.Serialize(receipt.Pin);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.payment_provider_selections (revision, profile_id, pin, previous_pin, provenance, admin_id,
                session_id, mfa_verified_at, operation_key, request_hash, reason, selected_at, receipt)
            VALUES ({receipt.Revision}, {receipt.ProfileId}, {pin}::jsonb, {JsonSerializer.Serialize(previous.Pin)}::jsonb,
                'PlatformOwner', {receipt.AdminId}, nullif(current_setting('app.provider_session_id', true), '')::uuid,
                {receipt.MfaVerifiedAt}, {receipt.OperationKey}, {hash}, {reason}, {receipt.SelectedAt}, {JsonSerializer.Serialize(receipt)}::jsonb);
            UPDATE public.payment_provider_routing SET revision = {receipt.Revision}, profile_id = {receipt.ProfileId},
                pin = {pin}::jsonb WHERE id = 1 AND revision = {previous.Revision}
            """, ct);
        return true;
    }

    private sealed record RoutingRow(long Revision, string? ProfileId, string? Pin)
    {
        internal PaymentProviderSelection Selection() => new(Revision, ProfileId,
            Pin is null ? null : JsonSerializer.Deserialize<PaymentProviderPin>(Pin));
    }
    private sealed record OperationRow(string RequestHash, string Receipt);
}
