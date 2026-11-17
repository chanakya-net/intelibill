using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

[DbContext(typeof(LicensingDbContext))]
[Migration("20261117170000_PaymentProviderRouting")]
public sealed partial class PaymentProviderRouting : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(SelectionTables);
        migrationBuilder.Sql(ProfileBindings);
        migrationBuilder.Sql(RoutingEvents);
        migrationBuilder.Sql("""
            ALTER TABLE public.development_gateway_sessions
                ADD COLUMN provider_account_id varchar(80) NOT NULL DEFAULT 'local-simulator',
                ADD COLUMN environment varchar(20) NOT NULL DEFAULT 'Development' CHECK (environment = 'Development'),
                ADD COLUMN configuration_version varchar(80) NOT NULL DEFAULT 'development-simulator/1';
            """);
        migrationBuilder.Sql(SimulatorIsolation);
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DO $$ BEGIN
            IF EXISTS (SELECT FROM public.payment_provider_selections) OR EXISTS (SELECT FROM public.payment_provider_bindings) THEN
                RAISE EXCEPTION 'Retain payment routing profiles, identities and evidence on rollback' USING ERRCODE = '55000';
            END IF;
        END $$;
        DROP TABLE public.payment_routing_events, public.payment_provider_bindings;
        ALTER TABLE public.payment_attempts DROP COLUMN selection_revision;
        DROP TABLE public.payment_provider_routing, public.payment_provider_selections;
        DROP FUNCTION public.guard_payment_provider_selection();
        DROP POLICY pinned_identity ON public.development_gateway_controls;
        DROP POLICY pinned_identity ON public.development_gateway_payments;
        DROP POLICY pinned_identity ON public.development_gateway_sessions;
        ALTER TABLE public.development_gateway_sessions DROP COLUMN provider_account_id, DROP COLUMN environment, DROP COLUMN configuration_version;
        """);
}
