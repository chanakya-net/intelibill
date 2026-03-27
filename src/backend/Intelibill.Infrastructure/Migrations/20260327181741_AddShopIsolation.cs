using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1861

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShopIsolation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "shops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shops", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shop_memberships",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shop_memberships", x => x.id);
                    table.ForeignKey(
                        name: "fk_shop_memberships_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_shop_memberships_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_shop_memberships_shop_id",
                table: "shop_memberships",
                column: "shop_id");

            migrationBuilder.CreateIndex(
                name: "ix_shop_memberships_user_id_is_default",
                table: "shop_memberships",
                columns: new[] { "user_id", "is_default" },
                unique: true,
                filter: "is_default = true");

            migrationBuilder.CreateIndex(
                name: "ix_shop_memberships_user_id_last_used_at",
                table: "shop_memberships",
                columns: new[] { "user_id", "last_used_at" });

            migrationBuilder.CreateIndex(
                name: "ix_shop_memberships_user_id_shop_id",
                table: "shop_memberships",
                columns: new[] { "user_id", "shop_id" },
                unique: true);

            migrationBuilder.Sql(
                """
                ALTER TABLE shop_memberships ENABLE ROW LEVEL SECURITY;
                ALTER TABLE shops ENABLE ROW LEVEL SECURITY;

                CREATE POLICY shop_memberships_user_policy
                    ON shop_memberships
                    USING (user_id = NULLIF(current_setting('app.current_user_id', true), '')::uuid)
                    WITH CHECK (user_id = NULLIF(current_setting('app.current_user_id', true), '')::uuid);

                CREATE POLICY shops_membership_policy
                    ON shops
                    USING (
                        EXISTS (
                            SELECT 1
                            FROM shop_memberships sm
                            WHERE sm.shop_id = shops.id
                              AND sm.user_id = NULLIF(current_setting('app.current_user_id', true), '')::uuid
                        )
                    )
                    WITH CHECK (
                        EXISTS (
                            SELECT 1
                            FROM shop_memberships sm
                            WHERE sm.shop_id = shops.id
                              AND sm.user_id = NULLIF(current_setting('app.current_user_id', true), '')::uuid
                        )
                    );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS shops_membership_policy ON shops;
                DROP POLICY IF EXISTS shop_memberships_user_policy ON shop_memberships;

                ALTER TABLE shops DISABLE ROW LEVEL SECURITY;
                ALTER TABLE shop_memberships DISABLE ROW LEVEL SECURITY;
                """);

            migrationBuilder.DropTable(
                name: "shop_memberships");

            migrationBuilder.DropTable(
                name: "shops");
        }
    }
}

#pragma warning restore CA1861
