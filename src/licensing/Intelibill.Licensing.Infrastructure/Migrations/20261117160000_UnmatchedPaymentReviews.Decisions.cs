namespace Intelibill.Licensing.Infrastructure.Migrations;

public sealed partial class UnmatchedPaymentReviews
{
    private const string Decisions = """
        CREATE TABLE public.payment_review_decisions (
            account_id uuid NOT NULL, case_id uuid NOT NULL, operation_key uuid NOT NULL,
            admin_id uuid NOT NULL REFERENCES public.admin_identities(id), session_id uuid NOT NULL,
            action text NOT NULL, disposition text NOT NULL,
            mfa_verified_at timestamptz NOT NULL CHECK (isfinite(mfa_verified_at)),
            decided_at timestamptz NOT NULL CHECK (isfinite(decided_at)),
            protected_payload text NOT NULL CHECK (length(protected_payload) BETWEEN 1 AND 8192),
            term_id uuid, accepted_source_hash text, reviewed_source_hash text, recovery_policy text,
            PRIMARY KEY (account_id, case_id),
            UNIQUE (account_id, operation_key),
            FOREIGN KEY (account_id, case_id) REFERENCES public.unmatched_payments(account_id, case_id),
            CHECK ((action, disposition) IN (('RecordNoAccessDecision', 'NoAccessDecisionRecorded'),
                ('ReferForRefundReview', 'RefundReviewPending'), ('RecoverAcceptedTerms', 'AcceptedTermsRecovered'))),
            CHECK ((action = 'RecoverAcceptedTerms') = (term_id IS NOT NULL)),
            CHECK ((term_id IS NULL AND accepted_source_hash IS NULL AND reviewed_source_hash IS NULL AND recovery_policy IS NULL) OR
                (term_id IS NOT NULL AND accepted_source_hash IS NOT NULL AND reviewed_source_hash IS NOT NULL
                    AND recovery_policy IS NOT NULL AND recovery_policy = 'accepted-source-equivalence/1'))
        );
        """;
}
