using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

/// <summary>
/// Lets a fresh proof-free provider read that proves the original order unpaid resolve earlier verification
/// uncertainty, so the same order can be offered again. Payment events gain an insertion sequence because event
/// instants can be equal; only a <c>VerificationRequested</c> after the latest <c>ResumeConfirmed</c> stays outstanding.
/// </summary>
[DbContext(typeof(LicensingDbContext))]
[Migration("20261117140000_PaymentResumeConfirmation")]
public sealed class PaymentResumeConfirmation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE public.payment_events ADD COLUMN sequence bigint GENERATED ALWAYS AS IDENTITY;
        ALTER TABLE public.payment_events ADD CONSTRAINT payment_events_sequence_key UNIQUE (sequence);
        ALTER TABLE public.payment_events DROP CONSTRAINT payment_events_kind_check,
            ADD CONSTRAINT payment_events_kind_check CHECK (kind IN ('AttemptStarted', 'SessionRecorded',
                'VerificationRequested', 'OrderFulfilled', 'PaidNeedsReview', 'CheckoutFailed', 'ResumeConfirmed'));
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DO $$ BEGIN
            IF EXISTS (SELECT FROM public.payment_events WHERE kind = 'ResumeConfirmed') THEN
                RAISE EXCEPTION 'Resume confirmations cannot be discarded' USING ERRCODE = '55000';
            END IF;
        END $$;
        ALTER TABLE public.payment_events DROP CONSTRAINT payment_events_kind_check,
            ADD CONSTRAINT payment_events_kind_check CHECK (kind IN ('AttemptStarted', 'SessionRecorded',
                'VerificationRequested', 'OrderFulfilled', 'PaidNeedsReview', 'CheckoutFailed'));
        ALTER TABLE public.payment_events DROP COLUMN sequence;
        """);
}
