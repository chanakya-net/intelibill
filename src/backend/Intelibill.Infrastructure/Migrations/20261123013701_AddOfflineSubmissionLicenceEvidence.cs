using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOfflineSubmissionLicenceEvidence : Migration
    {
        private static readonly string[] GrantReference = ["licence_grant_id", "shop_id"];
        private static readonly string[] PermitReference = ["licence_permit_id", "shop_id"];
        private static readonly string[] TenantKey = ["id", "shop_id"];
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "licence_grant_id",
                table: "offline_submissions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "licence_permit_id",
                table: "offline_submissions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_offline_license_permits_id_shop_id",
                table: "offline_license_permits",
                columns: TenantKey);

            migrationBuilder.CreateIndex(
                name: "ix_offline_submissions_licence_grant_id_shop_id",
                table: "offline_submissions",
                columns: GrantReference);

            migrationBuilder.CreateIndex(
                name: "ix_offline_submissions_licence_permit_id_shop_id",
                table: "offline_submissions",
                columns: PermitReference);

            migrationBuilder.AddForeignKey(
                name: "fk_offline_submissions_offline_license_grants_licence_grant_id",
                table: "offline_submissions",
                columns: GrantReference,
                principalTable: "offline_license_grants",
                principalColumns: TenantKey,
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_offline_submissions_offline_license_permits_licence_permit_",
                table: "offline_submissions",
                columns: PermitReference,
                principalTable: "offline_license_permits",
                principalColumns: TenantKey,
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
                CREATE TRIGGER retain_offline_licence_receipt
                BEFORE UPDATE OF licence_grant_id, licence_permit_id, payload_json, payload_hash, received_at,
                    shop_id, device_id, client_sale_id, invoice_number, claimed_sold_at, original_lease_id,
                    original_issuer_user_id, submitted_by_user_id, asserted_issuer_user_id ON public.offline_submissions
                FOR EACH ROW WHEN (OLD.payload_json ? 'licenceEvidence' OR NEW.payload_json ? 'licenceEvidence')
                EXECUTE FUNCTION public.retain_entitlement_evidence();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE public.offline_submissions IN ACCESS EXCLUSIVE MODE;
                ALTER TABLE public.offline_submissions NO FORCE ROW LEVEL SECURITY;
                DO $$ BEGIN
                    IF EXISTS (SELECT FROM public.offline_submissions WHERE payload_json ? 'licenceEvidence') THEN
                        RAISE EXCEPTION 'Cannot remove historical licence references after receipt';
                    END IF;
                END $$;
                ALTER TABLE public.offline_submissions FORCE ROW LEVEL SECURITY;
                DROP TRIGGER retain_offline_licence_receipt ON public.offline_submissions;
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_offline_submissions_offline_license_grants_licence_grant_id",
                table: "offline_submissions");

            migrationBuilder.DropForeignKey(
                name: "fk_offline_submissions_offline_license_permits_licence_permit_",
                table: "offline_submissions");

            migrationBuilder.DropIndex(
                name: "ix_offline_submissions_licence_grant_id_shop_id",
                table: "offline_submissions");

            migrationBuilder.DropIndex(
                name: "ix_offline_submissions_licence_permit_id_shop_id",
                table: "offline_submissions");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_offline_license_permits_id_shop_id",
                table: "offline_license_permits");

            migrationBuilder.DropColumn(
                name: "licence_grant_id",
                table: "offline_submissions");

            migrationBuilder.DropColumn(
                name: "licence_permit_id",
                table: "offline_submissions");
        }
    }
}
