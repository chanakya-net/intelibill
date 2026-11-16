using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShopLicensingState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "license_grant_evidence",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    affiliation_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_revision = table.Column<long>(type: "bigint", nullable: true),
                    shop_revision = table.Column<long>(type: "bigint", nullable: true),
                    account_upstream_verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    shop_upstream_verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    key_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    public_key = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    assertion = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    payload = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_license_grant_evidence", x => x.event_id);
                });

            migrationBuilder.CreateTable(
                name: "account_license_restrictions",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    upstream_verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    restriction = table.Column<string>(type: "text", nullable: false),
                    released_revision = table.Column<long>(type: "bigint", nullable: true),
                    evidence_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_license_restrictions", x => x.account_id);
                    table.ForeignKey(
                        name: "fk_account_license_restrictions_license_grant_evidence_evidenc",
                        column: x => x.evidence_id,
                        principalTable: "license_grant_evidence",
                        principalColumn: "event_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shop_license_states",
                columns: table => new
                {
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    affiliation_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enforcement_activated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    upstream_verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    term_id = table.Column<Guid>(type: "uuid", nullable: true),
                    basis = table.Column<string>(type: "text", nullable: true),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    access_ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_boundary = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    shop_capacity = table.Column<int>(type: "integer", nullable: true),
                    member_allowance = table.Column<int>(type: "integer", nullable: true),
                    evidence_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shop_license_states", x => x.shop_id);
                    table.ForeignKey(
                        name: "fk_shop_license_states_account_license_restrictions_account_id",
                        column: x => x.account_id,
                        principalTable: "account_license_restrictions",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shop_license_states_license_grant_evidence_evidence_id",
                        column: x => x.evidence_id,
                        principalTable: "license_grant_evidence",
                        principalColumn: "event_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shop_license_states_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_account_license_restrictions_evidence_id",
                table: "account_license_restrictions",
                column: "evidence_id");

            migrationBuilder.CreateIndex(
                name: "ix_license_grant_evidence_shop_id",
                table: "license_grant_evidence",
                column: "shop_id");

            migrationBuilder.CreateIndex(
                name: "ix_shop_license_states_account_id",
                table: "shop_license_states",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_shop_license_states_evidence_id",
                table: "shop_license_states",
                column: "evidence_id");
            AddDelivery(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            DropDelivery(migrationBuilder);
            migrationBuilder.DropTable(
                name: "shop_license_states");

            migrationBuilder.DropTable(
                name: "account_license_restrictions");

            migrationBuilder.DropTable(
                name: "license_grant_evidence");
        }
    }
}
