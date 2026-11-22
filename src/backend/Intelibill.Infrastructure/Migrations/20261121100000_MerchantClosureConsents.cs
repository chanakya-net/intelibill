using Intelibill.Infrastructure.Data;
using Intelibill.Shared.Web.Delivery;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261121100000_MerchantClosureConsents")]
public sealed class MerchantClosureConsents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(StateConstraint(",'Closed'"));
        migrationBuilder.Sql("""
            CREATE TABLE public.shop_closure_consents (
                id uuid PRIMARY KEY,
                shop_id uuid NOT NULL REFERENCES public.shops(id),
                account_id uuid NOT NULL,
                affiliation_id uuid NOT NULL,
                proposal_id uuid NOT NULL,
                is_account_closure boolean NOT NULL,
                operation_key uuid NOT NULL,
                actor_id uuid NOT NULL,
                membership_id uuid NOT NULL,
                owner_version bigint NOT NULL CHECK (owner_version >= 0),
                closure_version bigint NOT NULL CHECK (closure_version IN (0, 1)),
                scope_digest text NOT NULL CHECK (scope_digest ~ '^[0-9a-f]{64}$'),
                work_digest text NOT NULL CHECK (work_digest ~ '^[0-9a-f]{64}$'),
                evidence jsonb NOT NULL,
                recorded_at timestamptz NOT NULL CHECK (isfinite(recorded_at)),
                UNIQUE (shop_id, operation_key)
            );
            CREATE INDEX shop_closure_consent_proposal ON public.shop_closure_consents(shop_id, proposal_id);
            CREATE TRIGGER retain_rows BEFORE UPDATE OR DELETE ON public.shop_closure_consents
                FOR EACH ROW EXECUTE FUNCTION public.retain_entitlement_evidence();
            CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.shop_closure_consents
                FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();
            """);
        migrationBuilder.Sql(LeasedHttpOutboxSchema.Scope("shop_closure_consents",
            "shop_id = nullif(current_setting('app.active_shop_id', true), '')::uuid"));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // EF commits each downgrade separately. Keep the closure schema when retained
        // entitlement evidence would prevent a later downgrade from completing.
        migrationBuilder.Sql(BoundMerchantEntitlementReconciliation.RefusePopulatedDowngrade);
        migrationBuilder.Sql("""
            LOCK TABLE public.shop_closure_consents IN ACCESS EXCLUSIVE MODE;
            ALTER TABLE public.shop_closure_consents NO FORCE ROW LEVEL SECURITY;
            DO $$ BEGIN
                IF EXISTS (SELECT FROM public.shop_closure_consents) THEN
                    RAISE EXCEPTION 'Retained closure evidence prevents downgrade' USING ERRCODE = '55000';
                END IF;
            END $$;
            DROP TABLE public.shop_closure_consents;
            """);
        migrationBuilder.Sql(StateConstraint(""));
    }

    private static string StateConstraint(string extra) => $"""
        ALTER TABLE public.shop_license_states DROP CONSTRAINT valid_shop_license_state;
        ALTER TABLE public.shop_license_states ADD CONSTRAINT valid_shop_license_state CHECK (
            revision > 0 AND isfinite(upstream_verified_at) AND isfinite(starts_at) AND isfinite(access_ends_at)
            AND isfinite(next_boundary) AND isfinite(enforcement_activated_at)
            AND next_boundary > upstream_verified_at AND shop_capacity > 0 AND member_allowance > 0
            AND state IN ('PendingActivation','Scheduled','Active','Grace','Expired','Suspended','EvidenceIncomplete','NeedsReview'{extra})
            AND (state NOT IN ('Active', 'Grace') OR (term_id IS NOT NULL AND basis IS NOT NULL
                AND starts_at IS NOT NULL AND access_ends_at IS NOT NULL
                AND starts_at <= upstream_verified_at AND access_ends_at > upstream_verified_at)));
        """;
}
