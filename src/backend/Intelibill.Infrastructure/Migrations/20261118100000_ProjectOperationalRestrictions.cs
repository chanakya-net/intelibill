using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261118100000_ProjectOperationalRestrictions")]
public sealed class ProjectOperationalRestrictions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE public.account_license_restrictions
                ADD COLUMN suspension_effective_at timestamptz,
                ADD COLUMN suspension_explanation text,
                ADD CONSTRAINT coherent_account_suspension CHECK (
                    (suspension_effective_at IS NULL AND suspension_explanation IS NULL)
                    OR (suspension_effective_at IS NOT NULL AND isfinite(suspension_effective_at)
                        AND suspension_explanation IS NOT NULL AND length(suspension_explanation) BETWEEN 1 AND 500
                        AND restriction IN ('Trading', 'Security')));
            ALTER TABLE public.shop_license_states
                ADD COLUMN suspension_mode text,
                ADD COLUMN suspension_effective_at timestamptz,
                ADD COLUMN suspension_explanation text,
                ADD CONSTRAINT coherent_shop_suspension CHECK (
                    (suspension_mode IS NULL AND suspension_effective_at IS NULL AND suspension_explanation IS NULL)
                    OR (suspension_mode IS NOT NULL AND suspension_mode IN ('Trading', 'Security')
                        AND suspension_effective_at IS NOT NULL AND isfinite(suspension_effective_at)
                        AND suspension_explanation IS NOT NULL AND length(suspension_explanation) BETWEEN 1 AND 500));
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Each downgrade commits separately. Keep the current readers and schema compatible when
        // an older downgrade would refuse due to retained projection evidence.
        migrationBuilder.Sql(BoundMerchantEntitlementReconciliation.RefusePopulatedDowngrade);
        migrationBuilder.Sql("""
            ALTER TABLE public.account_license_restrictions DROP COLUMN suspension_effective_at, DROP COLUMN suspension_explanation;
            ALTER TABLE public.shop_license_states DROP COLUMN suspension_mode, DROP COLUMN suspension_effective_at,
                DROP COLUMN suspension_explanation;
            """);
    }
}
