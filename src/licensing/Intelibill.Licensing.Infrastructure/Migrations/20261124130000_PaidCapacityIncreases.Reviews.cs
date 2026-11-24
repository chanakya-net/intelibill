using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

public sealed partial class PaidCapacityIncreases
{
    private const string CapacityReasons = "'ReservationExpired','ReservationChanged','SourceChanged','ReservationBusy'";
    private const string OriginalClassifications = """
        'ReceivingPinMismatch','CurrencyMismatch','Underpayment','Overpayment','DuplicateFunds','CoverageChanged',
        'TermUnavailable','CaptureTimeUnproven','RestorationPending','LegacyNeedsReview'
        """;

    private const string ManualCapacityReason = """
        OR r.review_reason='CapacityPending' AND EXISTS (SELECT FROM public.capacity_increases i
            WHERE i.account_id=r.account_id AND i.order_id=r.order_id AND i.receipt_id=r.receipt_id
              AND i.state='NeedsReview' AND i.reason=NEW.classification)
        """;

    private static string CurrentDecision => $"""
        account_id={Account} AND EXISTS (SELECT FROM public.capacity_increases i
            WHERE i.account_id=unmatched_payments.account_id AND i.order_id=unmatched_payments.order_id
                AND i.state='NeedsReview' AND i.reason=unmatched_payments.classification AND i.xmin=pg_current_xact_id()::xid
                AND (i.receipt_id=unmatched_payments.manual_receipt_id OR i.attempt_id=unmatched_payments.attempt_id))
        """;

    private static void ConfigureReview(MigrationBuilder builder)
    {
        builder.Sql($"""
            ALTER TABLE public.unmatched_payments DROP CONSTRAINT unmatched_payments_classification_check,
                ADD CHECK (classification IN ({OriginalClassifications},{CapacityReasons}));
            CREATE POLICY capacity_review ON public.unmatched_payments FOR INSERT WITH CHECK ({CurrentDecision});
            CREATE POLICY capacity_reason_requires_decision ON public.unmatched_payments AS RESTRICTIVE FOR INSERT
                WITH CHECK (classification NOT IN ({CapacityReasons}) OR ({CurrentDecision}));
            CREATE POLICY capacity_review_event ON public.payment_events FOR INSERT WITH CHECK (account_id={Account}
                AND kind='PaidNeedsReview' AND EXISTS (SELECT FROM public.capacity_increases i
                    WHERE i.account_id=payment_events.account_id AND i.order_id=payment_events.order_id
                      AND i.attempt_id=payment_events.attempt_id AND i.state='NeedsReview' AND i.xmin=pg_current_xact_id()::xid));
            """);
        builder.Sql(UnmatchedPaymentReviews.SourceGuard(ManualCapacityReason));
    }

    private static void RestoreReview(MigrationBuilder builder)
    {
        builder.Sql(UnmatchedPaymentReviews.SourceGuard());
        builder.Sql($"""
            DROP POLICY capacity_review_event ON public.payment_events;
            DROP POLICY capacity_reason_requires_decision ON public.unmatched_payments;
            DROP POLICY capacity_review ON public.unmatched_payments;
            ALTER TABLE public.unmatched_payments DROP CONSTRAINT unmatched_payments_classification_check,
                ADD CHECK (classification IN ({OriginalClassifications}));
            """);
    }
}
