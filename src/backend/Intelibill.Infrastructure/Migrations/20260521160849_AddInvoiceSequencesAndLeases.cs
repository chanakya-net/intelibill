using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceSequencesAndLeases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "invoice_sequences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fiscal_year_start = table.Column<int>(type: "integer", nullable: false),
                    next_number = table.Column<int>(type: "integer", nullable: false),
                    prefix = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_sequences", x => x.id);
                    table.ForeignKey(
                        name: "fk_invoice_sequences_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "invoice_leases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_sequence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fiscal_year_start = table.Column<int>(type: "integer", nullable: false),
                    device_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    prefix = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    range_start = table.Column<int>(type: "integer", nullable: false),
                    range_end = table.Column<int>(type: "integer", nullable: false),
                    next_number = table.Column<int>(type: "integer", nullable: false),
                    number_padding = table.Column<int>(type: "integer", nullable: false),
                    reserved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_leases", x => x.id);
                    table.ForeignKey(
                        name: "fk_invoice_leases_invoice_sequences_invoice_sequence_id",
                        column: x => x.invoice_sequence_id,
                        principalTable: "invoice_sequences",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_invoice_leases_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_invoice_leases_invoice_sequence_id",
                table: "invoice_leases",
                column: "invoice_sequence_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_leases_shop_id_device_id_fiscal_year_start",
                table: "invoice_leases",
                columns: new[] { "shop_id", "device_id", "fiscal_year_start" });

            migrationBuilder.CreateIndex(
                name: "ix_invoice_leases_shop_id_expires_at",
                table: "invoice_leases",
                columns: new[] { "shop_id", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_invoice_sequences_shop_id_fiscal_year_start",
                table: "invoice_sequences",
                columns: new[] { "shop_id", "fiscal_year_start" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "invoice_leases");

            migrationBuilder.DropTable(
                name: "invoice_sequences");
        }
    }
}
