using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

[DbContext(typeof(LicensingDbContext))]
[Migration("20261121101500_ScopeClosureProposalOperationKeys")]
public sealed class ScopeClosureProposalOperationKeys : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE public.operational_closure_proposals ADD COLUMN operation_key uuid;
        CREATE UNIQUE INDEX closure_proposal_account_operation
            ON public.operational_closure_proposals(account_id, coalesce(operation_key, id));
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        LOCK TABLE public.operational_closure_proposals IN ACCESS EXCLUSIVE MODE;
        ALTER TABLE public.operational_closure_proposals NO FORCE ROW LEVEL SECURITY;
        DO $$ BEGIN
            IF EXISTS (SELECT FROM public.operational_closure_proposals WHERE operation_key IS NOT NULL) THEN
                RAISE EXCEPTION 'Retained proposal operation keys prevent downgrade' USING ERRCODE = '55000';
            END IF;
        END $$;
        ALTER TABLE public.operational_closure_proposals FORCE ROW LEVEL SECURITY;
        DROP INDEX public.closure_proposal_account_operation;
        ALTER TABLE public.operational_closure_proposals DROP COLUMN operation_key;
        """);
}
