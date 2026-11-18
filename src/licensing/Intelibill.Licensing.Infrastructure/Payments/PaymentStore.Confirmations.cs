using System.Text.Json;
using Intelibill.Licensing.Application.Payments;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Payments;

internal sealed partial class PaymentStore : IPaymentConfirmationStore
{
    private static readonly JsonSerializerOptions ConfirmationJson = new(JsonSerializerDefaults.Web);

    public Task IssueAsync(Guid account, Guid order, Guid? attempt, Guid? manualReceipt, DateTimeOffset issuedAt, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.payment_confirmations (confirmation_id, account_id, order_id, attempt_id, manual_receipt_id, issued_at, facts)
            VALUES ({id}, {account}, {order}, {attempt}, {manualReceipt}, {issuedAt},
                public.payment_confirmation_snapshot({id}, {account}, {order}, {attempt}, {manualReceipt}, {issuedAt}, 'Verification'))
            """, ct);
    }

    public async Task<PaymentConfirmationV1[]> ListConfirmationsAsync(Guid account, Guid order, CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<string>($"""
            SELECT (facts->'receipt')::text AS "Value" FROM public.payment_confirmations
            WHERE account_id = {account} AND order_id = {order} ORDER BY issued_at, confirmation_id
            """).ToArrayAsync(ct);
        return rows.Select(row => JsonSerializer.Deserialize<PaymentConfirmationV1>(row, ConfirmationJson)!).ToArray();
    }

    public async Task<PaymentConfirmationSnapshotV1?> FindConfirmationAsync(Guid account, Guid order, Guid confirmation, CancellationToken ct)
    {
        var json = await db.Database.SqlQuery<string>($"""
            SELECT facts::text AS "Value" FROM public.payment_confirmations
            WHERE account_id = {account} AND order_id = {order} AND confirmation_id = {confirmation}
            """).SingleOrDefaultAsync(ct);
        return json is null ? null : JsonSerializer.Deserialize<PaymentConfirmationSnapshotV1>(json, ConfirmationJson);
    }
}
