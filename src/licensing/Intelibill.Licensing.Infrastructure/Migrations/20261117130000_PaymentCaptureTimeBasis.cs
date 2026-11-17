using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

/// <summary>
/// Records how a verified payment's <c>captured_at</c> was established. A provider that proves the capture instant
/// is <c>ProviderCapture</c>; one that exposes only a creation time is <c>ObservedAtVerification</c>, where
/// <c>captured_at</c> is the verification instant (a no-later-than bound), never the provider's creation time.
/// Existing rows keep the simulator-proven basis they were written under.
/// </summary>
[DbContext(typeof(LicensingDbContext))]
[Migration("20261117130000_PaymentCaptureTimeBasis")]
public sealed class PaymentCaptureTimeBasis : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE public.verified_payments ADD COLUMN capture_time_basis text NOT NULL DEFAULT 'ProviderCapture'
            CHECK (capture_time_basis IN ('ProviderCapture', 'ObservedAtVerification'));
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DO $$ BEGIN
            IF EXISTS (SELECT FROM public.verified_payments WHERE capture_time_basis <> 'ProviderCapture') THEN
                RAISE EXCEPTION 'Capture-time provenance cannot be discarded' USING ERRCODE = '55000';
            END IF;
        END $$;
        ALTER TABLE public.verified_payments DROP COLUMN capture_time_basis;
        """);
}
