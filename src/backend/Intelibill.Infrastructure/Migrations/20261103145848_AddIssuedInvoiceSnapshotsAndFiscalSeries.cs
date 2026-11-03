using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIssuedInvoiceSnapshotsAndFiscalSeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_invoice_sequences_shop_id_fiscal_year_start",
                table: "invoice_sequences");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "e_invoice_attested_at",
                table: "shops",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "e_invoice_attested_by_user_id",
                table: "shops",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "e_invoice_status",
                table: "shops",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "invoice_series_code",
                table: "shops",
                type: "character varying(4)",
                maxLength: 4,
                nullable: false,
                defaultValue: "A");

            migrationBuilder.AddColumn<string>(
                name: "legal_name",
                table: "shops",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "state_code",
                table: "shops",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "tax_regime",
                table: "shops",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "invoice_document",
                table: "sales",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "seller_registration_key",
                table: "invoice_sequences",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "series_code",
                table: "invoice_sequences",
                type: "character varying(4)",
                maxLength: 4,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "seller_profile",
                table: "invoice_leases",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "gstin",
                table: "customers",
                type: "character varying(15)",
                maxLength: 15,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "state_code",
                table: "customers",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            // Existing counters are the legacy per-shop INV-yyyy-yy- series: they keep their prefix and
            // open leases, under series '' and a registration key of their own shop. Runs shop by shop
            // with app.active_shop_id set, because FORCE row level security binds the migrator as
            // table owner (see AddReturnAccountingSnapshots).
            migrationBuilder.Sql("""
                DO $backfill$
                DECLARE
                    target_shop uuid;
                BEGIN
                    FOR target_shop IN SELECT id FROM shops ORDER BY id LOOP
                        PERFORM set_config('app.active_shop_id', target_shop::text, true);

                        UPDATE invoice_sequences
                        SET seller_registration_key = 'legacy:' || shop_id::text
                        WHERE shop_id = target_shop AND seller_registration_key = '';
                    END LOOP;
                END
                $backfill$;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_invoice_sequences_seller_registration_key_fiscal_year_start",
                table: "invoice_sequences",
                columns: new[] { "seller_registration_key", "fiscal_year_start", "series_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoice_sequences_shop_id_fiscal_year_start_series_code",
                table: "invoice_sequences",
                columns: new[] { "shop_id", "fiscal_year_start", "series_code" },
                unique: true);
        }

        /// <inheritdoc />
        /// <remarks>
        /// Restoring the per-shop, per-year index fails once a shop has opened a fiscal series beside
        /// its legacy counter; roll forward instead. Issued documents are dropped with the column.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_invoice_sequences_seller_registration_key_fiscal_year_start",
                table: "invoice_sequences");

            migrationBuilder.DropIndex(
                name: "ix_invoice_sequences_shop_id_fiscal_year_start_series_code",
                table: "invoice_sequences");

            migrationBuilder.DropColumn(
                name: "e_invoice_attested_at",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "e_invoice_attested_by_user_id",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "e_invoice_status",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "invoice_series_code",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "legal_name",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "state_code",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "tax_regime",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "invoice_document",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "seller_registration_key",
                table: "invoice_sequences");

            migrationBuilder.DropColumn(
                name: "series_code",
                table: "invoice_sequences");

            migrationBuilder.DropColumn(
                name: "seller_profile",
                table: "invoice_leases");

            migrationBuilder.DropColumn(
                name: "gstin",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "state_code",
                table: "customers");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_sequences_shop_id_fiscal_year_start",
                table: "invoice_sequences",
                columns: new[] { "shop_id", "fiscal_year_start" },
                unique: true);
        }
    }
}
