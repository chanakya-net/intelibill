using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Licensing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMerchantBillingDelegationUses : Migration
    {
        private static readonly string[] ScopeColumns = ["account_id", "actor_id"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "merchant_billing_delegation_uses",
                columns: table => new
                {
                    issuer = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    replay_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_merchant_billing_delegation_uses", x => new { x.issuer, x.replay_id });
                    table.CheckConstraint("ck_merchant_billing_delegation_use_ids", "replay_id <> '00000000-0000-0000-0000-000000000000'\nAND account_id <> '00000000-0000-0000-0000-000000000000'\nAND actor_id <> '00000000-0000-0000-0000-000000000000'\nAND request_id <> '00000000-0000-0000-0000-000000000000'\nAND request_id <> replay_id\nAND key_id ~ '^[A-Za-z0-9_-]{1,128}$'");
                    table.CheckConstraint("ck_merchant_billing_delegation_use_window", "issued_at <= consumed_at AND consumed_at < expires_at\nAND expires_at - issued_at <= interval '60 seconds'");
                });

            migrationBuilder.CreateIndex(
                name: "ix_merchant_billing_delegation_uses_account_id_actor_id",
                table: "merchant_billing_delegation_uses",
                columns: ScopeColumns);
            migrationBuilder.Sql("""
                ALTER TABLE public.merchant_billing_delegation_uses ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.merchant_billing_delegation_uses FORCE ROW LEVEL SECURITY;

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

                CREATE FUNCTION public.refuse_billing_delegation_use_mutation() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Merchant billing delegation use is immutable' USING ERRCODE = '55000';
                END $$;

                CREATE TRIGGER merchant_billing_delegation_use_immutable
                    BEFORE UPDATE OR DELETE OR TRUNCATE ON public.merchant_billing_delegation_uses
                    FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_delegation_use_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE public.merchant_billing_delegation_uses IN ACCESS EXCLUSIVE MODE;
                ALTER TABLE public.merchant_billing_delegation_uses NO FORCE ROW LEVEL SECURITY;
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM public.merchant_billing_delegation_uses) THEN
                        RAISE EXCEPTION 'Merchant billing delegation evidence exists' USING ERRCODE = '55000';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "merchant_billing_delegation_uses");
            migrationBuilder.Sql("DROP FUNCTION public.refuse_billing_delegation_use_mutation()");
        }
    }
}
