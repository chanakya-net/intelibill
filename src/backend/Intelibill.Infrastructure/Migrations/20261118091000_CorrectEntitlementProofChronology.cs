using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261118091000_CorrectEntitlementProofChronology")]
public sealed class CorrectEntitlementProofChronology : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Accepted consent identifies the account even on the very first delivery for a sibling.
        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION public.merchant_licence_proof_issuance(p_shop_id uuid)
            RETURNS TABLE (shop_issued_at timestamptz, account_issued_at timestamptz)
            LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog AS $$
                SELECT se.issued_at, ae.issued_at
                FROM public.shop_affiliation_consent_events c
                LEFT JOIN public.shop_license_states s ON s.shop_id = c.shop_id AND s.account_id = c.account_id
                    AND s.affiliation_request_id = c.request_id
                LEFT JOIN public.license_grant_evidence se ON se.event_id = s.evidence_id AND se.shop_id = s.shop_id
                LEFT JOIN public.account_license_restrictions a ON a.account_id = c.account_id
                LEFT JOIN public.license_grant_evidence ae ON ae.event_id = a.evidence_id AND ae.account_id = a.account_id
                WHERE c.kind = 'Accepted' AND c.shop_id = p_shop_id
                    AND p_shop_id = nullif(current_setting('app.active_shop_id', true), '')::uuid
            $$;
            """);
        migrationBuilder.Sql(RevisionGuard);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(BoundMerchantEntitlementReconciliation.RefusePopulatedDowngrade);
        migrationBuilder.Sql(AddShopLicensingState.RevisionGuard);
        migrationBuilder.Sql(BoundMerchantEntitlementReconciliation.ProofIssuance);
    }

    private const string RevisionGuard = """
        CREATE OR REPLACE FUNCTION public.guard_license_projection() RETURNS trigger
        LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        DECLARE
            account_scope boolean := TG_TABLE_NAME = 'account_license_restrictions';
            active_shop uuid := nullif(current_setting('app.active_shop_id', true), '')::uuid;
            prior_issued_at timestamptz;
        BEGIN
            IF NEW.revision < OLD.revision OR (NEW.revision = OLD.revision AND (to_jsonb(NEW) - 'enforcement_activated_at')
                IS DISTINCT FROM (to_jsonb(OLD) - 'enforcement_activated_at')) THEN
                RAISE EXCEPTION 'Entitlement head cannot go backwards' USING ERRCODE = '23514';
            END IF;
            IF NEW.upstream_verified_at < OLD.upstream_verified_at THEN
                SELECT CASE WHEN account_scope THEN account_issued_at ELSE shop_issued_at END INTO prior_issued_at
                FROM public.merchant_licence_proof_issuance(active_shop)
                WHERE EXISTS (SELECT FROM public.shop_affiliation_consent_events c
                    WHERE c.kind = 'Accepted' AND c.account_id = OLD.account_id AND c.shop_id = active_shop)
                    AND (account_scope OR active_shop = (to_jsonb(OLD)->>'shop_id')::uuid);
                IF prior_issued_at IS NULL OR OLD.upstream_verified_at < prior_issued_at + interval '1 second'
                    OR NOT EXISTS (SELECT FROM public.license_grant_evidence e
                        WHERE e.event_id = NEW.evidence_id AND e.shop_id = active_shop
                            AND e.account_id = NEW.account_id AND NEW.account_id = OLD.account_id
                            AND NEW.upstream_verified_at < e.issued_at + interval '1 second'
                            AND NEW.upstream_verified_at = CASE WHEN account_scope THEN e.account_upstream_verified_at
                                ELSE e.shop_upstream_verified_at END
                            AND NEW.revision = CASE WHEN account_scope THEN e.account_revision ELSE e.shop_revision END)
                THEN
                    RAISE EXCEPTION 'Entitlement stamp correction requires coherent higher evidence' USING ERRCODE = '23514';
                END IF;
            END IF;
            RETURN NEW;
        END $$;
        """;
}
