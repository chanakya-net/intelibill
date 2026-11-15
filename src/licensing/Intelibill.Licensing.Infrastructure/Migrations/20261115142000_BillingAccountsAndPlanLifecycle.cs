using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Licensing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BillingAccountsAndPlanLifecycle : Migration
    {
        private static readonly string[] ActorColumns = ["merchant_user_id", "status", "created_at"];
        private static readonly string[] MemberColumns = ["account_id", "merchant_user_id"];
        private static readonly string[] CreationFenceColumns = ["created_by_merchant_user_id", "creation_key"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_merchant_billing_delegation_use_ids",
                table: "merchant_billing_delegation_uses");

            migrationBuilder.AlterColumn<Guid>(
                name: "account_id",
                table: "merchant_billing_delegation_uses",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.CreateTable(
                name: "billing_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    created_by_merchant_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creation_key = table.Column<Guid>(type: "uuid", nullable: false),
                    creation_request_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_billing_accounts", x => x.id);
                    table.CheckConstraint("ck_billing_accounts_creation_request_hash", "octet_length(creation_request_hash) = 32");
                    table.CheckConstraint("ck_billing_accounts_ids", "id <> '00000000-0000-0000-0000-000000000000'\nAND created_by_merchant_user_id <> '00000000-0000-0000-0000-000000000000'\nAND creation_key <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("ck_billing_accounts_name", "name = btrim(name) AND char_length(name) BETWEEN 1 AND 120");
                });

            migrationBuilder.CreateTable(
                name: "billing_account_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    merchant_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_billing_account_members", x => x.id);
                    table.CheckConstraint("ck_billing_account_members_ids", "id <> '00000000-0000-0000-0000-000000000000'\nAND merchant_user_id <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("ck_billing_account_members_revision", "revision >= 1");
                    table.CheckConstraint("ck_billing_account_members_role", "role IN (1, 2)");
                    table.CheckConstraint("ck_billing_account_members_status", "status IN (1, 2)");
                    table.ForeignKey(
                        name: "fk_billing_account_members_billing_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "billing_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_merchant_billing_delegation_use_ids",
                table: "merchant_billing_delegation_uses",
                sql: "replay_id <> '00000000-0000-0000-0000-000000000000'\nAND (account_id IS NULL OR account_id <> '00000000-0000-0000-0000-000000000000')\nAND actor_id <> '00000000-0000-0000-0000-000000000000'\nAND request_id <> '00000000-0000-0000-0000-000000000000'\nAND request_id <> replay_id\nAND key_id ~ '^[A-Za-z0-9_-]{1,128}$'");

            migrationBuilder.CreateIndex(
                name: "ix_billing_account_members_actor",
                table: "billing_account_members",
                columns: ActorColumns);

            migrationBuilder.CreateIndex(
                name: "ux_billing_account_members_active_member",
                table: "billing_account_members",
                columns: MemberColumns,
                unique: true,
                filter: "status = 1");

            migrationBuilder.CreateIndex(
                name: "ux_billing_account_members_active_owner",
                table: "billing_account_members",
                column: "account_id",
                unique: true,
                filter: "role = 1 AND status = 1");

            migrationBuilder.CreateIndex(
                name: "ix_billing_accounts_created_by_merchant_user_id_creation_key",
                table: "billing_accounts",
                columns: CreationFenceColumns,
                unique: true);
            migrationBuilder.Sql(RowSecurity);
            migrationBuilder.Sql(ActorScopedReplayPolicies);
        }

        // Account context and actor come only from the delegation transaction (set_config(..., true)).
        // Without an account context no account row is visible; without an actor nothing is.
        private const string RowSecurity = """
            ALTER TABLE public.billing_accounts ENABLE ROW LEVEL SECURITY;
            ALTER TABLE public.billing_accounts FORCE ROW LEVEL SECURITY;
            ALTER TABLE public.billing_account_members ENABLE ROW LEVEL SECURITY;
            ALTER TABLE public.billing_account_members FORCE ROW LEVEL SECURITY;

            CREATE POLICY billing_accounts_read ON public.billing_accounts FOR SELECT
            USING (
                id = nullif(current_setting('app.billing_account_id', true), '')::uuid
                AND EXISTS (
                    SELECT FROM public.billing_account_members member
                    WHERE member.account_id = billing_accounts.id AND member.status = 1
                      AND member.merchant_user_id = nullif(current_setting('app.billing_actor_id', true), '')::uuid)
            );

            CREATE POLICY billing_accounts_insert ON public.billing_accounts FOR INSERT
            WITH CHECK (
                id = nullif(current_setting('app.billing_account_id', true), '')::uuid
                AND created_by_merchant_user_id = nullif(current_setting('app.billing_actor_id', true), '')::uuid
            );

            -- The actor's own membership index is the only surface without an account context.
            CREATE POLICY billing_account_members_read ON public.billing_account_members FOR SELECT
            USING (
                merchant_user_id = nullif(current_setting('app.billing_actor_id', true), '')::uuid
                AND (nullif(current_setting('app.billing_account_id', true), '') IS NULL
                    OR account_id = nullif(current_setting('app.billing_account_id', true), '')::uuid)
            );

            CREATE POLICY billing_account_members_insert_owner ON public.billing_account_members FOR INSERT
            WITH CHECK (
                account_id = nullif(current_setting('app.billing_account_id', true), '')::uuid
                AND merchant_user_id = nullif(current_setting('app.billing_actor_id', true), '')::uuid
                AND role = 1 AND status = 1 AND revision = 1
            );

            -- Lock-only: lets the authority take FOR SHARE on its own row and refuses every real update.
            CREATE POLICY billing_account_members_lock ON public.billing_account_members FOR UPDATE
            USING (
                account_id = nullif(current_setting('app.billing_account_id', true), '')::uuid
                AND merchant_user_id = nullif(current_setting('app.billing_actor_id', true), '')::uuid
            )
            WITH CHECK (false);

            CREATE FUNCTION public.refuse_billing_account_evidence_loss() RETURNS trigger
            LANGUAGE plpgsql AS $$
            BEGIN
                RAISE EXCEPTION 'Billing account facts are retained' USING ERRCODE = '55000';
            END $$;

            CREATE TRIGGER billing_accounts_retained
                BEFORE UPDATE OR DELETE OR TRUNCATE ON public.billing_accounts
                FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
            CREATE TRIGGER billing_account_members_retained
                BEFORE DELETE OR TRUNCATE ON public.billing_account_members
                FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
            """;

        // Actor-scoped uses (account creation and listing) have no account; both scopes still need the actor.
        private const string ActorScopedReplayPolicies = """
            DROP POLICY billing_delegation_uses_read ON public.merchant_billing_delegation_uses;
            DROP POLICY billing_delegation_uses_insert ON public.merchant_billing_delegation_uses;

            CREATE POLICY billing_delegation_uses_read ON public.merchant_billing_delegation_uses FOR SELECT
            USING (
                account_id IS NOT DISTINCT FROM nullif(current_setting('app.billing_account_id', true), '')::uuid
                AND actor_id = nullif(current_setting('app.billing_actor_id', true), '')::uuid
            );

            CREATE POLICY billing_delegation_uses_insert ON public.merchant_billing_delegation_uses FOR INSERT
            WITH CHECK (
                account_id IS NOT DISTINCT FROM nullif(current_setting('app.billing_account_id', true), '')::uuid
                AND actor_id = nullif(current_setting('app.billing_actor_id', true), '')::uuid
            );
            """;

        private const string AccountScopedReplayPolicies = """
            DROP POLICY billing_delegation_uses_read ON public.merchant_billing_delegation_uses;
            DROP POLICY billing_delegation_uses_insert ON public.merchant_billing_delegation_uses;

            CREATE POLICY billing_delegation_uses_read ON public.merchant_billing_delegation_uses FOR SELECT
            USING (
                account_id = nullif(current_setting('app.billing_account_id', true), '')::uuid
                AND actor_id = nullif(current_setting('app.billing_actor_id', true), '')::uuid
            );

            CREATE POLICY billing_delegation_uses_insert ON public.merchant_billing_delegation_uses FOR INSERT
            WITH CHECK (
                account_id = nullif(current_setting('app.billing_account_id', true), '')::uuid
                AND actor_id = nullif(current_setting('app.billing_actor_id', true), '')::uuid
            );
            """;

        // Rolling back would drop accounts, owners or actor-scoped uses. Refuse unless none exist.
        private const string RefuseEvidenceLoss = """
            LOCK TABLE public.billing_accounts, public.billing_account_members,
                public.merchant_billing_delegation_uses IN ACCESS EXCLUSIVE MODE;
            ALTER TABLE public.billing_accounts NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE public.billing_account_members NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE public.merchant_billing_delegation_uses NO FORCE ROW LEVEL SECURITY;
            DO $$
            BEGIN
                IF EXISTS (SELECT FROM public.billing_accounts)
                    OR EXISTS (SELECT FROM public.billing_account_members)
                    OR EXISTS (SELECT FROM public.merchant_billing_delegation_uses WHERE account_id IS NULL) THEN
                    RAISE EXCEPTION 'Billing account evidence exists' USING ERRCODE = '55000';
                END IF;
            END $$;
            ALTER TABLE public.merchant_billing_delegation_uses FORCE ROW LEVEL SECURITY;
            -- The account read policy refers to the members table; drop it before the tables.
            DROP POLICY billing_accounts_read ON public.billing_accounts;
            """;

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RefuseEvidenceLoss);
            migrationBuilder.DropTable(
                name: "billing_account_members");

            migrationBuilder.DropTable(
                name: "billing_accounts");

            migrationBuilder.Sql("DROP FUNCTION public.refuse_billing_account_evidence_loss()");
            migrationBuilder.Sql(AccountScopedReplayPolicies);

            migrationBuilder.DropCheckConstraint(
                name: "ck_merchant_billing_delegation_use_ids",
                table: "merchant_billing_delegation_uses");

            migrationBuilder.AlterColumn<Guid>(
                name: "account_id",
                table: "merchant_billing_delegation_uses",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_merchant_billing_delegation_use_ids",
                table: "merchant_billing_delegation_uses",
                sql: "replay_id <> '00000000-0000-0000-0000-000000000000'\nAND account_id <> '00000000-0000-0000-0000-000000000000'\nAND actor_id <> '00000000-0000-0000-0000-000000000000'\nAND request_id <> '00000000-0000-0000-0000-000000000000'\nAND request_id <> replay_id\nAND key_id ~ '^[A-Za-z0-9_-]{1,128}$'");
        }
    }
}
