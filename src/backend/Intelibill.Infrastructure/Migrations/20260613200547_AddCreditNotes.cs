using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCreditNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "credit_notes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    original_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    available_balance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_voided = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_notes", x => x.id);
                    table.CheckConstraint("ck_credit_notes_available_balance_non_negative", "available_balance >= 0");
                    table.CheckConstraint("ck_credit_notes_balance_le_original", "available_balance <= original_amount");
                    table.CheckConstraint("ck_credit_notes_original_amount_positive", "original_amount > 0");
                    table.ForeignKey(
                        name: "fk_credit_notes_sales_sale_id",
                        column: x => x.sale_id,
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_credit_notes_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "credit_note_redemptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    credit_note_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_note_redemptions", x => x.id);
                    table.CheckConstraint("ck_credit_note_redemptions_amount_positive", "amount > 0");
                    table.ForeignKey(
                        name: "fk_credit_note_redemptions_credit_notes_credit_note_id",
                        column: x => x.credit_note_id,
                        principalTable: "credit_notes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_credit_note_redemptions_sales_sale_id",
                        column: x => x.sale_id,
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_credit_note_redemptions_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_credit_note_redemptions_credit_note_id",
                table: "credit_note_redemptions",
                column: "credit_note_id");

            migrationBuilder.CreateIndex(
                name: "ix_credit_note_redemptions_sale_id",
                table: "credit_note_redemptions",
                column: "sale_id");

            migrationBuilder.CreateIndex(
                name: "ix_credit_note_redemptions_shop_id_sale_id",
                table: "credit_note_redemptions",
                columns: new[] { "shop_id", "sale_id" });

            migrationBuilder.CreateIndex(
                name: "ix_credit_notes_sale_id",
                table: "credit_notes",
                column: "sale_id");

            migrationBuilder.CreateIndex(
                name: "ix_credit_notes_shop_id_code",
                table: "credit_notes",
                columns: new[] { "shop_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_credit_notes_shop_id_is_voided",
                table: "credit_notes",
                columns: new[] { "shop_id", "is_voided" });

            migrationBuilder.CreateIndex(
                name: "ix_credit_notes_shop_id_sale_id",
                table: "credit_notes",
                columns: new[] { "shop_id", "sale_id" });

            migrationBuilder.Sql("""
                ALTER TABLE credit_notes ENABLE ROW LEVEL SECURITY;
                ALTER TABLE credit_notes FORCE ROW LEVEL SECURITY;
                CREATE POLICY credit_notes_shop_isolation ON credit_notes
                    USING (shop_id = current_setting('app.active_shop_id', true)::uuid);

                ALTER TABLE credit_note_redemptions ENABLE ROW LEVEL SECURITY;
                ALTER TABLE credit_note_redemptions FORCE ROW LEVEL SECURITY;
                CREATE POLICY credit_note_redemptions_shop_isolation ON credit_note_redemptions
                    USING (shop_id = current_setting('app.active_shop_id', true)::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP POLICY IF EXISTS credit_notes_shop_isolation ON credit_notes;
                DROP POLICY IF EXISTS credit_note_redemptions_shop_isolation ON credit_note_redemptions;
                """);

            migrationBuilder.DropTable(
                name: "credit_note_redemptions");

            migrationBuilder.DropTable(
                name: "credit_notes");
        }
    }
}

#pragma warning restore CA1861
