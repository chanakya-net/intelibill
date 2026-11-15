using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Licensing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogueDrafts : Migration
    {
        private static readonly string[] ChangeColumns = ["target_kind", "target_id", "revision"];
        private static readonly string[] VersionColumns = ["plan_id", "version_number"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "catalogue_draft_changes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_kind = table.Column<int>(type: "integer", nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    actor_admin_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_catalogue_draft_changes", x => x.id);
                    table.CheckConstraint("ck_catalogue_draft_changes_reason", "reason = btrim(reason) AND char_length(reason) BETWEEN 1 AND 500");
                    table.CheckConstraint("ck_catalogue_draft_changes_revision", "revision >= 1");
                    table.CheckConstraint("ck_catalogue_draft_changes_target", "target_kind IN (1, 2)");
                    table.ForeignKey(
                        name: "fk_catalogue_draft_changes_admin_identities_actor_admin_id",
                        column: x => x.actor_admin_id,
                        principalTable: "admin_identities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_admin_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plans", x => x.id);
                    table.CheckConstraint("ck_plans_id", "id <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("ck_plans_kind", "kind IN (1, 2, 3)");
                    table.ForeignKey(
                        name: "fk_plans_admin_identities_created_by_admin_id",
                        column: x => x.created_by_admin_id,
                        principalTable: "admin_identities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trial_policy_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    duration_days = table.Column<int>(type: "integer", nullable: false),
                    active_member_cap = table.Column<int>(type: "integer", nullable: true),
                    max_concurrent_trial_shops = table.Column<int>(type: "integer", nullable: true),
                    trial_terms_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_admin_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_admin_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trial_policy_versions", x => x.id);
                    table.CheckConstraint("ck_trial_policy_versions_caps", "active_member_cap BETWEEN 1 AND 100000 AND max_concurrent_trial_shops BETWEEN 1 AND 100000");
                    table.CheckConstraint("ck_trial_policy_versions_duration", "duration_days = 14");
                    table.CheckConstraint("ck_trial_policy_versions_numbers", "version_number >= 1 AND revision >= 1");
                    table.CheckConstraint("ck_trial_policy_versions_status", "status IN (1, 2, 3)");
                    table.CheckConstraint("ck_trial_policy_versions_terms", "trial_terms_version ~ '^[A-Za-z0-9][A-Za-z0-9._-]{0,39}$'");
                });

            migrationBuilder.CreateTable(
                name: "plan_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    monthly_price_paise = table.Column<long>(type: "bigint", nullable: true),
                    annual_price_paise = table.Column<long>(type: "bigint", nullable: true),
                    included_shops = table.Column<int>(type: "integer", nullable: true),
                    members_per_shop = table.Column<int>(type: "integer", nullable: true),
                    extra_shops_offered = table.Column<bool>(type: "boolean", nullable: true),
                    extra_shop_monthly_price_paise = table.Column<long>(type: "bigint", nullable: true),
                    extra_shop_annual_price_paise = table.Column<long>(type: "bigint", nullable: true),
                    customer_terms_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    refund_policy_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    negotiation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_admin_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_admin_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plan_versions", x => x.id);
                    table.CheckConstraint("ck_plan_versions_amounts", "monthly_price_paise BETWEEN 0 AND 1000000000000\nAND annual_price_paise BETWEEN 0 AND 1000000000000\nAND extra_shop_monthly_price_paise BETWEEN 0 AND 1000000000000\nAND extra_shop_annual_price_paise BETWEEN 0 AND 1000000000000");
                    table.CheckConstraint("ck_plan_versions_capacities", "included_shops BETWEEN 1 AND 100000 AND members_per_shop BETWEEN 1 AND 100000");
                    table.CheckConstraint("ck_plan_versions_currency", "currency = 'INR'");
                    table.CheckConstraint("ck_plan_versions_extra_shop_offer", "extra_shops_offered IS TRUE\nOR (extra_shop_monthly_price_paise IS NULL AND extra_shop_annual_price_paise IS NULL)");
                    table.CheckConstraint("ck_plan_versions_name", "name = btrim(name) AND char_length(name) BETWEEN 1 AND 80");
                    table.CheckConstraint("ck_plan_versions_numbers", "version_number >= 1 AND revision >= 1");
                    table.CheckConstraint("ck_plan_versions_policy_versions", "customer_terms_version ~ '^[A-Za-z0-9][A-Za-z0-9._-]{0,39}$' AND refund_policy_version ~ '^[A-Za-z0-9][A-Za-z0-9._-]{0,39}$'");
                    table.CheckConstraint("ck_plan_versions_reason", "negotiation_reason = btrim(negotiation_reason) AND char_length(negotiation_reason) BETWEEN 1 AND 500");
                    table.CheckConstraint("ck_plan_versions_status", "status IN (1, 2, 3)");
                    table.ForeignKey(
                        name: "fk_plan_versions_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_catalogue_draft_changes_actor_admin_id",
                table: "catalogue_draft_changes",
                column: "actor_admin_id");

            migrationBuilder.CreateIndex(
                name: "ix_catalogue_draft_changes_target_kind_target_id_revision",
                table: "catalogue_draft_changes",
                columns: ChangeColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plan_versions_plan_id_version_number",
                table: "plan_versions",
                columns: VersionColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_plan_versions_open_draft",
                table: "plan_versions",
                column: "plan_id",
                unique: true,
                filter: "status = 1");

            migrationBuilder.CreateIndex(
                name: "ix_plans_created_by_admin_id",
                table: "plans",
                column: "created_by_admin_id");

            migrationBuilder.CreateIndex(
                name: "ix_trial_policy_versions_version_number",
                table: "trial_policy_versions",
                column: "version_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_trial_policy_versions_open_draft",
                table: "trial_policy_versions",
                column: "status",
                unique: true,
                filter: "status = 1");
            migrationBuilder.Sql(ManagerContext);
            migrationBuilder.Sql(RowSecurity);
            migrationBuilder.Sql(Retention);
        }

        // The catalogue is provider-only: a transaction sees or writes it only after naming (transaction-locally)
        // an active, MFA-enrolled administrator who holds the Licensing Manager grant (capability bit 4).
        private const string ManagerContext = """
            CREATE FUNCTION public.catalogue_manager_id() RETURNS uuid
            LANGUAGE sql STABLE AS $$
                SELECT admin.id FROM public.admin_identities admin
                WHERE admin.id = nullif(current_setting('app.catalogue_admin_id', true), '')::uuid
                  AND admin.deactivated_at IS NULL AND admin.mfa_enrolled_at IS NOT NULL
                  AND admin.capabilities & 4 = 4
            $$;
            """;

        private const string RowSecurity = """
            ALTER TABLE public.plans ENABLE ROW LEVEL SECURITY;
            ALTER TABLE public.plans FORCE ROW LEVEL SECURITY;
            ALTER TABLE public.plan_versions ENABLE ROW LEVEL SECURITY;
            ALTER TABLE public.plan_versions FORCE ROW LEVEL SECURITY;
            ALTER TABLE public.trial_policy_versions ENABLE ROW LEVEL SECURITY;
            ALTER TABLE public.trial_policy_versions FORCE ROW LEVEL SECURITY;
            ALTER TABLE public.catalogue_draft_changes ENABLE ROW LEVEL SECURITY;
            ALTER TABLE public.catalogue_draft_changes FORCE ROW LEVEL SECURITY;

            CREATE POLICY catalogue_read ON public.plans FOR SELECT USING (public.catalogue_manager_id() IS NOT NULL);
            CREATE POLICY catalogue_insert ON public.plans FOR INSERT
                WITH CHECK (created_by_admin_id = public.catalogue_manager_id());

            CREATE POLICY catalogue_read ON public.plan_versions FOR SELECT USING (public.catalogue_manager_id() IS NOT NULL);
            CREATE POLICY catalogue_insert ON public.plan_versions FOR INSERT
                WITH CHECK (created_by_admin_id = public.catalogue_manager_id()
                    AND updated_by_admin_id = public.catalogue_manager_id() AND status = 1);
            CREATE POLICY catalogue_revise ON public.plan_versions FOR UPDATE
                USING (public.catalogue_manager_id() IS NOT NULL)
                WITH CHECK (updated_by_admin_id = public.catalogue_manager_id() AND status = 1);

            CREATE POLICY catalogue_read ON public.trial_policy_versions FOR SELECT
                USING (public.catalogue_manager_id() IS NOT NULL);
            CREATE POLICY catalogue_insert ON public.trial_policy_versions FOR INSERT
                WITH CHECK (created_by_admin_id = public.catalogue_manager_id()
                    AND updated_by_admin_id = public.catalogue_manager_id() AND status = 1);
            CREATE POLICY catalogue_revise ON public.trial_policy_versions FOR UPDATE
                USING (public.catalogue_manager_id() IS NOT NULL)
                WITH CHECK (updated_by_admin_id = public.catalogue_manager_id() AND status = 1);

            CREATE POLICY catalogue_read ON public.catalogue_draft_changes FOR SELECT
                USING (public.catalogue_manager_id() IS NOT NULL);
            CREATE POLICY catalogue_insert ON public.catalogue_draft_changes FOR INSERT
                WITH CHECK (actor_admin_id = public.catalogue_manager_id());
            """;

        // Plans and change evidence are append-only; versions are never deleted and a draft only advances
        // one revision at a time while it stays a draft (publication will widen this guard).
        private const string Retention = """
            CREATE FUNCTION public.refuse_catalogue_evidence_loss() RETURNS trigger
            LANGUAGE plpgsql AS $$
            BEGIN
                RAISE EXCEPTION 'Catalogue facts are retained' USING ERRCODE = '55000';
            END $$;

            CREATE FUNCTION public.guard_catalogue_draft_version() RETURNS trigger
            LANGUAGE plpgsql AS $$
            BEGIN
                IF OLD.status <> 1 OR NEW.status <> 1 OR NEW.revision <> OLD.revision + 1
                    OR NEW.id <> OLD.id OR NEW.version_number <> OLD.version_number
                    OR NEW.created_at <> OLD.created_at OR NEW.created_by_admin_id <> OLD.created_by_admin_id THEN
                    RAISE EXCEPTION 'Only an open draft advances, one revision at a time' USING ERRCODE = '55000';
                END IF;
                IF TG_TABLE_NAME = 'plan_versions' THEN
                    IF NEW.plan_id <> OLD.plan_id THEN
                        RAISE EXCEPTION 'A plan version keeps its plan' USING ERRCODE = '55000';
                    END IF;
                END IF;
                RETURN NEW;
            END $$;

            CREATE TRIGGER plans_retained BEFORE UPDATE OR DELETE OR TRUNCATE ON public.plans
                FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_catalogue_evidence_loss();
            CREATE TRIGGER catalogue_draft_changes_retained BEFORE UPDATE OR DELETE OR TRUNCATE
                ON public.catalogue_draft_changes
                FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_catalogue_evidence_loss();
            CREATE TRIGGER plan_versions_retained BEFORE DELETE OR TRUNCATE ON public.plan_versions
                FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_catalogue_evidence_loss();
            CREATE TRIGGER trial_policy_versions_retained BEFORE DELETE OR TRUNCATE ON public.trial_policy_versions
                FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_catalogue_evidence_loss();
            CREATE TRIGGER plan_versions_draft_guard BEFORE UPDATE ON public.plan_versions
                FOR EACH ROW EXECUTE FUNCTION public.guard_catalogue_draft_version();
            CREATE TRIGGER trial_policy_versions_draft_guard BEFORE UPDATE ON public.trial_policy_versions
                FOR EACH ROW EXECUTE FUNCTION public.guard_catalogue_draft_version();
            """;

        // Rolling back would drop drafted commercial facts. Refuse unless the catalogue is empty; the owner
        // must stop forcing RLS first or it would see no rows and drop real evidence.
        private const string RefuseEvidenceLoss = """
            LOCK TABLE public.plans, public.plan_versions, public.trial_policy_versions,
                public.catalogue_draft_changes IN ACCESS EXCLUSIVE MODE;
            ALTER TABLE public.plans NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE public.plan_versions NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE public.trial_policy_versions NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE public.catalogue_draft_changes NO FORCE ROW LEVEL SECURITY;
            DO $$
            BEGIN
                IF EXISTS (SELECT FROM public.plans) OR EXISTS (SELECT FROM public.plan_versions)
                    OR EXISTS (SELECT FROM public.trial_policy_versions)
                    OR EXISTS (SELECT FROM public.catalogue_draft_changes) THEN
                    RAISE EXCEPTION 'Catalogue draft evidence exists' USING ERRCODE = '55000';
                END IF;
            END $$;
            """;

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RefuseEvidenceLoss);
            migrationBuilder.DropTable(
                name: "catalogue_draft_changes");

            migrationBuilder.DropTable(
                name: "plan_versions");

            migrationBuilder.DropTable(
                name: "trial_policy_versions");

            migrationBuilder.DropTable(
                name: "plans");

            migrationBuilder.Sql("""
                DROP FUNCTION public.guard_catalogue_draft_version();
                DROP FUNCTION public.refuse_catalogue_evidence_loss();
                DROP FUNCTION public.catalogue_manager_id();
                """);
        }
    }
}
