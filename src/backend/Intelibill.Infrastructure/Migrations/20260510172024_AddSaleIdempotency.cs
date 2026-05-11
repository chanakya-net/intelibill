using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSaleIdempotency : Migration
    {
        private static readonly string[] IdempotencyIndexColumns =
        [
            "shop_id",
            "actor_user_id",
            "idempotency_key",
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "actor_user_id",
                table: "sales",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                table: "sales",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "request_hash",
                table: "sales",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "warnings",
                table: "sales",
                type: "text[]",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE sales
                SET actor_user_id = '00000000-0000-0000-0000-000000000000',
                    idempotency_key = CONCAT('legacy-', id::text),
                    request_hash = CONCAT('legacy-', id::text),
                    warnings = COALESCE(warnings, ARRAY[]::text[])
                WHERE actor_user_id IS NULL
                   OR idempotency_key IS NULL
                   OR request_hash IS NULL
                   OR warnings IS NULL;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "actor_user_id",
                table: "sales",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "idempotency_key",
                table: "sales",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "request_hash",
                table: "sales",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string[]>(
                name: "warnings",
                table: "sales",
                type: "text[]",
                nullable: false,
                oldClrType: typeof(string[]),
                oldType: "text[]",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_shop_id_actor_user_id_idempotency_key",
                table: "sales",
                columns: IdempotencyIndexColumns,
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sales_shop_id_actor_user_id_idempotency_key",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "actor_user_id",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "request_hash",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "warnings",
                table: "sales");
        }
    }
}
