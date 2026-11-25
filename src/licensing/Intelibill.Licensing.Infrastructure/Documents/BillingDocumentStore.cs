using Intelibill.Licensing.Application.Documents;
using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Documents;

internal sealed class BillingDocumentStore(LicensingDbContext db) : IBillingDocumentStore
{
    public async Task<BillingDocumentVersionView[]> ListAsync(Guid account, Guid order, CancellationToken ct)
    {
        var rows = await Rows(account).Where(row => row.OrderId == order)
            .OrderByDescending(row => row.UploadedAt).ThenBy(row => row.VersionId).ToListAsync(ct);
        return rows.Select(row => row.ToView()).ToArray();
    }

    public async Task<StoredBillingDocument?> FindOperationAsync(Guid account, Guid key, CancellationToken ct)
    {
        var row = await Rows(account).SingleOrDefaultAsync(item => item.OperationKey == key, ct);
        return row is null ? null : new(row.ToView(), row.PayloadHash);
    }

    public Task<int> CountConfirmationsAsync(Guid account, Guid order, CancellationToken ct) =>
        db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM public.payment_confirmations WHERE account_id = {account} AND order_id = {order}
            """).SingleAsync(ct);

    public async Task<bool> InsertAsync(StoredBillingDocument document, Guid session, byte[] content, CancellationToken ct)
    {
        var view = document.View;
        return await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.billing_document_versions
                (account_id, order_id, document_id, version_id, version_number, kind, file_name, media_type, byte_length,
                 content_sha256, content, issuer_name, issuer_reference, issued_on, operation_key, payload_hash,
                 admin_id, session_id, mfa_verified_at, uploaded_at)
            VALUES ({view.AccountId}, {view.OrderId}, {view.DocumentId}, {view.VersionId}, {view.Version}, {view.Kind},
                {view.FileName}, {view.MediaType}, {view.ByteLength}, {view.Sha256}, {content}, {view.IssuerName},
                {view.IssuerReference}, {view.IssuedOn}, {view.OperationKey}, {document.PayloadHash},
                {view.UploadedBy}, {session}, {view.MfaVerifiedAt}, {view.UploadedAt})
            ON CONFLICT DO NOTHING
            """, ct) == 1;
    }

    private IQueryable<DocumentRow> Rows(Guid account) => db.Database.SqlQuery<DocumentRow>($"""
        SELECT account_id, order_id, document_id, version_id, version_number AS version, supersedes_version_id,
            kind, file_name, media_type, byte_length, content_sha256 AS sha256, issuer_name, issuer_reference, issued_on,
            operation_key, admin_id AS uploaded_by, mfa_verified_at, uploaded_at, payload_hash
        FROM public.billing_document_versions WHERE account_id = {account}
        """);

    private sealed record DocumentRow(Guid AccountId, Guid OrderId, Guid DocumentId, Guid VersionId, int Version,
        Guid? SupersedesVersionId, string Kind, string FileName, string MediaType, int ByteLength, string Sha256, string IssuerName,
        string IssuerReference, DateOnly IssuedOn, Guid OperationKey, Guid UploadedBy, DateTimeOffset MfaVerifiedAt,
        DateTimeOffset UploadedAt, string PayloadHash)
    {
        public BillingDocumentVersionView ToView() => new(AccountId, OrderId, DocumentId, VersionId, Version, SupersedesVersionId,
            Kind, FileName, MediaType, ByteLength, Sha256, IssuerName, IssuerReference, IssuedOn, OperationKey, UploadedBy,
            MfaVerifiedAt, UploadedAt);
    }
}
