using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBatchDispositionEvidence : Migration
    {
        private static readonly string[] Columns1 = new[] { "id", "inventory_batch_id", "item_id", "shop_id" };
        private static readonly string[] Columns2 = new[] { "id", "item_id", "shop_id" };
        private static readonly string[] Columns3 = new[] { "quarantine_change_id", "shop_id" };
        private static readonly string[] Columns4 = new[] { "inventory_batch_id", "item_id", "shop_id" };
        private static readonly string[] Columns5 = new[] { "root_change_id", "inventory_batch_id", "item_id", "shop_id" };
        private static readonly string[] Columns6 = new[] { "shop_id", "inventory_batch_id", "recorded_at" };
        private static readonly string[] Columns7 = new[] { "shop_id", "source_kind", "source_id", "source_line_id" };
        private static readonly string[] Columns8 = new[] { "id", "shop_id" };
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "disposition_reason_code",
                table: "sale_return_items",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "quarantine_change_id",
                table: "sale_return_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "quarantined_quantity",
                table: "sale_return_items",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "disposition_reason_code",
                table: "sale_cancellation_lines",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "quarantine_change_id",
                table: "sale_cancellation_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "quarantined_quantity",
                table: "sale_cancellation_lines",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "disposition_change_id",
                table: "inventory_adjustments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "batch_disposition_changes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inventory_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    root_change_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    source_kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stock_movement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    inventory_adjustment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    manufacturing_date = table.Column<DateOnly>(type: "date", nullable: true),
                    reason_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    policy_json = table.Column<string>(type: "jsonb", nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_batch_disposition_changes", x => x.id);
                    table.UniqueConstraint("ak_batch_disposition_changes_id_inventory_batch_id_item_id_sho", x => new { x.id, x.inventory_batch_id, x.item_id, x.shop_id });
                    table.UniqueConstraint("ak_batch_disposition_changes_id_shop_id", x => new { x.id, x.shop_id });
                    table.CheckConstraint("ck_batch_disposition_action", "(root_change_id IS NULL AND action = 'Quarantine' AND stock_movement_id IS NOT NULL AND inventory_adjustment_id IS NULL) OR (root_change_id IS NOT NULL AND ((action = 'Release' AND stock_movement_id IS NULL AND inventory_adjustment_id IS NULL) OR (action = 'Dispose' AND stock_movement_id IS NOT NULL AND inventory_adjustment_id IS NOT NULL) OR (action = 'Reverse' AND stock_movement_id IS NOT NULL AND inventory_adjustment_id IS NULL)))");
                    table.CheckConstraint("ck_batch_disposition_cause", "reason_code IN ('Expiry.Expired','Expiry.RequiredDateMissing')");
                    table.CheckConstraint("ck_batch_disposition_quantity", "quantity > 0");
                    table.CheckConstraint("ck_batch_disposition_reason", "length(btrim(reason)) BETWEEN 1 AND 500");
                    table.CheckConstraint("ck_batch_disposition_source", "source_kind IN ('SaleReturn','SaleCancellation') AND source_id <> '00000000-0000-0000-0000-000000000000' AND source_line_id <> '00000000-0000-0000-0000-000000000000'");
                    table.ForeignKey(
                        name: "fk_batch_disposition_changes_batch_disposition_changes_root_ch",
                        columns: x => new { x.root_change_id, x.inventory_batch_id, x.item_id, x.shop_id },
                        principalTable: "batch_disposition_changes",
                        principalColumns: Columns1,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_batch_disposition_changes_inventory_adjustments_inventory_a",
                        column: x => x.inventory_adjustment_id,
                        principalTable: "inventory_adjustments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_batch_disposition_changes_inventory_batches_inventory_batch",
                        columns: x => new { x.inventory_batch_id, x.item_id, x.shop_id },
                        principalTable: "inventory_batches",
                        principalColumns: Columns2,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_batch_disposition_changes_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_batch_disposition_changes_stock_transactions_stock_movement",
                        column: x => x.stock_movement_id,
                        principalTable: "stock_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sale_return_items_quarantine_change_id_shop_id",
                table: "sale_return_items",
                columns: Columns3);

            migrationBuilder.CreateIndex(
                name: "ix_sale_cancellation_lines_quarantine_change_id_shop_id",
                table: "sale_cancellation_lines",
                columns: Columns3);

            migrationBuilder.CreateIndex(
                name: "ix_batch_disposition_changes_inventory_adjustment_id",
                table: "batch_disposition_changes",
                column: "inventory_adjustment_id");

            migrationBuilder.CreateIndex(
                name: "ix_batch_disposition_changes_inventory_batch_id_item_id_shop_id",
                table: "batch_disposition_changes",
                columns: Columns4);

            migrationBuilder.CreateIndex(
                name: "ix_batch_disposition_changes_root_change_id_inventory_batch_id",
                table: "batch_disposition_changes",
                columns: Columns5);

            migrationBuilder.CreateIndex(
                name: "ix_batch_disposition_changes_shop_id_inventory_batch_id_record",
                table: "batch_disposition_changes",
                columns: Columns6);

            migrationBuilder.CreateIndex(
                name: "ix_batch_disposition_changes_shop_id_source_kind_source_id_sou",
                table: "batch_disposition_changes",
                columns: Columns7,
                unique: true,
                filter: "root_change_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_batch_disposition_changes_stock_movement_id",
                table: "batch_disposition_changes",
                column: "stock_movement_id");

            migrationBuilder.AddForeignKey(
                name: "fk_sale_cancellation_lines_batch_disposition_changes_quarantin",
                table: "sale_cancellation_lines",
                columns: Columns3,
                principalTable: "batch_disposition_changes",
                principalColumns: Columns8,
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_sale_return_items_batch_disposition_changes_quarantine_chan",
                table: "sale_return_items",
                columns: Columns3,
                principalTable: "batch_disposition_changes",
                principalColumns: Columns8,
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
                ALTER TABLE batch_disposition_changes ENABLE ROW LEVEL SECURITY;
                ALTER TABLE batch_disposition_changes FORCE ROW LEVEL SECURITY;
                CREATE POLICY batch_disposition_changes_shop_isolation ON batch_disposition_changes
                    USING (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid)
                    WITH CHECK (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid);
                CREATE TRIGGER batch_disposition_changes_immutable BEFORE UPDATE OR DELETE ON batch_disposition_changes
                    FOR EACH ROW EXECUTE FUNCTION expiry_reject_revision_mutation();
                CREATE FUNCTION expiry_validate_disposition() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE root batch_disposition_changes; balance numeric; aggregate_hold numeric; physical_hold numeric; valid_source boolean;
                BEGIN
                    IF NEW.root_change_id IS NULL THEN
                        root := NEW;
                    ELSE
                        SELECT * INTO root FROM batch_disposition_changes WHERE id = NEW.root_change_id AND shop_id = NEW.shop_id;
                        IF root.root_change_id IS NOT NULL OR root.action <> 'Quarantine' OR root.source_kind <> NEW.source_kind
                            OR root.source_id <> NEW.source_id OR root.source_line_id <> NEW.source_line_id
                            OR root.reason_code <> NEW.reason_code OR root.expiry_date IS DISTINCT FROM NEW.expiry_date
                            OR root.manufacturing_date IS DISTINCT FROM NEW.manufacturing_date OR root.policy_json <> NEW.policy_json THEN
                            RAISE EXCEPTION 'Disposition must retain its original root' USING ERRCODE = '23514';
                        END IF;
                    END IF;
                    SELECT root.quantity - COALESCE(sum(quantity),0) INTO balance FROM batch_disposition_changes WHERE root_change_id = root.id AND shop_id = root.shop_id;
                    IF balance < 0 THEN RAISE EXCEPTION 'Disposition root is exhausted' USING ERRCODE = '23514'; END IF;
                    SELECT COALESCE(sum(CASE WHEN root_change_id IS NULL THEN quantity ELSE -quantity END),0) INTO aggregate_hold
                        FROM batch_disposition_changes WHERE shop_id = NEW.shop_id AND inventory_batch_id = NEW.inventory_batch_id;
                    SELECT quarantined_quantity INTO physical_hold FROM inventory_batches WHERE id = NEW.inventory_batch_id AND shop_id = NEW.shop_id;
                    IF physical_hold IS DISTINCT FROM aggregate_hold THEN RAISE EXCEPTION 'Quarantine must equal retained root balances' USING ERRCODE = '23514'; END IF;
                    IF root.source_kind = 'SaleReturn' THEN
                        SELECT EXISTS (SELECT 1 FROM sale_return_items ri JOIN sale_items si ON si.id = ri.sale_item_id
                            WHERE ri.id = root.source_line_id AND ri.sale_return_id = root.source_id AND ri.shop_id = root.shop_id
                                AND si.inventory_batch_id = root.inventory_batch_id AND ri.quarantine_change_id = root.id
                                AND ri.quarantined_quantity = root.quantity AND ri.disposition_reason_code = root.reason_code) INTO valid_source;
                    ELSE
                        SELECT EXISTS (SELECT 1 FROM sale_cancellation_lines cl WHERE cl.id = root.source_line_id AND cl.sale_cancellation_id = root.source_id
                            AND cl.shop_id = root.shop_id AND cl.inventory_batch_id = root.inventory_batch_id AND cl.quarantine_change_id = root.id
                            AND cl.quarantined_quantity = root.quantity AND cl.disposition_reason_code = root.reason_code) INTO valid_source;
                    END IF;
                    IF NOT valid_source THEN RAISE EXCEPTION 'Disposition source must name its exact recovered goods' USING ERRCODE = '23514'; END IF;
                    IF NEW.action = 'Dispose' AND NOT EXISTS (SELECT 1 FROM inventory_adjustments a WHERE a.id = NEW.inventory_adjustment_id
                        AND a.shop_id = NEW.shop_id AND a.inventory_batch_id = NEW.inventory_batch_id AND a.disposition_change_id = NEW.id
                        AND a.quantity = NEW.quantity AND NOT a.is_voided) THEN
                        RAISE EXCEPTION 'Disposal must have its original independent loss adjustment' USING ERRCODE = '23514';
                    END IF;
                    IF NEW.stock_movement_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM stock_transactions m WHERE m.id = NEW.stock_movement_id
                        AND m.shop_id = NEW.shop_id AND m.inventory_batch_id = NEW.inventory_batch_id
                        AND m.quantity = CASE WHEN NEW.action = 'Quarantine' THEN NEW.quantity ELSE -NEW.quantity END) THEN
                        RAISE EXCEPTION 'Disposition movement must retain exact physical effect' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END; $$;
                CREATE CONSTRAINT TRIGGER batch_disposition_conservation AFTER INSERT ON batch_disposition_changes
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION expiry_validate_disposition();
                CREATE FUNCTION expiry_validate_batch_quarantine() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE retained numeric;
                BEGIN
                    SELECT COALESCE(sum(CASE WHEN root_change_id IS NULL THEN quantity ELSE -quantity END),0) INTO retained
                        FROM batch_disposition_changes WHERE shop_id = NEW.shop_id AND inventory_batch_id = NEW.id;
                    IF (SELECT quarantined_quantity FROM inventory_batches WHERE id = NEW.id AND shop_id = NEW.shop_id) IS DISTINCT FROM retained THEN
                        RAISE EXCEPTION 'Quarantine must equal retained root balances' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END; $$;
                CREATE CONSTRAINT TRIGGER inventory_batches_quarantine_conservation AFTER INSERT OR UPDATE ON inventory_batches
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION expiry_validate_batch_quarantine();
                CREATE FUNCTION expiry_validate_source_disposition() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE expected_source_id uuid; expected_source_kind text;
                BEGIN
                    IF NEW.quarantine_change_id IS NULL THEN
                        IF NEW.quarantined_quantity <> 0 OR NEW.disposition_reason_code IS NOT NULL THEN
                            RAISE EXCEPTION 'Held source goods need their exact root' USING ERRCODE = '23514';
                        END IF;
                        RETURN NEW;
                    END IF;
                    IF TG_TABLE_NAME = 'sale_return_items' THEN
                        expected_source_id := NEW.sale_return_id; expected_source_kind := 'SaleReturn';
                    ELSE
                        expected_source_id := NEW.sale_cancellation_id; expected_source_kind := 'SaleCancellation';
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM batch_disposition_changes root WHERE root.id = NEW.quarantine_change_id
                        AND root.shop_id = NEW.shop_id AND root.source_id = expected_source_id AND root.source_kind = expected_source_kind
                        AND root.source_line_id = NEW.id AND root.root_change_id IS NULL AND root.action = 'Quarantine'
                        AND root.quantity = NEW.quarantined_quantity AND root.quantity <= NEW.quantity AND root.reason_code = NEW.disposition_reason_code) THEN
                        RAISE EXCEPTION 'Held source goods must retain their own exact quarantine root' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END; $$;
                CREATE CONSTRAINT TRIGGER sale_return_items_disposition_source AFTER INSERT ON sale_return_items
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION expiry_validate_source_disposition();
                CREATE CONSTRAINT TRIGGER sale_cancellation_lines_disposition_source AFTER INSERT ON sale_cancellation_lines
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION expiry_validate_source_disposition();
                CREATE FUNCTION expiry_protect_source_disposition() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        IF OLD.quarantine_change_id IS NOT NULL THEN RAISE EXCEPTION 'Original disposition is immutable' USING ERRCODE = '23514'; END IF;
                        RETURN OLD;
                    END IF;
                    IF OLD.quarantine_change_id IS DISTINCT FROM NEW.quarantine_change_id OR OLD.quarantined_quantity IS DISTINCT FROM NEW.quarantined_quantity
                        OR OLD.disposition_reason_code IS DISTINCT FROM NEW.disposition_reason_code THEN
                        RAISE EXCEPTION 'Original disposition is immutable' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END; $$;
                CREATE TRIGGER sale_return_items_disposition_immutable BEFORE UPDATE OR DELETE ON sale_return_items
                    FOR EACH ROW EXECUTE FUNCTION expiry_protect_source_disposition();
                CREATE TRIGGER sale_cancellation_lines_disposition_immutable BEFORE UPDATE OR DELETE ON sale_cancellation_lines
                    FOR EACH ROW EXECUTE FUNCTION expiry_protect_source_disposition();
                CREATE FUNCTION expiry_protect_disposal_adjustment() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        IF OLD.disposition_change_id IS NOT NULL THEN RAISE EXCEPTION 'Disposal is immutable' USING ERRCODE = '23514'; END IF;
                        RETURN OLD;
                    END IF;
                    IF OLD.disposition_change_id IS NOT NULL AND (NEW IS DISTINCT FROM OLD) THEN
                        RAISE EXCEPTION 'Disposal is immutable' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END; $$;
                CREATE TRIGGER inventory_adjustments_disposal_immutable BEFORE UPDATE OR DELETE ON inventory_adjustments
                    FOR EACH ROW EXECUTE FUNCTION expiry_protect_disposal_adjustment();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Retain disposition journal, physical quarantine and source evidence; use a compatible forward migration.");
    }
}
