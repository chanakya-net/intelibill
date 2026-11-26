using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

/// <summary>
/// P06-12: Allows live Billing Owners and Managers to read accounting documents for their own account only inside
/// a transaction that has consumed a matching billing-document delegation use. Leaves finance_read untouched.
/// </summary>
[DbContext(typeof(LicensingDbContext))]
[Migration("20261126120000_BillingDocumentMemberRead")]
public sealed class BillingDocumentMemberRead : Migration
{
    private const string Account = "nullif(current_setting('app.billing_account_id', true), '')::uuid";
    private const string Actor = "nullif(current_setting('app.billing_actor_id', true), '')::uuid";

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql($"""
        CREATE POLICY member_read ON public.billing_document_versions FOR SELECT USING (
            account_id = {Account}
            AND EXISTS (
                SELECT FROM public.billing_account_members m
                WHERE m.account_id = billing_document_versions.account_id
                    AND m.merchant_user_id = {Actor}
                    AND m.status = 1
                    AND m.role IN (1, 2))
            AND EXISTS (
                SELECT FROM public.merchant_billing_delegation_uses u
                WHERE u.account_id = billing_document_versions.account_id
                    AND u.actor_id = {Actor}
                    AND u.action IN ('billing.document.list', 'billing.document.download')
                    AND u.xmin = pg_current_xact_id()::xid));
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP POLICY member_read ON public.billing_document_versions;
        """);
}
