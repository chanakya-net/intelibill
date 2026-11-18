using Intelibill.Application.Common.Licensing;
using System.Net;
using System.Text.Json;
using Intelibill.Domain.Entities;
using Intelibill.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Intelibill.Shared.Web.Delivery;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Licensing;

[LicenceAction(LicenceAction.InternalService)]
internal sealed class MerchantEntitlementDelivery(ApplicationDbContext db, EntitlementProjectionStore projection,
    MerchantEntitlementTransport transport, LeasedHttpOutbox outbox, IHttpClientFactory clients, TimeProvider clock)
    : ILeasedHttpDestination
{
    public bool Enabled => transport.Origin is not null;

    public async Task<string?> SendAsync(LeasedHttpMessage message, CancellationToken ct)
    {
        var path = message.Kind switch
        {
            "Acknowledgement" => EntitlementSnapshotsV1.AcknowledgementPath,
            "Reconciliation" => EntitlementSnapshotsV1.ReconciliationPath,
            _ => null,
        };
        if (transport.Origin is not { } origin || path is null ||
            await transport.SignAsync(path, message.Payload, ct) is not { } assertion)
            return "Unavailable";

        using var client = clients.CreateClient(LeasedHttpDeliveryPump.ClientName);
        using var response = await LeasedHttpDeliveryPump.PostAsync(client, origin, path, message.Payload, assertion, ct);
        return response.StatusCode == HttpStatusCode.NoContent ? null :
            response.IsSuccessStatusCode ? "InvalidResponse" : response.StatusCode is >= HttpStatusCode.InternalServerError
            ? "Unavailable" : "Rejected";
    }

    public async Task QueueDueAsync(CancellationToken ct)
    {
        var now = EntitlementSnapshotsV1.Instant(clock);
        var due = await db.Database.SqlQuery<Binding>(
            $"SELECT * FROM public.due_merchant_entitlement_reconciliation({now})").ToListAsync(ct);
        foreach (var binding in due)
            await QueueAsync(binding, now, ct);
    }

    private async Task QueueAsync(Binding binding, DateTimeOffset now, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if ((await projection.EnterAsync(binding.AccountId, binding.ShopId, ct)).IsError)
            return;
        if (!await StillDueAsync(binding.ShopId, now, ct))
            return;
        var shop = await db.Set<ShopLicenseState>().AsNoTracking().SingleOrDefaultAsync(row => row.ShopId == binding.ShopId, ct);
        var account = await db.Set<AccountLicenseRestriction>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.AccountId == binding.AccountId, ct);
        if (shop is not null && account is not null && now < shop.UpstreamVerifiedAt.AddHours(47) &&
            now < account.UpstreamVerifiedAt.AddHours(47) && (shop.NextBoundary is null || now < shop.NextBoundary))
            return;

        var request = new EntitlementReconciliationV1(1, Guid.NewGuid(), binding.AccountId, binding.ShopId,
            binding.AffiliationRequestId, account?.Revision, shop?.Revision);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(request, EntitlementSnapshotsV1.Json);
        await outbox.AppendAsync(request.EventId, binding.AccountId, binding.ShopId, "Reconciliation",
            bytes, SignedServiceAssertion.Digest(bytes), now, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.entitlement_reconciliation_requests (shop_id, event_id, next_check_at)
            VALUES ({binding.ShopId}, {request.EventId}, {now.AddHours(1)})
            ON CONFLICT (shop_id) DO UPDATE SET event_id = EXCLUDED.event_id, next_check_at = EXCLUDED.next_check_at
            """, ct);
        await transaction.CommitAsync(ct);
    }

    private Task<bool> StillDueAsync(Guid shop, DateTimeOffset now, CancellationToken ct) =>
        db.Database.SqlQuery<bool>($"""
            SELECT NOT EXISTS (SELECT FROM public.entitlement_reconciliation_requests r
                JOIN public.entitlement_outbox o ON o.event_id = r.event_id WHERE r.shop_id = {shop}
                AND (r.next_check_at > {now} OR o.status NOT IN ('Delivered', 'DeadLetter'))) AS "Value"
            """).SingleAsync(ct);

    private sealed record Binding(Guid AccountId, Guid ShopId, Guid AffiliationRequestId);
}
