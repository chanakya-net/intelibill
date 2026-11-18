using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261118080000_AddLicenceEnforcementFences")]
public sealed class AddLicenceEnforcementFences : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE FUNCTION public.take_merchant_licence_fence(p_account_id uuid, p_shop_id uuid, p_shared boolean)
        RETURNS void LANGUAGE plpgsql SECURITY INVOKER SET search_path = pg_catalog AS $$
        DECLARE
            fence_key bigint;
        BEGIN
            -- Preserve the projection writer's original keys for rolling upgrades.
            -- Account before shop. The shop key also fences first delivery when no projection row exists.
            FOREACH fence_key IN ARRAY ARRAY[
                hashtextextended('a' || p_account_id::text, 859),
                hashtextextended('s' || p_shop_id::text, 859)
            ] LOOP
                IF fence_key IS NOT NULL THEN
                    IF p_shared THEN
                        PERFORM pg_advisory_xact_lock_shared(fence_key);
                    ELSE
                        PERFORM pg_advisory_xact_lock(fence_key);
                    END IF;
                END IF;
            END LOOP;
        END $$;
        """);

    // Each migration commits on its own, so the predecessor's refusal would come after this drop was final.
    // Refuse here first. The shop table locks first because projection writers read it before writing the others.
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        LOCK TABLE public.shop_license_states, public.license_grant_evidence, public.entitlement_messages
            IN ACCESS EXCLUSIVE MODE;
        ALTER TABLE public.shop_license_states NO FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.license_grant_evidence NO FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.entitlement_messages NO FORCE ROW LEVEL SECURITY;
        DO $$ BEGIN
            IF EXISTS (SELECT FROM public.license_grant_evidence) OR EXISTS (SELECT FROM public.shop_license_states)
                OR EXISTS (SELECT FROM public.entitlement_messages) THEN
                RAISE EXCEPTION 'Retained entitlement evidence prevents downgrade' USING ERRCODE = '55000';
            END IF;
        END $$;
        ALTER TABLE public.shop_license_states FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.license_grant_evidence FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.entitlement_messages FORCE ROW LEVEL SECURITY;
        DROP FUNCTION public.take_merchant_licence_fence(uuid, uuid, boolean);
        """);
}
