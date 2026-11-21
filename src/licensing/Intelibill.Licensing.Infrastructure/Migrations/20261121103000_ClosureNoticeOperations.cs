using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

[DbContext(typeof(LicensingDbContext))]
[Migration("20261121103000_ClosureNoticeOperations")]
public sealed class ClosureNoticeOperations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE public.operational_closure_consent_notices
            DROP CONSTRAINT operational_closure_consent_notices_pkey,
            ADD COLUMN actor_id uuid,
            ADD COLUMN operation_key uuid,
            ADD CHECK ((actor_id IS NULL) = (operation_key IS NULL)),
            ADD UNIQUE NULLS NOT DISTINCT (proposal_id, shop_id, actor_id, operation_key);
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        LOCK TABLE public.operational_closure_consent_notices IN ACCESS EXCLUSIVE MODE;
        ALTER TABLE public.operational_closure_consent_notices NO FORCE ROW LEVEL SECURITY;
        DO $$ BEGIN
            IF EXISTS (SELECT FROM public.operational_closure_consent_notices WHERE operation_key IS NOT NULL) THEN
                RAISE EXCEPTION 'Retained consent operations prevent downgrade' USING ERRCODE = '55000';
            END IF;
        END $$;
        ALTER TABLE public.operational_closure_consent_notices FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.operational_closure_consent_notices
            DROP COLUMN actor_id, DROP COLUMN operation_key, ADD PRIMARY KEY (proposal_id, shop_id);
        """);
}
