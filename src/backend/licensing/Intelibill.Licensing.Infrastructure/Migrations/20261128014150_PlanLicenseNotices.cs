using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Licensing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PlanLicenseNotices : Migration
    {
        private static readonly string[] ContactEventRevisionColumns = ["contact_id", "revision", "event_kind"];
        private static readonly string[] ContactAccountColumns = ["account_id", "id"];
        private static readonly string[] ContactActiveDestinationColumns = ["account_id", "destination_hash"];
        private static readonly string[] ContactActiveUserColumns = ["account_id", "merchant_user_id"];
        private static readonly string[] ContactOperationColumns = ["account_id", "operation_key"];
        private static readonly string[] JobAccountActorDueColumns = ["account_id", "recipient_user_id", "due_at", "id"];
        private static readonly string[] JobAccountDueColumns = ["account_id", "due_at", "id"];
        private static readonly string[] JobShopActorDueColumns = ["scope_id", "recipient_user_id", "due_at", "id"];
        private static readonly string[] JobSemanticKeyColumns =
        [
            "account_id", "scope_kind", "scope_id", "recipient_key", "purpose",
            "source_kind", "source_id", "source_revision", "threshold",
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "billing_notice_contacts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    merchant_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    destination_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    protected_destination = table.Column<string>(type: "text", nullable: false),
                    destination_hint = table.Column<string>(type: "text", nullable: false),
                    locale = table.Column<string>(type: "text", nullable: false),
                    approved_by_actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    operation_key = table.Column<Guid>(type: "uuid", nullable: false),
                    verification_token_hash = table.Column<byte[]>(type: "bytea", nullable: true),
                    verification_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    opted_in_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by_actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_billing_notice_contacts", x => x.id);
                    table.CheckConstraint("ck_billing_notice_contacts_consent", "revision >= 1 AND (opted_in_at IS NULL OR verified_at IS NOT NULL)");
                    table.CheckConstraint("ck_billing_notice_contacts_hashes", "octet_length(destination_hash) = 32\nAND (verification_token_hash IS NULL OR octet_length(verification_token_hash) = 32)\nAND length(btrim(protected_destination)) > 0\nAND length(btrim(destination_hint)) BETWEEN 3 AND 128");
                    table.CheckConstraint("ck_billing_notice_contacts_ids", "id <> '00000000-0000-0000-0000-000000000000'\nAND account_id <> '00000000-0000-0000-0000-000000000000'\nAND approved_by_actor_id <> '00000000-0000-0000-0000-000000000000'\nAND operation_key <> '00000000-0000-0000-0000-000000000000'\nAND (merchant_user_id IS NULL OR merchant_user_id <> '00000000-0000-0000-0000-000000000000')\nAND (revoked_by_actor_id IS NULL OR revoked_by_actor_id <> '00000000-0000-0000-0000-000000000000')");
                    table.CheckConstraint("ck_billing_notice_contacts_locale", "locale IN ('en-IN', 'hi-IN', 'ta-IN', 'te-IN', 'bn-IN', 'ml-IN', 'kn-IN', 'mr-IN', 'gu-IN')");
                    table.ForeignKey(
                        name: "fk_billing_notice_contacts_billing_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "billing_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "notification_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope_kind = table.Column<string>(type: "text", nullable: false),
                    scope_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_key = table.Column<string>(type: "text", nullable: false),
                    recipient_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    audience = table.Column<string>(type: "text", nullable: false),
                    purpose = table.Column<string>(type: "text", nullable: false),
                    source_kind = table.Column<string>(type: "text", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_revision = table.Column<string>(type: "text", nullable: false),
                    threshold = table.Column<string>(type: "text", nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_deadline_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    time_zone = table.Column<string>(type: "text", nullable: false),
                    locale = table.Column<string>(type: "text", nullable: false),
                    allowed_channels = table.Column<string[]>(type: "text[]", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    template_key = table.Column<string>(type: "text", nullable: false),
                    template_arguments = table.Column<string>(type: "jsonb", nullable: false),
                    protected_destination = table.Column<string>(type: "text", nullable: true),
                    destination_hint = table.Column<string>(type: "text", nullable: true),
                    planned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_jobs", x => x.id);
                    table.CheckConstraint("ck_notification_jobs_channels", "cardinality(allowed_channels) >= 1\nAND allowed_channels <@ ARRAY['InApp', 'Email']::text[]\nAND (audience <> 'ShopStaffOperational' OR allowed_channels = ARRAY['InApp']::text[])\nAND (audience <> 'CommercialContact' OR allowed_channels = ARRAY['Email']::text[])\nAND (NOT ('Email' = ANY(allowed_channels)) OR (protected_destination IS NOT NULL AND destination_hint IS NOT NULL))");
                    table.CheckConstraint("ck_notification_jobs_dates", "effective_deadline_at >= due_at AND time_zone = 'Asia/Kolkata'");
                    table.CheckConstraint("ck_notification_jobs_ids", "id <> '00000000-0000-0000-0000-000000000000'\nAND account_id <> '00000000-0000-0000-0000-000000000000'\nAND scope_id <> '00000000-0000-0000-0000-000000000000'\nAND source_id <> '00000000-0000-0000-0000-000000000000'\nAND (recipient_user_id IS NULL OR recipient_user_id <> '00000000-0000-0000-0000-000000000000')\nAND (contact_id IS NULL OR contact_id <> '00000000-0000-0000-0000-000000000000')");
                    table.CheckConstraint("ck_notification_jobs_locale", "locale IN ('en-IN', 'hi-IN', 'ta-IN', 'te-IN', 'bn-IN', 'ml-IN', 'kn-IN', 'mr-IN', 'gu-IN')");
                    table.CheckConstraint("ck_notification_jobs_purpose", "purpose IN ('TrialReminder', 'PaidRenewalReminder', 'GraceNotice')");
                    table.CheckConstraint("ck_notification_jobs_recipient_key", "length(btrim(recipient_key)) BETWEEN 1 AND 128");
                    table.CheckConstraint("ck_notification_jobs_scope", "(scope_kind = 'Account' AND scope_id = account_id AND audience IN ('CommercialBillingRole', 'CommercialContact'))\nOR (scope_kind = 'Shop' AND audience IN ('ShopOwnerOperational', 'ShopStaffOperational'))");
                    table.CheckConstraint("ck_notification_jobs_source", "source_kind IN ('Trial', 'Paid', 'Complimentary') AND length(btrim(source_revision)) BETWEEN 1 AND 128");
                    table.CheckConstraint("ck_notification_jobs_status", "status = 'Scheduled'");
                    table.CheckConstraint("ck_notification_jobs_template", "length(btrim(template_key)) BETWEEN 1 AND 128 AND jsonb_typeof(template_arguments) = 'object'");
                    table.CheckConstraint("ck_notification_jobs_threshold", "threshold IN (\n    'TrialMinus3Days', 'TrialMinus1Day',\n    'PaidMinus7Days', 'PaidMinus1Day',\n    'GraceStart', 'GraceMinus1Day', 'GraceExpiry')");
                    table.ForeignKey(
                        name: "fk_notification_jobs_billing_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "billing_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "billing_notice_contact_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_kind = table.Column<string>(type: "text", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_billing_notice_contact_events", x => x.id);
                    table.CheckConstraint("ck_billing_notice_contact_events_ids", "id <> '00000000-0000-0000-0000-000000000000'\nAND contact_id <> '00000000-0000-0000-0000-000000000000'\nAND account_id <> '00000000-0000-0000-0000-000000000000'\nAND (actor_id IS NULL OR actor_id <> '00000000-0000-0000-0000-000000000000')");
                    table.CheckConstraint("ck_billing_notice_contact_events_kind", "event_kind IN ('Approved', 'Verified', 'OptedIn', 'Revoked') AND revision >= 1");
                    table.ForeignKey(
                        name: "fk_billing_notice_contact_events_billing_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "billing_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_billing_notice_contact_events_billing_notice_contacts_conta",
                        column: x => x.contact_id,
                        principalTable: "billing_notice_contacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_billing_notice_contact_events_account_id",
                table: "billing_notice_contact_events",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ux_billing_notice_contact_events_revision",
                table: "billing_notice_contact_events",
                columns: ContactEventRevisionColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_billing_notice_contacts_account",
                table: "billing_notice_contacts",
                columns: ContactAccountColumns);

            migrationBuilder.CreateIndex(
                name: "ux_billing_notice_contacts_active_destination",
                table: "billing_notice_contacts",
                columns: ContactActiveDestinationColumns,
                unique: true,
                filter: "revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_billing_notice_contacts_active_user",
                table: "billing_notice_contacts",
                columns: ContactActiveUserColumns,
                unique: true,
                filter: "merchant_user_id IS NOT NULL AND revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_billing_notice_contacts_operation",
                table: "billing_notice_contacts",
                columns: ContactOperationColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notification_jobs_account_actor_due",
                table: "notification_jobs",
                columns: JobAccountActorDueColumns,
                filter: "scope_kind = 'Account' AND recipient_user_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_notification_jobs_account_due",
                table: "notification_jobs",
                columns: JobAccountDueColumns);

            migrationBuilder.CreateIndex(
                name: "ix_notification_jobs_shop_actor_due",
                table: "notification_jobs",
                columns: JobShopActorDueColumns,
                filter: "scope_kind = 'Shop' AND recipient_user_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_notification_jobs_semantic_key",
                table: "notification_jobs",
                columns: JobSemanticKeyColumns,
                unique: true);

            migrationBuilder.Sql("""
                CREATE FUNCTION public.refuse_notification_evidence_mutation() RETURNS trigger
                    LANGUAGE plpgsql
                    SET search_path TO 'pg_catalog'
                AS $$
                BEGIN
                    RAISE EXCEPTION 'Notification job and consent history are append-only' USING ERRCODE = '55000';
                END;
                $$;

                CREATE TRIGGER notification_jobs_immutable
                    BEFORE UPDATE OR DELETE OR TRUNCATE ON public.notification_jobs
                    FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_notification_evidence_mutation();

                CREATE TRIGGER billing_notice_contact_events_immutable
                    BEFORE UPDATE OR DELETE OR TRUNCATE ON public.billing_notice_contact_events
                    FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_notification_evidence_mutation();

                CREATE TRIGGER billing_notice_contacts_retain
                    BEFORE DELETE OR TRUNCATE ON public.billing_notice_contacts
                    FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_notification_evidence_mutation();

                ALTER TABLE public.notification_jobs ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.notification_jobs FORCE ROW LEVEL SECURITY;
                ALTER TABLE public.billing_notice_contacts ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.billing_notice_contacts FORCE ROW LEVEL SECURITY;
                ALTER TABLE public.billing_notice_contact_events ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.billing_notice_contact_events FORCE ROW LEVEL SECURITY;

                DO $$
                BEGIN
                    EXECUTE format(
                        'CREATE POLICY notification_jobs_owner ON public.notification_jobs TO %I USING (true) WITH CHECK (true)',
                        current_user);
                    EXECUTE format(
                        'CREATE POLICY billing_notice_contacts_owner ON public.billing_notice_contacts TO %I USING (true) WITH CHECK (true)',
                        current_user);
                    EXECUTE format(
                        'CREATE POLICY billing_notice_contact_events_owner ON public.billing_notice_contact_events TO %I USING (true) WITH CHECK (true)',
                        current_user);
                    EXECUTE format(
                        'CREATE POLICY notification_planner_discovery ON public.subscription_coverage_terms FOR SELECT TO %I USING (true)',
                        current_user);
                END;
                $$;

                CREATE POLICY notification_jobs_account_read ON public.notification_jobs
                    FOR SELECT
                    USING (account_id = NULLIF(current_setting('app.billing_account_id', true), '')::uuid);

                CREATE POLICY notification_jobs_shop_actor_read ON public.notification_jobs
                    FOR SELECT
                    USING (
                        scope_kind = 'Shop'
                        AND scope_id = NULLIF(current_setting('app.status_shop_id', true), '')::uuid
                        AND recipient_user_id = NULLIF(current_setting('app.billing_actor_id', true), '')::uuid);

                CREATE POLICY notification_jobs_account_insert ON public.notification_jobs
                    FOR INSERT
                    WITH CHECK (account_id = NULLIF(current_setting('app.billing_account_id', true), '')::uuid);

                CREATE POLICY billing_notice_contacts_account_scope ON public.billing_notice_contacts
                    FOR ALL
                    USING (account_id = NULLIF(current_setting('app.billing_account_id', true), '')::uuid)
                    WITH CHECK (account_id = NULLIF(current_setting('app.billing_account_id', true), '')::uuid);

                CREATE POLICY billing_notice_contacts_holder_read ON public.billing_notice_contacts
                    FOR SELECT
                    USING (merchant_user_id = NULLIF(current_setting('app.billing_actor_id', true), '')::uuid);

                CREATE POLICY billing_notice_contacts_holder_update ON public.billing_notice_contacts
                    FOR UPDATE
                    USING (merchant_user_id = NULLIF(current_setting('app.billing_actor_id', true), '')::uuid)
                    WITH CHECK (merchant_user_id = NULLIF(current_setting('app.billing_actor_id', true), '')::uuid);

                CREATE POLICY billing_notice_contact_events_account_read ON public.billing_notice_contact_events
                    FOR SELECT
                    USING (account_id = NULLIF(current_setting('app.billing_account_id', true), '')::uuid);

                CREATE POLICY billing_notice_contact_events_account_insert ON public.billing_notice_contact_events
                    FOR INSERT
                    WITH CHECK (account_id = NULLIF(current_setting('app.billing_account_id', true), '')::uuid);

                CREATE FUNCTION public.due_notification_planning(p_after_account uuid, p_limit integer)
                    RETURNS TABLE(account_id uuid)
                    LANGUAGE sql STABLE SECURITY DEFINER
                    SET search_path TO 'pg_catalog'
                AS $$
                    SELECT DISTINCT t.account_id
                    FROM public.subscription_coverage_terms t
                    WHERE (p_after_account IS NULL OR t.account_id > p_after_account)
                      AND nullif(current_setting('app.active_shop_id', true), '') IS NULL
                      AND nullif(current_setting('app.current_user_id', true), '') IS NULL
                      AND nullif(current_setting('app.billing_account_id', true), '') IS NULL
                      AND nullif(current_setting('app.entitlement_account_id', true), '') IS NULL
                      AND nullif(current_setting('app.status_shop_id', true), '') IS NULL
                    ORDER BY t.account_id
                    LIMIT LEAST(GREATEST(COALESCE(p_limit, 20), 1), 100)
                $$;

                REVOKE ALL ON FUNCTION public.due_notification_planning(uuid, integer) FROM PUBLIC;

                CREATE FUNCTION public.account_notification_billing_members(
                    p_account uuid,
                    p_after_user uuid,
                    p_limit integer)
                    RETURNS TABLE(id uuid, merchant_user_id uuid, role integer, revision integer)
                    LANGUAGE sql STABLE SECURITY DEFINER
                    SET search_path TO 'pg_catalog'
                AS $$
                    SELECT m.id, m.merchant_user_id, m.role, m.revision
                    FROM public.billing_account_members m
                    WHERE m.account_id = p_account
                      AND m.status = 1
                      AND m.role IN (1, 2)
                      AND (p_after_user IS NULL OR m.merchant_user_id > p_after_user)
                      AND p_account = nullif(current_setting('app.billing_account_id', true), '')::uuid
                      AND nullif(current_setting('app.billing_actor_id', true), '') IS NULL
                    ORDER BY m.merchant_user_id
                    LIMIT LEAST(GREATEST(COALESCE(p_limit, 50), 1), 100)
                $$;

                REVOKE ALL ON FUNCTION public.account_notification_billing_members(uuid, uuid, integer) FROM PUBLIC;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP FUNCTION IF EXISTS public.account_notification_billing_members(uuid, uuid, integer);
                DROP FUNCTION IF EXISTS public.due_notification_planning(uuid, integer);
                DROP POLICY IF EXISTS notification_planner_discovery ON public.subscription_coverage_terms;
                DROP TRIGGER IF EXISTS notification_jobs_immutable ON public.notification_jobs;
                DROP TRIGGER IF EXISTS billing_notice_contact_events_immutable ON public.billing_notice_contact_events;
                DROP TRIGGER IF EXISTS billing_notice_contacts_retain ON public.billing_notice_contacts;
                DROP FUNCTION IF EXISTS public.refuse_notification_evidence_mutation();
                """);

            migrationBuilder.DropTable(
                name: "billing_notice_contact_events");

            migrationBuilder.DropTable(
                name: "notification_jobs");

            migrationBuilder.DropTable(
                name: "billing_notice_contacts");
        }
    }
}
