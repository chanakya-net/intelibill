using System.Text.Json;
using Intelibill.Licensing.Application.Payments;
using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Payments;

/// <summary>Immutable, safe reference bindings; credential values never enter this table or its cache.</summary>
internal sealed class PaymentProviderBindings(LicensingDbContext db, TimeProvider clock)
{
    private readonly Dictionary<PaymentProviderPin, BindingRow?> loaded = [];

    // The existing resolver port is synchronous. Cache one bounded metadata read per profile per scope;
    // writes remain asynchronous inside the authorized selection transaction, never on a GET.
    internal bool Matches(PaymentProviderProfile profile) => Find(profile.Pin) is not { } prior || Same(prior, profile);
    internal bool IsRegistered(PaymentProviderPin pin) => Find(pin) is not null;

    private BindingRow? Find(PaymentProviderPin identity)
    {
        if (loaded.TryGetValue(identity, out var prior)) return prior;
        var pin = JsonSerializer.Serialize(identity);
        prior = db.Database.SqlQuery<BindingRow>($"""
            SELECT profile_id, credential_reference, webhook_reference FROM public.payment_provider_bindings WHERE pin = {pin}::jsonb
            """).SingleOrDefault();
        loaded[identity] = prior;
        return prior;
    }

    internal async Task<bool> RetainAsync(PaymentProviderProfile profile, CancellationToken ct, bool bootstrap = false)
    {
        var pin = JsonSerializer.Serialize(profile.Pin);
        var provenance = bootstrap ? "DeploymentConfiguration" : "PlatformOwner";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.payment_provider_bindings (pin, profile_id, credential_reference, webhook_reference, registered_at, provenance)
            VALUES ({pin}::jsonb, {profile.Id}, {profile.CredentialReference}, {profile.WebhookSecretReference}, {clock.GetUtcNow()}, {provenance})
            ON CONFLICT (pin) DO NOTHING
            """, ct);
        var row = await db.Database.SqlQuery<BindingRow>($"""
            SELECT profile_id, credential_reference, webhook_reference FROM public.payment_provider_bindings WHERE pin = {pin}::jsonb
            """).SingleAsync(ct);
        loaded[profile.Pin] = row;
        return Same(row, profile);
    }

    private static bool Same(BindingRow prior, PaymentProviderProfile profile) =>
        prior.ProfileId == profile.Id && prior.CredentialReference == profile.CredentialReference && prior.WebhookReference == profile.WebhookSecretReference;

    private sealed record BindingRow(string ProfileId, string? CredentialReference, string? WebhookReference);
}
