using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "operation_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_type = table.Column<int>(type: "integer", nullable: false),
                    operation_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    hash_version = table.Column<int>(type: "integer", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    response_version = table.Column<int>(type: "integer", nullable: false),
                    response_body = table.Column<string>(type: "jsonb", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operation_receipts", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_operation_receipts_shop_id_operation_type_operation_key",
                table: "operation_receipts",
                columns: new[] { "shop_id", "operation_type", "operation_key" },
                unique: true);

            // Tenant data: the unique index leads with shop_id, which is the index the policy
            // predicate needs. Same predicate as CompleteTenantRowLevelSecurity.
            migrationBuilder.Sql("""
                ALTER TABLE operation_receipts ENABLE ROW LEVEL SECURITY;
                ALTER TABLE operation_receipts FORCE ROW LEVEL SECURITY;
                CREATE POLICY operation_receipts_shop_isolation ON operation_receipts
                    USING (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid)
                    WITH CHECK (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Receipts are retained financial records; rolling this back discards them.
            migrationBuilder.Sql("DROP POLICY IF EXISTS operation_receipts_shop_isolation ON operation_receipts;");

            migrationBuilder.DropTable(
                name: "operation_receipts");
        }
    }
}
