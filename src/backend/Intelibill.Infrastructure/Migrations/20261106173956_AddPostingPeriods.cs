using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPostingPeriods : Migration
    {
        private static readonly string[] EventColumns = [ "shop_id", "posting_period_id", "occurred_at" ];
        private static readonly string[] PeriodColumns = [ "shop_id", "start_date" ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "posting_periods",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    // xmin is a PostgreSQL system column on every table; it is the row version and needs no DDL.
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_posting_periods", x => x.id);
                    table.CheckConstraint("ck_posting_periods_range", "end_date >= start_date");
                    table.ForeignKey(
                        name: "fk_posting_periods_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "posting_period_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    posting_period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_posting_period_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_posting_period_events_posting_periods_posting_period_id",
                        column: x => x.posting_period_id,
                        principalTable: "posting_periods",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_posting_period_events_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_posting_period_events_posting_period_id",
                table: "posting_period_events",
                column: "posting_period_id");

            migrationBuilder.CreateIndex(
                name: "ix_posting_period_events_shop_id_posting_period_id_occurred_at",
                table: "posting_period_events",
                columns: EventColumns);

            migrationBuilder.CreateIndex(
                name: "ix_posting_periods_shop_id_start_date",
                table: "posting_periods",
                columns: PeriodColumns,
                unique: true);

            // Tenant data. Same predicate as CompleteTenantRowLevelSecurity.
            migrationBuilder.Sql("""
                ALTER TABLE posting_periods ENABLE ROW LEVEL SECURITY;
                ALTER TABLE posting_periods FORCE ROW LEVEL SECURITY;
                CREATE POLICY posting_periods_shop_isolation ON posting_periods
                    USING (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid)
                    WITH CHECK (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid);

                ALTER TABLE posting_period_events ENABLE ROW LEVEL SECURITY;
                ALTER TABLE posting_period_events FORCE ROW LEVEL SECURITY;
                CREATE POLICY posting_period_events_shop_isolation ON posting_period_events
                    USING (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid)
                    WITH CHECK (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP POLICY IF EXISTS posting_period_events_shop_isolation ON posting_period_events;
                DROP POLICY IF EXISTS posting_periods_shop_isolation ON posting_periods;
                """);

            migrationBuilder.DropTable(
                name: "posting_period_events");

            migrationBuilder.DropTable(
                name: "posting_periods");
        }
    }
}
