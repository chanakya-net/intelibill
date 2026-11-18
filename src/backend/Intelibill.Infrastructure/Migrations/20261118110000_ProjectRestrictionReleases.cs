using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261118110000_ProjectRestrictionReleases")]
public sealed class ProjectRestrictionReleases : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE public.account_license_restrictions ADD COLUMN previous_release_revision bigint,
            ADD CONSTRAINT coherent_previous_release CHECK (previous_release_revision IS NULL OR
                (previous_release_revision > 0 AND previous_release_revision < revision AND restriction IN ('Trading', 'Security')));
        ALTER TABLE public.shop_license_states ADD COLUMN released_revision bigint,
            ADD CONSTRAINT coherent_shop_release CHECK (released_revision IS NULL OR
                (released_revision > 0 AND released_revision < revision));
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(BoundMerchantEntitlementReconciliation.RefusePopulatedDowngrade);
        migrationBuilder.Sql("""
            ALTER TABLE public.account_license_restrictions DROP COLUMN previous_release_revision;
            ALTER TABLE public.shop_license_states DROP COLUMN released_revision;
            """);
    }
}
