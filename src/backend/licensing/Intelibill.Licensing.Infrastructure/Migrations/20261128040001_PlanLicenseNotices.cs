using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Licensing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PlanLicenseNotices : Migration
    {
        private static readonly string[] ContactEventRevisionColumns = ["contact_id", "revision", "event_kind"];
        private static readonly string[] ContactAccountColumns = ["account_id", "approved_at", "id"];
        private static readonly string[] ContactActiveDestinationColumns = ["account_id", "destination_hash"];
        private static readonly string[] ContactActiveUserColumns = ["account_id", "merchant_user_id"];
        private static readonly string[] ContactOperationColumns = ["account_id", "operation_key"];
        private static readonly string[] JobAccountActorDueColumns = ["account_id", "recipient_user_id", "due_at", "id"];
        private static readonly string[] JobAccountDueColumns = ["account_id", "due_at", "id"];
        private static readonly string[] JobShopActorDueColumns = ["scope_id", "recipient_user_id", "due_at", "id"];
        private static readonly string[] JobEmailDestinationColumns =
        [
            "account_id", "scope_kind", "scope_id", "destination_hash", "purpose",
            "source_kind", "source_id", "source_revision", "threshold",
        ];
        private static readonly string[] JobSemanticKeyColumns =
        [
            "account_id", "scope_kind", "scope_id", "recipient_key", "purpose",
            "source_kind", "source_id", "source_revision", "threshold",
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            CreateContacts(migrationBuilder);
            CreateJobs(migrationBuilder);
            CreateConsentEvents(migrationBuilder);
            CreateContactOperations(migrationBuilder);
            IndexContacts(migrationBuilder);
            IndexJobs(migrationBuilder);
            RetainNoticeEvidence(migrationBuilder);
            EnableNoticeIsolation(migrationBuilder);
            AddNoticePolicies(migrationBuilder);
            AddPlannerContext(migrationBuilder);
            AddPlannerDiscovery(migrationBuilder);
            AddPlannerMembers(migrationBuilder);
            AddChannelEvidence(migrationBuilder);
        }

        private static void CreateContacts(MigrationBuilder migrationBuilder)
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
        }

        private static void CreateJobs(MigrationBuilder migrationBuilder)
        {
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
                    destination_hash = table.Column<byte[]>(type: "bytea", nullable: true),
                    protected_destination = table.Column<string>(type: "text", nullable: true),
                    destination_hint = table.Column<string>(type: "text", nullable: true),
                    planned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_jobs", x => x.id);
                    table.CheckConstraint("ck_notification_jobs_channels", "cardinality(allowed_channels) >= 1\nAND allowed_channels <@ ARRAY['InApp', 'Email']::text[]\nAND (audience <> 'ShopStaffOperational' OR allowed_channels = ARRAY['InApp']::text[])\nAND (audience <> 'CommercialContact' OR allowed_channels = ARRAY['Email']::text[])\nAND (NOT ('Email' = ANY(allowed_channels)) OR (\n    protected_destination IS NOT NULL\n    AND destination_hint IS NOT NULL\n    AND destination_hash IS NOT NULL\n    AND octet_length(destination_hash) = 32))\nAND ('Email' = ANY(allowed_channels) OR destination_hash IS NULL)");
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
        }

        private static void CreateConsentEvents(MigrationBuilder migrationBuilder)
        {
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
                    table.CheckConstraint("ck_billing_notice_contact_events_kind", "event_kind IN ('Approved', 'Verified', 'OptedIn', 'OptedOut', 'Revoked') AND revision >= 1");
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
        }

        private static void CreateContactOperations(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "billing_notice_contact_operations",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    operation_key = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payload_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    resulting_revision = table.Column<int>(type: "integer", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_billing_notice_contact_operations", x => new { x.account_id, x.action, x.operation_key });
                    table.CheckConstraint("ck_billing_notice_contact_operations_action", "action IN ('Approve', 'SelfConsent', 'StandaloneConsent', 'OwnerRevoke', 'HolderRevoke')\nAND octet_length(payload_hash) = 32\nAND resulting_revision >= 1");
                    table.CheckConstraint("ck_billing_notice_contact_operations_ids", "account_id <> '00000000-0000-0000-0000-000000000000'\nAND contact_id <> '00000000-0000-0000-0000-000000000000'\nAND operation_key <> '00000000-0000-0000-0000-000000000000'\nAND (actor_id IS NULL OR actor_id <> '00000000-0000-0000-0000-000000000000')");
                    table.ForeignKey(
                        name: "fk_billing_notice_contact_operations_billing_accounts_account_",
                        column: x => x.account_id,
                        principalTable: "billing_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_billing_notice_contact_operations_billing_notice_contacts_c",
                        column: x => x.contact_id,
                        principalTable: "billing_notice_contacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });
        }

        private static void IndexContacts(MigrationBuilder migrationBuilder)
        {
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
                name: "ix_billing_notice_contact_operations_contact_id",
                table: "billing_notice_contact_operations",
                column: "contact_id");

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
        }

        private static void IndexJobs(MigrationBuilder migrationBuilder)
        {
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
                name: "ux_notification_jobs_email_destination",
                table: "notification_jobs",
                columns: JobEmailDestinationColumns,
                unique: true,
                filter: "destination_hash IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_notification_jobs_semantic_key",
                table: "notification_jobs",
                columns: JobSemanticKeyColumns,
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP VIEW public.effective_notification_jobs;
                DROP TABLE public.notification_channel_additions;
                ALTER TABLE public.notification_jobs DROP CONSTRAINT uq_notification_job_account;
                DROP FUNCTION IF EXISTS public.account_notification_billing_members(uuid, uuid, integer);
                DROP FUNCTION IF EXISTS public.due_notification_planning(timestamp with time zone, uuid, integer);
                DROP FUNCTION IF EXISTS public.notification_planner_context(uuid);
                DROP POLICY IF EXISTS notification_planner_discovery ON public.paid_fresh_starts;
                DROP POLICY IF EXISTS notification_planner_discovery ON public.payment_fulfilments;
                DROP POLICY IF EXISTS notification_planner_discovery ON public.operational_closures;
                DROP POLICY IF EXISTS notification_planner_discovery ON public.complimentary_grants;
                DROP POLICY IF EXISTS notification_planner_discovery ON public.payment_trial_conversions;
                DROP POLICY IF EXISTS notification_planner_discovery ON public.trial_extensions;
                DROP POLICY IF EXISTS notification_planner_discovery ON public.trial_history;
                DROP POLICY IF EXISTS notification_planner_discovery ON public.subscription_term_facts;
                DROP POLICY IF EXISTS notification_planner_discovery ON public.subscription_coverage_terms;
                DROP TRIGGER IF EXISTS notification_jobs_immutable ON public.notification_jobs;
                DROP TRIGGER IF EXISTS billing_notice_contact_events_immutable ON public.billing_notice_contact_events;
                DROP TRIGGER IF EXISTS billing_notice_contact_operations_immutable ON public.billing_notice_contact_operations;
                DROP TRIGGER IF EXISTS billing_notice_contacts_retain ON public.billing_notice_contacts;
                DROP FUNCTION IF EXISTS public.refuse_notification_evidence_mutation();
                """);

            migrationBuilder.DropTable(
                name: "billing_notice_contact_events");

            migrationBuilder.DropTable(
                name: "billing_notice_contact_operations");

            migrationBuilder.DropTable(
                name: "notification_jobs");

            migrationBuilder.DropTable(
                name: "billing_notice_contacts");
        }
    }
}
