using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // EF-generated column arrays

namespace Intelibill.Infrastructure.Migrations
{
    /// <summary>
    /// Offline licence evidence: the original grant of each new invoice lease, the signed permits issued
    /// from it and the public keys that signed them. Append-only and isolated per shop. Existing leases
    /// keep their nullable reserver columns and gain only a (id, shop_id) key for the tenant-scoped
    /// reference. Apply before any API that issues permits.
    /// </summary>
    public partial class AddOfflineLicenseEvidence : Migration
    {
        private static readonly string[] Tables =
            ["offline_license_grants", "offline_license_permits", "offline_license_verification_keys"];

        private const string Token = "'^[A-Za-z0-9_-]+\\.[A-Za-z0-9_-]+\\.[A-Za-z0-9_-]+$'";
        private const string KeyId = "'^[A-Za-z0-9_-]{1,128}$'";
        private const string Reason = "'^[A-Za-z]{1,32}$'";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_invoice_leases_id_shop_id",
                table: "invoice_leases",
                columns: new[] { "id", "shop_id" });

            migrationBuilder.CreateTable(
                name: "offline_license_grants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lease_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    affiliation_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_revision = table.Column<long>(type: "bigint", nullable: false),
                    shop_revision = table.Column<long>(type: "bigint", nullable: false),
                    account_evidence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_evidence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    authority_verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    authority_valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    membership_authority_version = table.Column<long>(type: "bigint", nullable: false),
                    session_scope_version = table.Column<long>(type: "bigint", nullable: false),
                    device_verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    auth_deadline = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    key_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    signed_grant = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_offline_license_grants", x => x.id);
                    table.UniqueConstraint("ak_offline_license_grants_id_shop_id", x => new { x.id, x.shop_id });
                    table.ForeignKey(
                        name: "fk_offline_license_grants_invoice_leases_lease_id_shop_id",
                        columns: x => new { x.lease_id, x.shop_id },
                        principalTable: "invoice_leases",
                        principalColumns: new[] { "id", "shop_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_offline_license_grants_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "offline_license_verification_keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    public_key = table.Column<byte[]>(type: "bytea", nullable: false),
                    fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    first_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_offline_license_verification_keys", x => x.id);
                    table.ForeignKey(
                        name: "fk_offline_license_verification_keys_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "offline_license_permits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    grant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lease_id = table.Column<Guid>(type: "uuid", nullable: false),
                    snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    till_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    batch_count = table.Column<int>(type: "integer", nullable: false),
                    catalog_version = table.Column<long>(type: "bigint", nullable: false),
                    current_account_revision = table.Column<long>(type: "bigint", nullable: false),
                    current_shop_revision = table.Column<long>(type: "bigint", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    not_before = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    limiting_reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    key_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    signed_permit = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_offline_license_permits", x => x.id);
                    table.ForeignKey(
                        name: "fk_offline_license_permits_offline_license_grants_grant_id_sho",
                        columns: x => new { x.grant_id, x.shop_id },
                        principalTable: "offline_license_grants",
                        principalColumns: new[] { "id", "shop_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_offline_license_permits_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_offline_license_grants_lease_id_shop_id",
                table: "offline_license_grants",
                columns: new[] { "lease_id", "shop_id" });

            migrationBuilder.CreateIndex(
                name: "ix_offline_license_grants_shop_id_lease_id",
                table: "offline_license_grants",
                columns: new[] { "shop_id", "lease_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_offline_license_permits_grant_id_shop_id",
                table: "offline_license_permits",
                columns: new[] { "grant_id", "shop_id" });

            migrationBuilder.CreateIndex(
                name: "ix_offline_license_permits_shop_id_lease_id_valid_until",
                table: "offline_license_permits",
                columns: new[] { "shop_id", "lease_id", "valid_until" });

            migrationBuilder.CreateIndex(
                name: "ix_offline_license_permits_shop_id_snapshot_id",
                table: "offline_license_permits",
                columns: new[] { "shop_id", "snapshot_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_offline_license_verification_keys_shop_id_key_id",
                table: "offline_license_verification_keys",
                columns: new[] { "shop_id", "key_id" },
                unique: true);

            migrationBuilder.Sql($"""
                ALTER TABLE public.offline_license_grants ADD CONSTRAINT valid_offline_license_grant CHECK (
                    account_revision > 0 AND shop_revision > 0 AND membership_authority_version >= 0
                    AND session_scope_version >= 0 AND isfinite(authority_verified_at) AND isfinite(authority_valid_until)
                    AND isfinite(device_verified_at) AND isfinite(auth_deadline) AND isfinite(captured_at)
                    AND authority_verified_at <= device_verified_at AND device_verified_at <= captured_at
                    AND captured_at < authority_valid_until AND captured_at < auth_deadline
                    AND btrim(device_id) <> '' AND key_id ~ {KeyId} AND signed_grant ~ {Token});
                ALTER TABLE public.offline_license_permits ADD CONSTRAINT valid_offline_license_permit CHECK (
                    current_account_revision > 0 AND current_shop_revision > 0 AND batch_count >= 0 AND catalog_version >= 0
                    AND isfinite(completed_at) AND isfinite(not_before) AND isfinite(valid_until)
                    AND completed_at <= not_before AND not_before < valid_until
                    AND btrim(device_id) <> '' AND limiting_reason ~ {Reason}
                    AND key_id ~ {KeyId} AND signed_permit ~ {Token});
                ALTER TABLE public.offline_license_verification_keys ADD CONSTRAINT valid_offline_license_verification_key CHECK (
                    key_id ~ {KeyId} AND isfinite(first_used_at) AND octet_length(public_key) BETWEEN 256 AND 2048
                    AND fingerprint = encode(sha256(public_key), 'hex'));
                """);
            foreach (var table in Tables)
                migrationBuilder.Sql($"""
                    ALTER TABLE public.{table} ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE public.{table} FORCE ROW LEVEL SECURITY;
                    CREATE POLICY {table}_shop_isolation ON public.{table}
                        USING (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid)
                        WITH CHECK (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid);
                    CREATE TRIGGER retain_rows BEFORE UPDATE OR DELETE ON public.{table}
                        FOR EACH ROW EXECUTE FUNCTION public.retain_entitlement_evidence();
                    CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.{table}
                        FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();
                    """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Issued permits stay provable only while their grants and public keys exist. Row security
            // would hide other shops' rows from this check, so it is lifted first; a refusal rolls that back.
            // Earlier entitlement tables are untouched by this migration, so a populated pre-P05 database
            // with no offline licence evidence rolls back cleanly to let P04 guards enforce their own contract.
            migrationBuilder.Sql("""
                LOCK TABLE public.offline_license_permits, public.offline_license_grants,
                    public.offline_license_verification_keys IN ACCESS EXCLUSIVE MODE;
                ALTER TABLE public.offline_license_permits NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE public.offline_license_grants NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE public.offline_license_verification_keys NO FORCE ROW LEVEL SECURITY;
                DO $$ BEGIN
                    IF EXISTS (SELECT FROM public.offline_license_permits) OR EXISTS (SELECT FROM public.offline_license_grants)
                        OR EXISTS (SELECT FROM public.offline_license_verification_keys) THEN
                        RAISE EXCEPTION 'Retained offline licence evidence prevents downgrade' USING ERRCODE = '55000';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropTable(
                name: "offline_license_permits");

            migrationBuilder.DropTable(
                name: "offline_license_verification_keys");

            migrationBuilder.DropTable(
                name: "offline_license_grants");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_invoice_leases_id_shop_id",
                table: "invoice_leases");
        }
    }
}
