using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
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
                    address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    city = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    state = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    pincode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    contact_person = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    mobile_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    gst_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shops", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    phone_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    password_hash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    language = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "en-IN"),
                    is_email_verified = table.Column<bool>(type: "boolean", nullable: false),
                    is_login_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bank_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    account_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    account_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    ifsc_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    account_holder_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_accounts", x => x.id);
                    table.ForeignKey(
                        name: "fk_bank_accounts_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "customers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    phone_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customers", x => x.id);
                    table.ForeignKey(
                        name: "fk_customers_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "expense_categories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expense_categories", x => x.id);
                    table.ForeignKey(
                        name: "fk_expense_categories_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    uom = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    barcode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_items", x => x.id);
                    table.UniqueConstraint("ak_items_id_shop_id", x => new { x.id, x.shop_id });
                    table.ForeignKey(
                        name: "fk_items_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "suppliers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    contact_person_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    contact_person_phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    city = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    state = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    pin = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    is_system = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_preferred = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_suppliers", x => x.id);
                    table.ForeignKey(
                        name: "fk_suppliers_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "password_reset_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_used = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_password_reset_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_password_reset_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_revoked = table.Column<bool>(type: "boolean", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_refresh_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
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

            migrationBuilder.CreateTable(
                name: "user_external_logins",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<int>(type: "integer", nullable: false),
                    provider_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    provider_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_external_logins", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_external_logins_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    customer_name = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: true),
                    customer_phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    payment_method = table.Column<int>(type: "integer", nullable: false),
                    sold_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    paid_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    due_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_tax_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales", x => x.id);
                    table.ForeignKey(
                        name: "fk_sales_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_sales_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    reorder_level = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    max_level = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    last_updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory", x => x.id);
                    table.CheckConstraint("ck_inventory_max_non_negative", "max_level >= 0");
                    table.CheckConstraint("ck_inventory_quantity_non_negative", "quantity >= 0");
                    table.CheckConstraint("ck_inventory_reorder_lte_max", "reorder_level <= max_level");
                    table.CheckConstraint("ck_inventory_reorder_non_negative", "reorder_level >= 0");
                    table.ForeignKey(
                        name: "fk_inventory_items_item_id_shop_id",
                        columns: x => new { x.item_id, x.shop_id },
                        principalTable: "items",
                        principalColumns: new[] { "id", "shop_id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_inventory_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "inventory_batches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_number = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    original_quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    cost_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    mrp = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    sales_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tax_rate_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    tax_included = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    manufacturing_date = table.Column<DateOnly>(type: "date", nullable: true),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_voided = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_batches", x => x.id);
                    table.UniqueConstraint("ak_inventory_batches_id_item_id_shop_id", x => new { x.id, x.item_id, x.shop_id });
                    table.CheckConstraint("ck_inventory_batches_quantity_non_negative", "quantity >= 0");
                    table.CheckConstraint("ck_inventory_batches_sales_lte_mrp", "sales_price <= mrp");
                    table.CheckConstraint("ck_inventory_batches_tax_rate_range", "tax_rate_percent >= 0 AND tax_rate_percent <= 100");
                    table.ForeignKey(
                        name: "fk_inventory_batches_items_item_id_shop_id",
                        columns: x => new { x.item_id, x.shop_id },
                        principalTable: "items",
                        principalColumns: new[] { "id", "shop_id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_inventory_batches_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_inventory_batches_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "customer_ledger_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entry_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    entry_date = table.Column<DateOnly>(type: "date", nullable: false),
                    notes = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_ledger_entries", x => x.id);
                    table.CheckConstraint("ck_customer_ledger_entries_amount_positive", "amount > 0");
                    table.CheckConstraint("ck_customer_ledger_entries_sale_by_type", "((entry_type = 'SALE_DUE' AND sale_id IS NOT NULL) OR (entry_type = 'PAYMENT_RECEIVED' AND sale_id IS NULL))");
                    table.ForeignKey(
                        name: "fk_customer_ledger_entries_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_customer_ledger_entries_sales_sale_id",
                        column: x => x.sale_id,
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_customer_ledger_entries_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sale_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inventory_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    cost_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    sales_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    mrp = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tax_rate_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    is_price_including_tax = table.Column<bool>(type: "boolean", nullable: false),
                    has_price_mismatch = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sale_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_sale_items_inventory_batches_inventory_batch_id",
                        column: x => x.inventory_batch_id,
                        principalTable: "inventory_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sale_items_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sale_items_sales_sale_id",
                        column: x => x.sale_id,
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_sale_items_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stock_transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inventory_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transaction_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    reference_number = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    performed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    performed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_transactions", x => x.id);
                    table.CheckConstraint("ck_stock_transactions_quantity_non_zero", "quantity <> 0");
                    table.CheckConstraint("ck_stock_transactions_quantity_sign_by_type", "((transaction_type IN ('IN', 'RET') AND quantity > 0) OR (transaction_type IN ('OUT', 'REJ', 'DMG', 'STOL') AND quantity < 0) OR (transaction_type = 'ADJ'))");
                    table.ForeignKey(
                        name: "fk_stock_transactions_inventory_batches_inventory_batch_id_ite",
                        columns: x => new { x.inventory_batch_id, x.item_id, x.shop_id },
                        principalTable: "inventory_batches",
                        principalColumns: new[] { "id", "item_id", "shop_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_transactions_items_item_id_shop_id",
                        columns: x => new { x.item_id, x.shop_id },
                        principalTable: "items",
                        principalColumns: new[] { "id", "shop_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_transactions_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "supplier_ledger_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entry_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    entry_date = table.Column<DateOnly>(type: "date", nullable: false),
                    notes = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supplier_ledger_entries", x => x.id);
                    table.CheckConstraint("ck_supplier_ledger_entries_amount_non_zero", "amount <> 0");
                    table.CheckConstraint("ck_supplier_ledger_entries_batch_by_type", "((entry_type = 'GOODS_RECEIVED' AND batch_id IS NOT NULL) OR (entry_type IN ('PAYMENT_MADE', 'RECORD_ADJUSTED') AND batch_id IS NULL))");
                    table.ForeignKey(
                        name: "fk_supplier_ledger_entries_inventory_batches_batch_id",
                        column: x => x.batch_id,
                        principalTable: "inventory_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_supplier_ledger_entries_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_supplier_ledger_entries_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "expenses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    paid_to = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    expense_date = table.Column<DateOnly>(type: "date", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_voided = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    original_expense_id = table.Column<Guid>(type: "uuid", nullable: true),
                    supplier_ledger_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expenses", x => x.id);
                    table.CheckConstraint("ck_expenses_amount_positive", "amount > 0");
                    table.ForeignKey(
                        name: "fk_expenses_expense_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "expense_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_expenses_expenses_original_expense_id",
                        column: x => x.original_expense_id,
                        principalTable: "expenses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_expenses_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_expenses_supplier_ledger_entries_supplier_ledger_entry_id",
                        column: x => x.supplier_ledger_entry_id,
                        principalTable: "supplier_ledger_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bank_accounts_shop_id",
                table: "bank_accounts",
                column: "shop_id");

            migrationBuilder.CreateIndex(
                name: "ix_customer_ledger_entries_customer_id",
                table: "customer_ledger_entries",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_customer_ledger_entries_sale_id",
                table: "customer_ledger_entries",
                column: "sale_id");

            migrationBuilder.CreateIndex(
                name: "ix_customer_ledger_entries_shop_id_customer_id_entry_date",
                table: "customer_ledger_entries",
                columns: new[] { "shop_id", "customer_id", "entry_date" });

            migrationBuilder.CreateIndex(
                name: "ix_customer_ledger_entries_shop_id_sale_id_entry_type",
                table: "customer_ledger_entries",
                columns: new[] { "shop_id", "sale_id", "entry_type" });

            migrationBuilder.CreateIndex(
                name: "ix_customers_shop_id",
                table: "customers",
                column: "shop_id");

            migrationBuilder.CreateIndex(
                name: "ix_customers_shop_id_is_active",
                table: "customers",
                columns: new[] { "shop_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_customers_shop_id_phone_number",
                table: "customers",
                columns: new[] { "shop_id", "phone_number" });

            migrationBuilder.CreateIndex(
                name: "ix_expense_categories_shop_id_name",
                table: "expense_categories",
                columns: new[] { "shop_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_expenses_category_id",
                table: "expenses",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_expenses_original_expense_id",
                table: "expenses",
                column: "original_expense_id");

            migrationBuilder.CreateIndex(
                name: "ix_expenses_shop_id_expense_date",
                table: "expenses",
                columns: new[] { "shop_id", "expense_date" });

            migrationBuilder.CreateIndex(
                name: "ix_expenses_shop_id_is_voided",
                table: "expenses",
                columns: new[] { "shop_id", "is_voided" });

            migrationBuilder.CreateIndex(
                name: "ix_expenses_shop_id_paid_to",
                table: "expenses",
                columns: new[] { "shop_id", "paid_to" });

            migrationBuilder.CreateIndex(
                name: "ix_expenses_supplier_ledger_entry_id",
                table: "expenses",
                column: "supplier_ledger_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_item_id_shop_id",
                table: "inventory",
                columns: new[] { "item_id", "shop_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inventory_shop_id_item_id",
                table: "inventory",
                columns: new[] { "shop_id", "item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inventory_batches_id_item_id_shop_id",
                table: "inventory_batches",
                columns: new[] { "id", "item_id", "shop_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inventory_batches_item_id_shop_id",
                table: "inventory_batches",
                columns: new[] { "item_id", "shop_id" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_batches_shop_id_expiry_date",
                table: "inventory_batches",
                columns: new[] { "shop_id", "expiry_date" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_batches_shop_id_item_id_batch_number",
                table: "inventory_batches",
                columns: new[] { "shop_id", "item_id", "batch_number" },
                unique: true,
                filter: "is_voided = false");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_batches_shop_id_supplier_id",
                table: "inventory_batches",
                columns: new[] { "shop_id", "supplier_id" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_batches_supplier_id",
                table: "inventory_batches",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "ix_items_id_shop_id",
                table: "items",
                columns: new[] { "id", "shop_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_items_shop_id_barcode",
                table: "items",
                columns: new[] { "shop_id", "barcode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_items_shop_id_is_active",
                table: "items",
                columns: new[] { "shop_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_items_shop_id_name",
                table: "items",
                columns: new[] { "shop_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_password_reset_tokens_user_id",
                table: "password_reset_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token",
                table: "refresh_tokens",
                column: "token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_user_id",
                table: "refresh_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_sale_items_inventory_batch_id",
                table: "sale_items",
                column: "inventory_batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_sale_items_item_id",
                table: "sale_items",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_sale_items_sale_id",
                table: "sale_items",
                column: "sale_id");

            migrationBuilder.CreateIndex(
                name: "ix_sale_items_shop_id",
                table: "sale_items",
                column: "shop_id");

            migrationBuilder.CreateIndex(
                name: "ix_sale_items_shop_id_item_id",
                table: "sale_items",
                columns: new[] { "shop_id", "item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_customer_id",
                table: "sales",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_shop_id",
                table: "sales",
                column: "shop_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_shop_id_customer_id",
                table: "sales",
                columns: new[] { "shop_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_shop_id_invoice_number",
                table: "sales",
                columns: new[] { "shop_id", "invoice_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_shop_id_sold_at",
                table: "sales",
                columns: new[] { "shop_id", "sold_at" });

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

            migrationBuilder.CreateIndex(
                name: "ix_stock_transactions_inventory_batch_id_item_id_shop_id",
                table: "stock_transactions",
                columns: new[] { "inventory_batch_id", "item_id", "shop_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transactions_item_id_shop_id",
                table: "stock_transactions",
                columns: new[] { "item_id", "shop_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transactions_shop_id_inventory_batch_id_performed_at",
                table: "stock_transactions",
                columns: new[] { "shop_id", "inventory_batch_id", "performed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transactions_shop_id_item_id_performed_at",
                table: "stock_transactions",
                columns: new[] { "shop_id", "item_id", "performed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_ledger_entries_batch_id",
                table: "supplier_ledger_entries",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_ledger_entries_shop_id_batch_id_entry_type",
                table: "supplier_ledger_entries",
                columns: new[] { "shop_id", "batch_id", "entry_type" });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_ledger_entries_shop_id_supplier_id_entry_date",
                table: "supplier_ledger_entries",
                columns: new[] { "shop_id", "supplier_id", "entry_date" });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_ledger_entries_supplier_id",
                table: "supplier_ledger_entries",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "ix_suppliers_shop_id",
                table: "suppliers",
                column: "shop_id");

            migrationBuilder.CreateIndex(
                name: "ix_suppliers_shop_id_is_active",
                table: "suppliers",
                columns: new[] { "shop_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_suppliers_shop_id_is_system",
                table: "suppliers",
                columns: new[] { "shop_id", "is_system" },
                unique: true,
                filter: "is_system = true");

            migrationBuilder.CreateIndex(
                name: "ix_user_external_logins_provider_provider_key",
                table: "user_external_logins",
                columns: new[] { "provider", "provider_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_external_logins_user_id",
                table: "user_external_logins",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "email",
                unique: true,
                filter: "email IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_users_phone_number",
                table: "users",
                column: "phone_number",
                unique: true,
                filter: "phone_number IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bank_accounts");

            migrationBuilder.DropTable(
                name: "customer_ledger_entries");

            migrationBuilder.DropTable(
                name: "expenses");

            migrationBuilder.DropTable(
                name: "inventory");

            migrationBuilder.DropTable(
                name: "password_reset_tokens");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "sale_items");

            migrationBuilder.DropTable(
                name: "shop_memberships");

            migrationBuilder.DropTable(
                name: "stock_transactions");

            migrationBuilder.DropTable(
                name: "user_external_logins");

            migrationBuilder.DropTable(
                name: "expense_categories");

            migrationBuilder.DropTable(
                name: "supplier_ledger_entries");

            migrationBuilder.DropTable(
                name: "sales");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "inventory_batches");

            migrationBuilder.DropTable(
                name: "customers");

            migrationBuilder.DropTable(
                name: "items");

            migrationBuilder.DropTable(
                name: "suppliers");

            migrationBuilder.DropTable(
                name: "shops");
        }
    }
}
#pragma warning restore CA1861
