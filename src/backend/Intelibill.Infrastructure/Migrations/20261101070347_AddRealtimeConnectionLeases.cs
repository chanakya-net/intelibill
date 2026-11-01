using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1861

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRealtimeConnectionLeases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "realtime_connection_leases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    instance_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    hub_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    security_version = table.Column<long>(type: "bigint", nullable: false),
                    scope_version = table.Column<long>(type: "bigint", nullable: false),
                    connected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_realtime_connection_leases", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_realtime_connection_leases_connection_id",
                table: "realtime_connection_leases",
                column: "connection_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_realtime_connection_leases_expires_at",
                table: "realtime_connection_leases",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_realtime_connection_leases_hub_name_shop_id",
                table: "realtime_connection_leases",
                columns: new[] { "hub_name", "shop_id" });

            migrationBuilder.CreateIndex(
                name: "ix_realtime_connection_leases_instance_id",
                table: "realtime_connection_leases",
                column: "instance_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "realtime_connection_leases");
        }
    }
}
#pragma warning restore CA1861
