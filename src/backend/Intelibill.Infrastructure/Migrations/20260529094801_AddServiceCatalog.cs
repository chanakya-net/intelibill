using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "service_code_sequences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    next_number = table.Column<int>(type: "integer", nullable: false),
                    prefix = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_code_sequences", x => x.id);
                    table.ForeignKey(
                        name: "fk_service_code_sequences_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "services",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    name = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    hsn_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    tax_rate_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false, defaultValue: 0m),
                    tax_included = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_services", x => x.id);
                    table.ForeignKey(
                        name: "fk_services_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_service_code_sequences_shop_id",
                table: "service_code_sequences",
                column: "shop_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_services_shop_id_code",
                table: "services",
                columns: new[] { "shop_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_services_shop_id_is_active",
                table: "services",
                columns: new[] { "shop_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_services_shop_id_name",
                table: "services",
                columns: new[] { "shop_id", "name" },
                unique: true);

            migrationBuilder.Sql("""
                ALTER TABLE services ENABLE ROW LEVEL SECURITY;
                ALTER TABLE services FORCE ROW LEVEL SECURITY;
                CREATE POLICY services_shop_isolation ON services
                    USING (shop_id = current_setting('app.active_shop_id', true)::uuid);

                ALTER TABLE service_code_sequences ENABLE ROW LEVEL SECURITY;
                ALTER TABLE service_code_sequences FORCE ROW LEVEL SECURITY;
                CREATE POLICY service_code_sequences_shop_isolation ON service_code_sequences
                    USING (shop_id = current_setting('app.active_shop_id', true)::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP POLICY IF EXISTS services_shop_isolation ON services;
                DROP POLICY IF EXISTS service_code_sequences_shop_isolation ON service_code_sequences;
                """);

            migrationBuilder.DropTable(
                name: "service_code_sequences");

            migrationBuilder.DropTable(
                name: "services");
        }
    }
}
#pragma warning restore CA1861

