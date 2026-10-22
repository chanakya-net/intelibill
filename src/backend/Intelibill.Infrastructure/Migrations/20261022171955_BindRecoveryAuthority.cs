using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BindRecoveryAuthority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Authority without an issuing destination/version must never survive cutover.
            migrationBuilder.Sql("DELETE FROM password_reset_tokens");
            migrationBuilder.Sql("""
                CREATE FUNCTION public.preserve_identity_business_history() RETURNS trigger
                LANGUAGE plpgsql SET search_path = pg_catalog, public AS $$
                BEGIN
                    NEW.requires_operator_recovery := OLD.requires_operator_recovery OR NEW.requires_operator_recovery;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER preserve_identity_business_history
                    BEFORE UPDATE ON public.users
                    FOR EACH ROW EXECUTE FUNCTION public.preserve_identity_business_history();
                """);
            migrationBuilder.AddColumn<string>(
                name: "destination",
                table: "password_reset_tokens",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "security_version",
                table: "password_reset_tokens",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS preserve_identity_business_history ON public.users; DROP FUNCTION IF EXISTS public.preserve_identity_business_history();");
            migrationBuilder.DropColumn(
                name: "destination",
                table: "password_reset_tokens");

            migrationBuilder.DropColumn(
                name: "security_version",
                table: "password_reset_tokens");
        }
    }
}
