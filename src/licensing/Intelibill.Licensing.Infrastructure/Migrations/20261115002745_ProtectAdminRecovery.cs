using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Licensing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ProtectAdminRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "issued_for_recovery",
                table: "bootstrap_enrollment_credentials",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "recovery_key_id",
                table: "admin_totp",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "recovery_continuation_digest",
                table: "admin_identities",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_admin_recovery_continuation",
                table: "admin_identities",
                sql: "recovery_continuation_digest IS NULL OR (mfa_enrolled_at IS NULL AND recovery_continuation_digest ~ '^[0-9A-F]{64}$')");

            migrationBuilder.Sql("""
                UPDATE public.admin_totp
                SET encrypted_secret = decode(repeat('00', 48), 'hex')
                WHERE retired_at IS NOT NULL AND encrypted_secret <> decode(repeat('00', 48), 'hex');

                CREATE FUNCTION public.protect_admin_recovery_evidence() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'UPDATE' THEN
                        IF OLD.kind = 2 AND OLD.redeemed_at IS NULL AND OLD.retired_at IS NULL
                            AND num_nonnulls(NEW.redeemed_at, NEW.retired_at) = 1
                            AND (NEW.redeemed_at IS NULL OR NEW.redeemed_at < OLD.grant_expires_at)
                            AND (to_jsonb(NEW) - ARRAY['redeemed_at', 'retired_at'])
                                = (to_jsonb(OLD) - ARRAY['redeemed_at', 'retired_at']) THEN
                            RETURN NEW;
                        END IF;
                    END IF;
                    RAISE EXCEPTION 'Administrator recovery evidence is immutable' USING ERRCODE = '55000';
                END $$;

                CREATE TRIGGER admin_recovery_evidence_immutable
                    BEFORE UPDATE OR DELETE ON public.admin_recoveries
                    FOR EACH ROW EXECUTE FUNCTION public.protect_admin_recovery_evidence();
                CREATE TRIGGER admin_recovery_evidence_no_truncate
                    BEFORE TRUNCATE ON public.admin_recoveries
                    FOR EACH STATEMENT EXECUTE FUNCTION public.protect_admin_recovery_evidence();
                """);

            // A predecessor can still reach its resume endpoint. Its ordinary enrollment insert
            // must fail as part of the same transaction, leaving current recovery credentials intact.
            migrationBuilder.Sql("""
                CREATE FUNCTION public.require_recovery_enrollment() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    IF NOT NEW.issued_for_recovery AND EXISTS (
                        SELECT FROM public.admin_recoveries WHERE admin_id = NEW.owner_id
                    ) THEN
                        RAISE EXCEPTION 'Recovery enrollment proof is required' USING ERRCODE = '55000';
                    END IF;
                    RETURN NEW;
                END $$;

                CREATE CONSTRAINT TRIGGER enrollment_requires_recovery_proof
                    AFTER INSERT ON public.bootstrap_enrollment_credentials
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW
                    EXECUTE FUNCTION public.require_recovery_enrollment();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE public.admin_identities, public.admin_recoveries IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM public.admin_recoveries) THEN
                        RAISE EXCEPTION 'Administrator recovery evidence exists' USING ERRCODE = '55000';
                    END IF;
                END $$;

                DROP TRIGGER enrollment_requires_recovery_proof ON public.bootstrap_enrollment_credentials;
                DROP FUNCTION public.require_recovery_enrollment();
                DROP TRIGGER admin_recovery_evidence_no_truncate ON public.admin_recoveries;
                DROP TRIGGER admin_recovery_evidence_immutable ON public.admin_recoveries;
                DROP FUNCTION public.protect_admin_recovery_evidence();
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_admin_recovery_continuation",
                table: "admin_identities");

            migrationBuilder.DropColumn(
                name: "issued_for_recovery",
                table: "bootstrap_enrollment_credentials");

            migrationBuilder.DropColumn(
                name: "recovery_key_id",
                table: "admin_totp");

            migrationBuilder.DropColumn(
                name: "recovery_continuation_digest",
                table: "admin_identities");
        }
    }
}
