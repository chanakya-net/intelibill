using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Web.Delivery;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

/// <summary>
/// Append-only evidence of what the merchant service observed about a device's offline permission.
/// Apply before the licensing API that accepts it, and before any merchant API that sends it.
/// </summary>
[DbContext(typeof(LicensingDbContext))]
[Migration("20261124100000_OfflineDeviceObservations")]
public sealed class OfflineDeviceObservations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE public.entitlement_merchant_inbox DROP CONSTRAINT entitlement_merchant_inbox_kind_check;
            ALTER TABLE public.entitlement_merchant_inbox ADD CONSTRAINT entitlement_merchant_inbox_kind_check
                CHECK (kind IN ('Acknowledgement', 'Reconciliation', 'DeviceObservation'));
            CREATE TABLE public.offline_device_observations (
                event_id uuid PRIMARY KEY,
                account_id uuid NOT NULL,
                shop_id uuid NOT NULL,
                affiliation_request_id uuid NOT NULL,
                device_ref text NOT NULL CHECK (device_ref ~ '^[0-9a-f]{64}$'),
                observation text NOT NULL CHECK (observation IN ('PermitIssued', 'OfflineSync')),
                observed_at timestamptz NOT NULL CHECK (isfinite(observed_at)),
                permit_id uuid UNIQUE,
                not_before timestamptz CHECK (isfinite(not_before)),
                valid_until timestamptz CHECK (isfinite(valid_until)),
                permit_limit text CHECK (permit_limit IN ('LicenceEnd', 'VerificationFreshness', 'MerchantLimit')),
                account_revision bigint CHECK (account_revision > 0),
                shop_revision bigint CHECK (shop_revision > 0),
                authority_verified_at timestamptz CHECK (isfinite(authority_verified_at)),
                CHECK (CASE WHEN observation = 'PermitIssued' THEN
                        permit_id IS NOT NULL AND not_before IS NOT NULL AND valid_until IS NOT NULL
                        AND permit_limit IS NOT NULL AND account_revision IS NOT NULL AND shop_revision IS NOT NULL
                        AND authority_verified_at IS NOT NULL AND not_before < valid_until AND observed_at = not_before
                    ELSE num_nonnulls(permit_id, not_before, valid_until, permit_limit, account_revision, shop_revision,
                        authority_verified_at) = 0 END)
            );
            CREATE INDEX offline_device_observations_device
                ON public.offline_device_observations (account_id, shop_id, device_ref, observed_at DESC);
            CREATE TRIGGER retain_rows BEFORE UPDATE OR DELETE ON public.offline_device_observations
                FOR EACH ROW EXECUTE FUNCTION public.retain_entitlement_evidence();
            CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.offline_device_observations
                FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();
            """);
        migrationBuilder.Sql(LeasedHttpOutboxSchema.Scope("offline_device_observations",
            "account_id = nullif(current_setting('app.billing_account_id', true), '')::uuid"));
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        LOCK TABLE public.offline_device_observations, public.entitlement_merchant_inbox IN ACCESS EXCLUSIVE MODE;
        ALTER TABLE public.offline_device_observations NO FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.entitlement_merchant_inbox NO FORCE ROW LEVEL SECURITY;
        DO $$ BEGIN
            IF EXISTS (SELECT FROM public.offline_device_observations)
                OR EXISTS (SELECT FROM public.entitlement_merchant_inbox WHERE kind = 'DeviceObservation') THEN
                RAISE EXCEPTION 'Retained device observations prevent downgrade' USING ERRCODE = '55000';
            END IF;
        END $$;
        ALTER TABLE public.entitlement_merchant_inbox FORCE ROW LEVEL SECURITY;
        DROP TABLE public.offline_device_observations;
        ALTER TABLE public.entitlement_merchant_inbox DROP CONSTRAINT entitlement_merchant_inbox_kind_check;
        ALTER TABLE public.entitlement_merchant_inbox ADD CONSTRAINT entitlement_merchant_inbox_kind_check
            CHECK (kind IN ('Acknowledgement', 'Reconciliation'));
        """);
}
