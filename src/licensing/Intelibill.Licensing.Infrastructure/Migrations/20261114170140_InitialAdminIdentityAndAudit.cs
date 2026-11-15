using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Licensing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialAdminIdentityAndAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "admin_identities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    verification_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    mfa_enrolled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_admin_identities", x => x.id);
                    table.CheckConstraint("ck_admin_mfa_date", "mfa_enrolled_at >= verified_at");
                });

            migrationBuilder.CreateTable(
                name: "bootstrap_rate_buckets",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    window_ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bootstrap_rate_buckets", x => x.id);
                    table.CheckConstraint("ck_bootstrap_rate_bucket", "id BETWEEN 1 AND 3 AND attempts >= 0");
                });

            migrationBuilder.CreateTable(
                name: "bootstrap_enrollment_credentials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bootstrap_enrollment_credentials", x => x.id);
                    table.CheckConstraint("ck_bootstrap_enrollment_evidence", "digest ~ '^[0-9A-F]{64}$' AND issued_at < expires_at AND (revoked_at IS NULL OR revoked_at >= issued_at)");
                    table.ForeignKey(
                        name: "fk_bootstrap_enrollment_credentials_admin_identities_owner_id",
                        column: x => x.owner_id,
                        principalTable: "admin_identities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "initial_admin_bootstrap",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    proof_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    verified_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    verification_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    admin_origin = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    prepared_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_initial_admin_bootstrap", x => x.id);
                    table.CheckConstraint("ck_bootstrap_preparation", "(num_nonnulls(proof_digest, verified_email, verification_reference, admin_origin, prepared_at, expires_at) = 0\n  OR (num_nonnulls(proof_digest, verified_email, verification_reference, admin_origin, prepared_at, expires_at) = 6\n      AND proof_digest ~ '^[0-9A-F]{64}$' AND prepared_at < expires_at))\nAND ((owner_id IS NULL AND consumed_at IS NULL)\n  OR (owner_id IS NOT NULL AND consumed_at IS NOT NULL AND prepared_at IS NOT NULL\n      AND prepared_at <= consumed_at AND consumed_at < expires_at))");
                    table.CheckConstraint("ck_bootstrap_singleton", "id = 1");
                    table.ForeignKey(
                        name: "fk_initial_admin_bootstrap_admin_identity_owner_id",
                        column: x => x.owner_id,
                        principalTable: "admin_identities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "provider_audit_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<int>(type: "integer", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provider_audit_events", x => x.id);
                    table.CheckConstraint("ck_bootstrap_audit_action", "action BETWEEN 1 AND 6");
                    table.ForeignKey(
                        name: "fk_provider_audit_events_admin_identities_owner_id",
                        column: x => x.owner_id,
                        principalTable: "admin_identities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql("INSERT INTO public.initial_admin_bootstrap (id) VALUES (1)");

            migrationBuilder.CreateIndex(
                name: "ix_admin_identities_email",
                table: "admin_identities",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bootstrap_enrollment_credentials_digest",
                table: "bootstrap_enrollment_credentials",
                column: "digest",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bootstrap_enrollment_credentials_owner_id",
                table: "bootstrap_enrollment_credentials",
                column: "owner_id",
                unique: true,
                filter: "revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_initial_admin_bootstrap_owner_id",
                table: "initial_admin_bootstrap",
                column: "owner_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_provider_audit_events_owner_id",
                table: "provider_audit_events",
                column: "owner_id");

            migrationBuilder.Sql("""
                CREATE FUNCTION public.refuse_provider_audit_mutation() RETURNS trigger
                LANGUAGE plpgsql SET search_path = pg_catalog AS $$
                BEGIN
                    RAISE EXCEPTION 'Provider audit is append-only' USING ERRCODE = '55000';
                END;
                $$;
                CREATE TRIGGER provider_audit_immutable
                    BEFORE UPDATE OR DELETE OR TRUNCATE ON public.provider_audit_events
                    FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_provider_audit_mutation();

                CREATE FUNCTION public.preserve_initial_admin_bootstrap() RETURNS trigger
                LANGUAGE plpgsql SET search_path = pg_catalog AS $$
                BEGIN
                    IF TG_OP <> 'UPDATE' THEN
                        RAISE EXCEPTION 'Bootstrap state cannot be removed' USING ERRCODE = '55000';
                    END IF;
                    IF OLD.consumed_at IS NOT NULL THEN
                        RAISE EXCEPTION 'Consumed bootstrap state is permanent' USING ERRCODE = '55000';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER initial_admin_bootstrap_terminal
                    BEFORE UPDATE OR DELETE ON public.initial_admin_bootstrap
                    FOR EACH ROW EXECUTE FUNCTION public.preserve_initial_admin_bootstrap();
                CREATE TRIGGER initial_admin_bootstrap_no_truncate
                    BEFORE TRUNCATE ON public.initial_admin_bootstrap
                    FOR EACH STATEMENT EXECUTE FUNCTION public.preserve_initial_admin_bootstrap();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER initial_admin_bootstrap_no_truncate ON public.initial_admin_bootstrap;
                DROP TRIGGER initial_admin_bootstrap_terminal ON public.initial_admin_bootstrap;
                DROP TRIGGER provider_audit_immutable ON public.provider_audit_events;
                DROP FUNCTION public.preserve_initial_admin_bootstrap();
                DROP FUNCTION public.refuse_provider_audit_mutation();
                """);

            migrationBuilder.DropTable(
                name: "bootstrap_enrollment_credentials");

            migrationBuilder.DropTable(
                name: "bootstrap_rate_buckets");

            migrationBuilder.DropTable(
                name: "initial_admin_bootstrap");

            migrationBuilder.DropTable(
                name: "provider_audit_events");

            migrationBuilder.DropTable(
                name: "admin_identities");
        }
    }
}
