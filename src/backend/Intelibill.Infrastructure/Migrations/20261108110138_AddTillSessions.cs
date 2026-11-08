using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTillSessions : Migration
    {
        private static readonly string[] MembershipColumns = [ "shop_id", "user_id" ];
        private static readonly string[] TenantEntityColumns = [ "id", "shop_id" ];
        private static readonly string[] SessionEntityColumns = [ "id", "session_id", "shop_id" ];
        private static readonly string[] CloseSessionColumns = [ "close_id", "session_id", "shop_id" ];
        private static readonly string[] ShopCloseColumns = [ "shop_id", "close_id" ];
        private static readonly string[] ReviewerColumns = [ "shop_id", "reviewed_by" ];
        private static readonly string[] SessionShopColumns = [ "session_id", "shop_id" ];
        private static readonly string[] CloserColumns = [ "shop_id", "closed_by" ];
        private static readonly string[] ShopSessionColumns = [ "shop_id", "session_id" ];
        private static readonly string[] CaseShopColumns = [ "case_id", "shop_id" ];
        private static readonly string[] SubmissionShopColumns = [ "offline_submission_id", "shop_id" ];
        private static readonly string[] SaleShopColumns = [ "sale_id", "shop_id" ];
        private static readonly string[] CashierColumns = [ "shop_id", "cashier_id" ];
        private static readonly string[] ShopSubmissionColumns = [ "shop_id", "offline_submission_id" ];
        private static readonly string[] ShopSaleColumns = [ "shop_id", "sale_id" ];
        private static readonly string[] BankAccountShopColumns = [ "bank_account_id", "shop_id" ];
        private static readonly string[] ReversedMovementShopColumns = [ "reverses_movement_id", "shop_id" ];
        private static readonly string[] ActorColumns = [ "shop_id", "actor_user_id" ];
        private static readonly string[] MovementSequenceColumns = [ "shop_id", "session_id", "sequence" ];
        private static readonly string[] SourceLineColumns = [ "shop_id", "source_type", "source_id", "purpose", "line_number" ];
        private static readonly string[] SourceRecordSessionColumns = [ "source_record_id", "session_id", "shop_id" ];
        private static readonly string[] CashierDateColumns = [ "shop_id", "cashier_id", "business_date" ];
        private static readonly string[] OpenerColumns = [ "shop_id", "opened_by" ];
        private static readonly string[] ShopTillColumns = [ "shop_id", "till_id" ];
        private static readonly string[] TillShopColumns = [ "till_id", "shop_id" ];
        private static readonly string[] SourceColumns = [ "shop_id", "source_type", "source_id", "purpose" ];
        private static readonly string[] CreatorColumns = [ "shop_id", "created_by" ];
        private static readonly string[] TillNameColumns = [ "shop_id", "normalized_name" ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_shop_memberships_shop_id_user_id",
                table: "shop_memberships",
                columns: MembershipColumns);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_reconciliation_issues_id_shop_id",
                table: "reconciliation_issues",
                columns: TenantEntityColumns);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_offline_submissions_id_shop_id",
                table: "offline_submissions",
                columns: TenantEntityColumns);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_bank_accounts_id_shop_id",
                table: "bank_accounts",
                columns: TenantEntityColumns);

            migrationBuilder.CreateTable(
                name: "tills",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tills", x => x.id);
                    table.UniqueConstraint("ak_tills_id_shop_id", x => new { x.id, x.shop_id });
                    table.CheckConstraint("ck_tills_name", "length(btrim(name)) BETWEEN 1 AND 100 AND length(btrim(normalized_name)) BETWEEN 1 AND 100");
                    table.ForeignKey(
                        name: "fk_tills_shop_memberships_shop_id_created_by",
                        columns: x => new { x.shop_id, x.created_by },
                        principalTable: "shop_memberships",
                        principalColumns: MembershipColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tills_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "till_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    till_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cashier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    till_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    cashier_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    opened_by = table.Column<Guid>(type: "uuid", nullable: false),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    business_time_zone_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    opening_float = table.Column<decimal>(type: "numeric", nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    last_movement_sequence = table.Column<long>(type: "bigint", nullable: false),
                    source_count = table.Column<long>(type: "bigint", nullable: false),
                    // xmin is PostgreSQL's system row version, not a user-created column.
                    override_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_till_sessions", x => x.id);
                    table.UniqueConstraint("ak_till_sessions_id_shop_id", x => new { x.id, x.shop_id });
                    table.CheckConstraint("ck_till_sessions_amount", "opening_float >= 0 AND opening_float <= 9999999999999999.99 AND opening_float = round(opening_float,2)");
                    table.CheckConstraint("ck_till_sessions_labels", "length(btrim(till_name)) BETWEEN 1 AND 100 AND length(btrim(cashier_name)) BETWEEN 1 AND 200 AND length(btrim(business_time_zone_id)) BETWEEN 1 AND 64");
                    table.CheckConstraint("ck_till_sessions_override", "opened_by = cashier_id OR (override_reason IS NOT NULL AND length(btrim(override_reason)) BETWEEN 1 AND 500)");
                    table.CheckConstraint("ck_till_sessions_sequence", "last_movement_sequence >= 0 AND source_count >= 0");
                    table.CheckConstraint("ck_till_sessions_state", "state IN (1,2)");
                    table.ForeignKey(
                        name: "fk_till_sessions_shop_memberships_shop_id_cashier_id",
                        columns: x => new { x.shop_id, x.cashier_id },
                        principalTable: "shop_memberships",
                        principalColumns: MembershipColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_till_sessions_shop_memberships_shop_id_opened_by",
                        columns: x => new { x.shop_id, x.opened_by },
                        principalTable: "shop_memberships",
                        principalColumns: MembershipColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_till_sessions_tills_till_id_shop_id",
                        columns: x => new { x.till_id, x.shop_id },
                        principalTable: "tills",
                        principalColumns: TenantEntityColumns,
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "till_closes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opening_float = table.Column<decimal>(type: "numeric", nullable: false),
                    last_movement_sequence = table.Column<long>(type: "bigint", nullable: false),
                    totals = table.Column<string>(type: "jsonb", nullable: false),
                    expected_cash = table.Column<decimal>(type: "numeric", nullable: false),
                    counted_cash = table.Column<decimal>(type: "numeric", nullable: false),
                    variance = table.Column<decimal>(type: "numeric", nullable: false),
                    closed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    override_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_overdue = table.Column<bool>(type: "boolean", nullable: false),
                    awaiting_review = table.Column<bool>(type: "boolean", nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    till_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    cashier_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    cashier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_till_closes", x => x.id);
                    table.UniqueConstraint("ak_till_closes_id_session_id_shop_id", x => new { x.id, x.session_id, x.shop_id });
                    table.CheckConstraint("ck_till_closes_amount", "opening_float >= 0 AND counted_cash >= 0 AND opening_float <= 9999999999999999.99 AND counted_cash <= 9999999999999999.99 AND opening_float = round(opening_float,2) AND counted_cash = round(counted_cash,2) AND expected_cash = round(expected_cash,2) AND variance = counted_cash - expected_cash");
                    table.CheckConstraint("ck_till_closes_override", "closed_by = cashier_id OR (override_reason IS NOT NULL AND length(btrim(override_reason)) BETWEEN 1 AND 500)");
                    table.CheckConstraint("ck_till_closes_reason", "(variance = 0 AND NOT is_overdue) OR (reason IS NOT NULL AND length(btrim(reason)) BETWEEN 1 AND 500)");
                    table.CheckConstraint("ck_till_closes_sequence", "last_movement_sequence >= 0 AND (NOT awaiting_review OR variance <> 0)");
                    table.ForeignKey(
                        name: "fk_till_closes_shop_memberships_shop_id_closed_by",
                        columns: x => new { x.shop_id, x.closed_by },
                        principalTable: "shop_memberships",
                        principalColumns: MembershipColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_till_closes_till_sessions_session_id_shop_id",
                        columns: x => new { x.session_id, x.shop_id },
                        principalTable: "till_sessions",
                        principalColumns: TenantEntityColumns,
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "till_source_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_type = table.Column<int>(type: "integer", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    operation_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    override_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    store_credit_issued = table.Column<decimal>(type: "numeric", nullable: false),
                    store_credit_redeemed = table.Column<decimal>(type: "numeric", nullable: false),
                    receivable_change = table.Column<decimal>(type: "numeric", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_till_source_records", x => x.id);
                    table.UniqueConstraint("ak_till_source_records_id_session_id_shop_id", x => new { x.id, x.session_id, x.shop_id });
                    table.CheckConstraint("ck_till_sources_labels", "length(btrim(purpose)) BETWEEN 1 AND 100 AND length(btrim(operation_key)) BETWEEN 1 AND 128");
                    table.CheckConstraint("ck_till_sources_settlement", "abs(store_credit_issued) <= 9999999999999999.99 AND store_credit_issued = round(store_credit_issued,2) AND abs(store_credit_redeemed) <= 9999999999999999.99 AND store_credit_redeemed = round(store_credit_redeemed,2) AND abs(receivable_change) <= 9999999999999999.99 AND receivable_change = round(receivable_change,2)");
                    table.CheckConstraint("ck_till_sources_times", "recorded_at >= occurred_at");
                    table.CheckConstraint("ck_till_sources_type", "source_type BETWEEN 1 AND 11");
                    table.ForeignKey(
                        name: "fk_till_source_records_shop_memberships_shop_id_actor_user_id",
                        columns: x => new { x.shop_id, x.actor_user_id },
                        principalTable: "shop_memberships",
                        principalColumns: MembershipColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_till_source_records_till_sessions_session_id_shop_id",
                        columns: x => new { x.session_id, x.shop_id },
                        principalTable: "till_sessions",
                        principalColumns: TenantEntityColumns,
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "till_close_reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    close_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_till_close_reviews", x => x.id);
                    table.CheckConstraint("ck_till_reviews_reason", "length(btrim(reason)) BETWEEN 1 AND 500");
                    table.ForeignKey(
                        name: "fk_till_close_reviews_shop_memberships_shop_id_reviewed_by",
                        columns: x => new { x.shop_id, x.reviewed_by },
                        principalTable: "shop_memberships",
                        principalColumns: MembershipColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_till_close_reviews_till_closes_close_id_session_id_shop_id",
                        columns: x => new { x.close_id, x.session_id, x.shop_id },
                        principalTable: "till_closes",
                        principalColumns: SessionEntityColumns,
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "till_late_postings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    offline_submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    close_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cashier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tender_lines = table.Column<string>(type: "jsonb", nullable: false),
                    evidence_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    manager_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    included_in_count = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_till_late_postings", x => x.id);
                    table.CheckConstraint("ck_till_late_postings_evidence", "NOT included_in_count AND evidence_status IN ('Unknown','Recorded') AND length(btrim(manager_reason)) BETWEEN 1 AND 500 AND recorded_at >= occurred_at AND (close_id IS NULL OR session_id IS NOT NULL) AND jsonb_typeof(tender_lines) = 'array' AND jsonb_array_length(tender_lines) <= 1 AND (evidence_status <> 'Unknown' OR jsonb_array_length(tender_lines) = 0)");
                    table.ForeignKey(
                        name: "fk_till_late_postings_offline_submissions_offline_submission_i",
                        columns: x => new { x.offline_submission_id, x.shop_id },
                        principalTable: "offline_submissions",
                        principalColumns: TenantEntityColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_till_late_postings_reconciliation_issues_case_id_shop_id",
                        columns: x => new { x.case_id, x.shop_id },
                        principalTable: "reconciliation_issues",
                        principalColumns: TenantEntityColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_till_late_postings_sales_sale_id_shop_id",
                        columns: x => new { x.sale_id, x.shop_id },
                        principalTable: "sales",
                        principalColumns: TenantEntityColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_till_late_postings_shop_memberships_shop_id_cashier_id",
                        columns: x => new { x.shop_id, x.cashier_id },
                        principalTable: "shop_memberships",
                        principalColumns: MembershipColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_till_late_postings_till_closes_close_id_session_id_shop_id",
                        columns: x => new { x.close_id, x.session_id, x.shop_id },
                        principalTable: "till_closes",
                        principalColumns: SessionEntityColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_till_late_postings_till_sessions_session_id_shop_id",
                        columns: x => new { x.session_id, x.shop_id },
                        principalTable: "till_sessions",
                        principalColumns: TenantEntityColumns,
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "till_movements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    source_type = table.Column<int>(type: "integer", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    line_number = table.Column<int>(type: "integer", nullable: false),
                    method = table.Column<int>(type: "integer", nullable: false),
                    signed_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    cash_tendered = table.Column<decimal>(type: "numeric", nullable: true),
                    change = table.Column<decimal>(type: "numeric", nullable: true),
                    external_reference = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    bank_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    override_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    reverses_movement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_till_movements", x => x.id);
                    table.UniqueConstraint("ak_till_movements_id_shop_id", x => new { x.id, x.shop_id });
                    table.CheckConstraint("ck_till_movements_amount", "signed_amount <> 0 AND abs(signed_amount) <= 9999999999999999.99 AND signed_amount = round(signed_amount,2)");
                    table.CheckConstraint("ck_till_movements_cash", "(cash_tendered IS NULL AND change IS NULL) OR (method = 2 AND cash_tendered IS NOT NULL AND change IS NOT NULL AND change >= 0 AND cash_tendered >= abs(signed_amount) AND cash_tendered-change = abs(signed_amount) AND cash_tendered <= 9999999999999999.99 AND cash_tendered = round(cash_tendered,2) AND change = round(change,2))");
                    table.CheckConstraint("ck_till_movements_labels", "length(btrim(purpose)) BETWEEN 1 AND 100 AND length(btrim(operation_key)) BETWEEN 1 AND 128 AND recorded_at >= occurred_at");
                    table.CheckConstraint("ck_till_movements_type", "method BETWEEN 2 AND 6 AND source_type BETWEEN 1 AND 11 AND sequence > 0 AND line_number = 1");
                    table.ForeignKey(
                        name: "fk_till_movements_bank_accounts_bank_account_id_shop_id",
                        columns: x => new { x.bank_account_id, x.shop_id },
                        principalTable: "bank_accounts",
                        principalColumns: TenantEntityColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_till_movements_shop_memberships_shop_id_actor_user_id",
                        columns: x => new { x.shop_id, x.actor_user_id },
                        principalTable: "shop_memberships",
                        principalColumns: MembershipColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_till_movements_till_movements_reverses_movement_id_shop_id",
                        columns: x => new { x.reverses_movement_id, x.shop_id },
                        principalTable: "till_movements",
                        principalColumns: TenantEntityColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_till_movements_till_sessions_session_id_shop_id",
                        columns: x => new { x.session_id, x.shop_id },
                        principalTable: "till_sessions",
                        principalColumns: TenantEntityColumns,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_till_movements_till_source_records_source_record_id_session",
                        columns: x => new { x.source_record_id, x.session_id, x.shop_id },
                        principalTable: "till_source_records",
                        principalColumns: SessionEntityColumns,
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_till_close_reviews_close_id_session_id_shop_id",
                table: "till_close_reviews",
                columns: CloseSessionColumns);

            migrationBuilder.CreateIndex(
                name: "ix_till_close_reviews_shop_id_close_id",
                table: "till_close_reviews",
                columns: ShopCloseColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_till_close_reviews_shop_id_reviewed_by",
                table: "till_close_reviews",
                columns: ReviewerColumns);

            migrationBuilder.CreateIndex(
                name: "ix_till_closes_session_id_shop_id",
                table: "till_closes",
                columns: SessionShopColumns);

            migrationBuilder.CreateIndex(
                name: "ix_till_closes_shop_id_closed_by",
                table: "till_closes",
                columns: CloserColumns);

            migrationBuilder.CreateIndex(
                name: "ix_till_closes_shop_id_session_id",
                table: "till_closes",
                columns: ShopSessionColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_till_late_postings_case_id_shop_id",
                table: "till_late_postings",
                columns: CaseShopColumns);

            migrationBuilder.CreateIndex(
                name: "ix_till_late_postings_close_id_session_id_shop_id",
                table: "till_late_postings",
                columns: CloseSessionColumns);

            migrationBuilder.CreateIndex(
                name: "ix_till_late_postings_offline_submission_id_shop_id",
                table: "till_late_postings",
                columns: SubmissionShopColumns);

            migrationBuilder.CreateIndex(
                name: "ix_till_late_postings_sale_id_shop_id",
                table: "till_late_postings",
                columns: SaleShopColumns);

            migrationBuilder.CreateIndex(
                name: "ix_till_late_postings_session_id_shop_id",
                table: "till_late_postings",
                columns: SessionShopColumns);

            migrationBuilder.CreateIndex(
                name: "ix_till_late_postings_shop_id_cashier_id",
                table: "till_late_postings",
                columns: CashierColumns);

            migrationBuilder.CreateIndex(
                name: "ix_till_late_postings_shop_id_offline_submission_id",
                table: "till_late_postings",
                columns: ShopSubmissionColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_till_late_postings_shop_id_sale_id",
                table: "till_late_postings",
                columns: ShopSaleColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_till_movements_bank_account_id_shop_id",
                table: "till_movements",
                columns: BankAccountShopColumns);

            migrationBuilder.CreateIndex(
                name: "ix_till_movements_reverses_movement_id_shop_id",
                table: "till_movements",
                columns: ReversedMovementShopColumns);

            migrationBuilder.CreateIndex(
                name: "ix_till_movements_session_id_shop_id",
                table: "till_movements",
                columns: SessionShopColumns);

            migrationBuilder.CreateIndex(
                name: "ix_till_movements_shop_id_actor_user_id",
                table: "till_movements",
                columns: ActorColumns);

            migrationBuilder.CreateIndex(
                name: "ix_till_movements_shop_id_session_id_sequence",
                table: "till_movements",
                columns: MovementSequenceColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_till_movements_shop_id_source_type_source_id_purpose_line_n",
                table: "till_movements",
                columns: SourceLineColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_till_movements_source_record_id_session_id_shop_id",
                table: "till_movements",
                columns: SourceRecordSessionColumns);

            migrationBuilder.CreateIndex(
                name: "ix_till_sessions_shop_id_cashier_id_business_date",
                table: "till_sessions",
                columns: CashierDateColumns);

            migrationBuilder.CreateIndex(
                name: "ix_till_sessions_shop_id_opened_by",
                table: "till_sessions",
                columns: OpenerColumns);

            migrationBuilder.CreateIndex(
                name: "ix_till_sessions_shop_id_till_id",
                table: "till_sessions",
                columns: ShopTillColumns,
                unique: true,
                filter: "state = 1");

            migrationBuilder.CreateIndex(
                name: "ix_till_sessions_till_id_shop_id",
                table: "till_sessions",
                columns: TillShopColumns);

            migrationBuilder.CreateIndex(
                name: "ix_till_source_records_session_id_shop_id",
                table: "till_source_records",
                columns: SessionShopColumns);

            migrationBuilder.CreateIndex(
                name: "ix_till_source_records_shop_id_actor_user_id",
                table: "till_source_records",
                columns: ActorColumns);

            migrationBuilder.CreateIndex(
                name: "ix_till_source_records_shop_id_source_type_source_id_purpose",
                table: "till_source_records",
                columns: SourceColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tills_shop_id_created_by",
                table: "tills",
                columns: CreatorColumns);

            migrationBuilder.CreateIndex(
                name: "ix_tills_shop_id_normalized_name",
                table: "tills",
                columns: TillNameColumns,
                unique: true);
            // Tenant isolation and append-only accounting facts; no historical source/tender assignment.
            migrationBuilder.Sql("""
                ALTER TABLE tills ENABLE ROW LEVEL SECURITY;
                ALTER TABLE tills FORCE ROW LEVEL SECURITY;
                CREATE POLICY tills_shop_isolation ON tills
                    USING (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid)
                    WITH CHECK (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid);
                ALTER TABLE till_sessions ENABLE ROW LEVEL SECURITY;
                ALTER TABLE till_sessions FORCE ROW LEVEL SECURITY;
                CREATE POLICY till_sessions_shop_isolation ON till_sessions
                    USING (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid)
                    WITH CHECK (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid);
                ALTER TABLE till_source_records ENABLE ROW LEVEL SECURITY;
                ALTER TABLE till_source_records FORCE ROW LEVEL SECURITY;
                CREATE POLICY till_source_records_shop_isolation ON till_source_records
                    USING (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid)
                    WITH CHECK (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid);
                ALTER TABLE till_movements ENABLE ROW LEVEL SECURITY;
                ALTER TABLE till_movements FORCE ROW LEVEL SECURITY;
                CREATE POLICY till_movements_shop_isolation ON till_movements
                    USING (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid)
                    WITH CHECK (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid);
                ALTER TABLE till_closes ENABLE ROW LEVEL SECURITY;
                ALTER TABLE till_closes FORCE ROW LEVEL SECURITY;
                CREATE POLICY till_closes_shop_isolation ON till_closes
                    USING (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid)
                    WITH CHECK (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid);
                ALTER TABLE till_close_reviews ENABLE ROW LEVEL SECURITY;
                ALTER TABLE till_close_reviews FORCE ROW LEVEL SECURITY;
                CREATE POLICY till_close_reviews_shop_isolation ON till_close_reviews
                    USING (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid)
                    WITH CHECK (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid);
                ALTER TABLE till_late_postings ENABLE ROW LEVEL SECURITY;
                ALTER TABLE till_late_postings FORCE ROW LEVEL SECURITY;
                CREATE POLICY till_late_postings_shop_isolation ON till_late_postings
                    USING (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid)
                    WITH CHECK (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid);
                CREATE FUNCTION till_reject_fact_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Till facts are immutable' USING ERRCODE = '23514';
                END;
                $$;
                CREATE TRIGGER till_source_records_immutable BEFORE UPDATE OR DELETE ON till_source_records FOR EACH ROW EXECUTE FUNCTION till_reject_fact_mutation();
                CREATE TRIGGER till_movements_immutable BEFORE UPDATE OR DELETE ON till_movements FOR EACH ROW EXECUTE FUNCTION till_reject_fact_mutation();
                CREATE TRIGGER till_closes_immutable BEFORE UPDATE OR DELETE ON till_closes FOR EACH ROW EXECUTE FUNCTION till_reject_fact_mutation();
                CREATE TRIGGER till_close_reviews_immutable BEFORE UPDATE OR DELETE ON till_close_reviews FOR EACH ROW EXECUTE FUNCTION till_reject_fact_mutation();
                CREATE TRIGGER till_late_postings_immutable BEFORE UPDATE OR DELETE ON till_late_postings FOR EACH ROW EXECUTE FUNCTION till_reject_fact_mutation();
                CREATE FUNCTION till_guard_closed_session() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' OR OLD.state = 2 THEN
                        RAISE EXCEPTION 'A closed till session is immutable' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER till_session_closed_immutable BEFORE UPDATE OR DELETE ON till_sessions
                    FOR EACH ROW EXECUTE FUNCTION till_guard_closed_session();
                CREATE FUNCTION till_guard_source_session() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE session_state integer;
                BEGIN
                    SELECT state INTO session_state FROM till_sessions
                        WHERE shop_id = NEW.shop_id AND id = NEW.session_id FOR NO KEY UPDATE;
                    IF session_state IS DISTINCT FROM 1 THEN
                        RAISE EXCEPTION 'An open till session is required' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER till_sources_open_session BEFORE INSERT ON till_source_records
                    FOR EACH ROW EXECUTE FUNCTION till_guard_source_session();
                CREATE TRIGGER till_movements_open_session BEFORE INSERT ON till_movements
                    FOR EACH ROW EXECUTE FUNCTION till_guard_source_session();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM till_sessions) OR EXISTS (SELECT 1 FROM till_late_postings) THEN
                        RAISE EXCEPTION 'Till facts must be preserved; disable the rollout instead of dropping its schema';
                    END IF;
                END $$;
                DROP FUNCTION till_guard_source_session() CASCADE;
                DROP FUNCTION till_guard_closed_session() CASCADE;
                DROP FUNCTION till_reject_fact_mutation() CASCADE;
                """);

            migrationBuilder.DropTable(
                name: "till_close_reviews");

            migrationBuilder.DropTable(
                name: "till_late_postings");

            migrationBuilder.DropTable(
                name: "till_movements");

            migrationBuilder.DropTable(
                name: "till_closes");

            migrationBuilder.DropTable(
                name: "till_source_records");

            migrationBuilder.DropTable(
                name: "till_sessions");

            migrationBuilder.DropTable(
                name: "tills");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_shop_memberships_shop_id_user_id",
                table: "shop_memberships");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_reconciliation_issues_id_shop_id",
                table: "reconciliation_issues");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_offline_submissions_id_shop_id",
                table: "offline_submissions");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_bank_accounts_id_shop_id",
                table: "bank_accounts");
        }
    }
}
