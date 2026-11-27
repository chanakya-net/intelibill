using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Billing;

internal sealed partial class MerchantClosureConsentStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private async Task<ShopClosureReviewV1> ReadReviewAsync(ClosureAccess access, CancellationToken ct)
    {
        var shop = access.Binding.ShopId;
        var row = await ReadEvidenceAsync(shop, ct);
        var devices = JsonSerializer.Deserialize<ClosureDeviceV1[]>(row.DeviceEvidence, Json)!;
        return new(shop, access.Owner.ShopName, access.Binding.AccountId, access.Binding.AffiliationRequestId,
            access.Binding.ClosureRevision ?? (access.Binding.State == ShopClosuresV1.Closed ? 1 : 0), access.Owner.Version, clock.GetUtcNow(),
            SignedServiceAssertion.Digest(JsonSerializer.SerializeToUtf8Bytes(
                new { access.Binding.AccountId, access.Binding.AffiliationRequestId, shop, access.Owner.MembershipId,
                    access.Owner.Version, row.Evidence }, Json)), row.OpenTills, row.UnresolvedCases,
            row.ObservedSubmissions, row.LinkedSubmissions, row.IncompleteIntakes, row.UnreadableIntakes,
            row.ObservedDevices, devices);
    }

    // A single MVCC snapshot covers counts, dispositions and the digest. Private row identifiers exist
    // only in the digest input. Recovery imports do not establish contact with the original device.
    private Task<ObservedWork> ReadEvidenceAsync(Guid shop, CancellationToken ct) => db.Database.SqlQuery<ObservedWork>($$"""
        WITH tills AS (
            SELECT id, xmin::text AS version FROM public.till_sessions WHERE shop_id = {{shop}} AND state = 1
        ), cases AS (
            SELECT id, xmin::text AS version, status, offline_submission_id FROM public.reconciliation_issues
            WHERE shop_id = {{shop}} AND status IN (1, 2)
        ), submissions AS (
            SELECT id, device_id, received_at, source, sale_id FROM public.offline_submissions WHERE shop_id = {{shop}}
        ), leases AS (
            SELECT id, device_id, reserved_at, expires_at FROM public.invoice_leases WHERE shop_id = {{shop}}
        ), contacts AS (
            SELECT device_id, max(contact_at) AS last_contact_at, max(expires_at) AS recorded_lease_ends_at
            FROM (
                SELECT device_id, reserved_at AS contact_at, expires_at FROM leases
                UNION ALL SELECT device_id, received_at, NULL FROM submissions WHERE source = 1
            ) observed GROUP BY device_id
        ), manifests AS (
            SELECT m.id, m.operation_key,
                CASE WHEN (m.response_body->>'rowCount') ~ '^[0-9]{1,5}$'
                    THEN (m.response_body->>'rowCount')::integer END AS expected
            FROM public.operation_receipts m WHERE shop_id = {{shop}} AND operation_type = 3 AND status = 2
        ), intake AS (
            SELECT m.id, m.expected, count(r.id) AS committed
            FROM manifests m LEFT JOIN public.operation_receipts r ON r.shop_id = {{shop}}
                AND r.operation_type = 4 AND r.status = 2
                AND starts_with(r.operation_key, (length(m.operation_key) + length(regexp_replace(m.operation_key,
                    '[^' || chr(65536) || '-' || chr(1114111) || ']', '', 'g')))::text || ':' || m.operation_key || ':')
            GROUP BY m.id, m.expected
        )
        SELECT (SELECT count(*)::integer FROM tills) AS open_tills,
            (SELECT count(*)::integer FROM cases) AS unresolved_cases,
            (SELECT count(*)::integer FROM submissions) AS observed_submissions,
            (SELECT count(*)::integer FROM submissions WHERE sale_id IS NOT NULL) AS linked_submissions,
            (SELECT count(*)::integer FROM intake WHERE committed < expected) AS incomplete_intakes,
            (SELECT count(*)::integer FROM intake WHERE expected IS NULL) AS unreadable_intakes,
            (SELECT count(*)::integer FROM contacts) AS observed_devices,
            (SELECT coalesce(jsonb_agg(jsonb_build_object('reference', reference, 'lastContactAt', last_contact_at,
                'recordedLeaseEndsAt', recorded_lease_ends_at) ORDER BY last_contact_at DESC, reference), '[]')::text
             FROM (SELECT substr(encode(sha256(convert_to({{shop}}::text || ':' || device_id, 'UTF8')), 'hex'), 1, 24) AS reference, last_contact_at, recorded_lease_ends_at
                 FROM contacts ORDER BY last_contact_at DESC, reference LIMIT 20) recent) AS device_evidence,
            encode(sha256(convert_to(jsonb_build_array(
                (SELECT jsonb_agg(to_jsonb(tills) ORDER BY id) FROM tills),
                (SELECT jsonb_agg(to_jsonb(cases) ORDER BY id) FROM cases),
                (SELECT jsonb_agg(jsonb_build_array(id, source, sale_id, received_at) ORDER BY id) FROM submissions),
                (SELECT jsonb_agg(jsonb_build_array(id, reserved_at, expires_at) ORDER BY id) FROM leases),
                (SELECT jsonb_agg(to_jsonb(intake) ORDER BY id) FROM intake WHERE committed < expected OR expected IS NULL)
            )::text, 'UTF8')), 'hex') AS evidence
        """).SingleAsync(ct);

    private sealed record ObservedWork(int OpenTills, int UnresolvedCases, int ObservedSubmissions, int LinkedSubmissions,
        int IncompleteIntakes, int UnreadableIntakes, int ObservedDevices, string DeviceEvidence, string Evidence);
}
