using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261121120000_ProjectClosureReopenings")]
public sealed class ProjectClosureReopenings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE public.shop_license_states ADD COLUMN reopened_revision bigint,
            ADD CONSTRAINT coherent_shop_reopening CHECK (reopened_revision IS NULL OR
                (reopened_revision > 0 AND reopened_revision < revision));
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        LOCK TABLE public.shop_license_states IN ACCESS EXCLUSIVE MODE;
        ALTER TABLE public.shop_license_states NO FORCE ROW LEVEL SECURITY;
        DO $$ BEGIN
            IF EXISTS (SELECT FROM public.shop_license_states WHERE reopened_revision IS NOT NULL) THEN
                RAISE EXCEPTION 'Applied reopening evidence prevents downgrade' USING ERRCODE = '55000';
            END IF;
        END $$;
        ALTER TABLE public.shop_license_states FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.shop_license_states DROP COLUMN reopened_revision;
        """);
}
