using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Licensing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminInvitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_bootstrap_audit_action",
                table: "provider_audit_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_bootstrap_rate_bucket",
                table: "bootstrap_rate_buckets");

            migrationBuilder.AddColumn<Guid>(
                name: "invitation_id",
                table: "provider_audit_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "admin_invitations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    token_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    invited_by = table.Column<Guid>(type: "uuid", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    accepted_admin_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failed_attempts = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_admin_invitations", x => x.id);
                    table.CheckConstraint("ck_admin_invitation_acceptance", "(accepted_at IS NULL AND accepted_admin_id IS NULL)\nOR (accepted_at IS NOT NULL AND accepted_admin_id IS NOT NULL AND revoked_at IS NULL\n    AND accepted_at >= issued_at AND accepted_at < expires_at)");
                    table.CheckConstraint("ck_admin_invitation_evidence", "token_digest ~ '^[0-9A-F]{64}$' AND issued_at < expires_at\nAND failed_attempts BETWEEN 0 AND 5\nAND (revoked_at IS NULL OR revoked_at >= issued_at)");
                    table.ForeignKey(
                        name: "fk_admin_invitations_admin_identities_accepted_admin_id",
                        column: x => x.accepted_admin_id,
                        principalTable: "admin_identities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_admin_invitations_admin_identities_invited_by",
                        column: x => x.invited_by,
                        principalTable: "admin_identities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_provider_audit_events_invitation_id",
                table: "provider_audit_events",
                column: "invitation_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bootstrap_audit_action",
                table: "provider_audit_events",
                sql: "action BETWEEN 1 AND 11");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bootstrap_rate_bucket",
                table: "bootstrap_rate_buckets",
                sql: "id BETWEEN 1 AND 5 AND attempts >= 0");

            migrationBuilder.CreateIndex(
                name: "ix_admin_invitations_accepted_admin_id",
                table: "admin_invitations",
                column: "accepted_admin_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_admin_invitations_invited_by",
                table: "admin_invitations",
                column: "invited_by");

            migrationBuilder.CreateIndex(
                name: "ix_admin_invitations_open_email",
                table: "admin_invitations",
                column: "email",
                unique: true,
                filter: "accepted_at IS NULL AND revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_admin_invitations_token_digest",
                table: "admin_invitations",
                column: "token_digest",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_provider_audit_events_admin_invitation_invitation_id",
                table: "provider_audit_events",
                column: "invitation_id",
                principalTable: "admin_invitations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                CREATE FUNCTION public.preserve_admin_invitation() RETURNS trigger
                LANGUAGE plpgsql SET search_path = pg_catalog AS $$
                BEGIN
                    IF TG_OP <> 'UPDATE' THEN
                        RAISE EXCEPTION 'Admin invitations cannot be removed' USING ERRCODE = '55000';
                    END IF;
                    IF OLD.accepted_at IS NOT NULL OR OLD.revoked_at IS NOT NULL THEN
                        RAISE EXCEPTION 'Settled admin invitations are permanent' USING ERRCODE = '55000';
                    END IF;
                    IF NEW.id <> OLD.id OR NEW.email <> OLD.email OR NEW.token_digest <> OLD.token_digest
                        OR NEW.invited_by <> OLD.invited_by OR NEW.issued_at <> OLD.issued_at
                        OR NEW.expires_at <> OLD.expires_at OR NEW.failed_attempts < OLD.failed_attempts THEN
                        RAISE EXCEPTION 'Admin invitation evidence is immutable' USING ERRCODE = '55000';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER admin_invitations_settled
                    BEFORE UPDATE OR DELETE ON public.admin_invitations
                    FOR EACH ROW EXECUTE FUNCTION public.preserve_admin_invitation();
                CREATE TRIGGER admin_invitations_no_truncate
                    BEFORE TRUNCATE ON public.admin_invitations
                    FOR EACH STATEMENT EXECUTE FUNCTION public.preserve_admin_invitation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Invitation evidence and its audit trail are retained: refuse once any exists.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM public.admin_invitations) THEN
                        RAISE EXCEPTION 'Admin invitation evidence exists; this migration cannot be reverted'
                            USING ERRCODE = '55000';
                    END IF;
                END;
                $$;
                DROP TRIGGER admin_invitations_no_truncate ON public.admin_invitations;
                DROP TRIGGER admin_invitations_settled ON public.admin_invitations;
                DROP FUNCTION public.preserve_admin_invitation();
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_provider_audit_events_admin_invitation_invitation_id",
                table: "provider_audit_events");

            migrationBuilder.DropTable(
                name: "admin_invitations");

            migrationBuilder.DropIndex(
                name: "ix_provider_audit_events_invitation_id",
                table: "provider_audit_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_bootstrap_audit_action",
                table: "provider_audit_events");

            migrationBuilder.DropCheckConstraint(
                name: "ck_bootstrap_rate_bucket",
                table: "bootstrap_rate_buckets");

            migrationBuilder.DropColumn(
                name: "invitation_id",
                table: "provider_audit_events");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bootstrap_audit_action",
                table: "provider_audit_events",
                sql: "action BETWEEN 1 AND 6");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bootstrap_rate_bucket",
                table: "bootstrap_rate_buckets",
                sql: "id BETWEEN 1 AND 3 AND attempts >= 0");
        }
    }
}
