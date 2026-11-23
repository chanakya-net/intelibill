using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Infrastructure.Migrations;

/// <summary>
/// Lets the merchant outbox carry device observations (permit window, offline upload receipt) to licensing.
/// Apply after the licensing service accepts them and before this API version writes any.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261124090000_AllowDeviceObservationMerchantEvents")]
public sealed class AllowDeviceObservationMerchantEvents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE public.entitlement_messages DROP CONSTRAINT entitlement_messages_kind_check;
        ALTER TABLE public.entitlement_messages ADD CONSTRAINT entitlement_messages_kind_check
            CHECK (kind IN ('Snapshot', 'Acknowledgement', 'Reconciliation', 'DeviceObservation'));
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        LOCK TABLE public.entitlement_messages IN ACCESS EXCLUSIVE MODE;
        ALTER TABLE public.entitlement_messages NO FORCE ROW LEVEL SECURITY;
        DO $$ BEGIN
            IF EXISTS (SELECT FROM public.entitlement_messages WHERE kind = 'DeviceObservation') THEN
                RAISE EXCEPTION 'Retained device observations prevent downgrade' USING ERRCODE = '55000';
            END IF;
        END $$;
        ALTER TABLE public.entitlement_messages FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.entitlement_messages DROP CONSTRAINT entitlement_messages_kind_check;
        ALTER TABLE public.entitlement_messages ADD CONSTRAINT entitlement_messages_kind_check
            CHECK (kind IN ('Snapshot', 'Acknowledgement', 'Reconciliation'));
        """);
}
