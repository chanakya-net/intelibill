namespace Intelibill.Integration.Tests;

internal static class TransferMigrationOwnership
{
    // These tests migrate as a non-superuser schema owner against a database cloned from the latest template.
    // Include the consent and transfer objects the migrator has to remove before reaching the older retention fence.
    internal static string Sql(string role) => $"""
        ALTER TABLE public.coverage_transfer_consent_events OWNER TO {role};
        ALTER TABLE public.merchant_transfer_decisions OWNER TO {role};
        GRANT SELECT ON public.shop_affiliation_consent_events, public.shop_affiliation_delivery_receipts,
            public.entitlement_reconciliation_requests, public.entitlement_outbox TO {role};
        ALTER VIEW public.merchant_entitlement_bindings OWNER TO {role};
        ALTER FUNCTION public.coverage_transfer_shop_inspect(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint,bigint,text,timestamptz) OWNER TO {role};
        ALTER FUNCTION public.guard_transfer_permit() OWNER TO {role};
        ALTER FUNCTION public.transfer_source_cutoff(uuid,uuid) OWNER TO {role};
        ALTER FUNCTION public.original_licence_proof_issuance(uuid) OWNER TO {role};
        ALTER FUNCTION public.merchant_licence_proof_issuance(uuid) OWNER TO {role};
        ALTER FUNCTION public.due_merchant_entitlement_reconciliation(timestamptz) OWNER TO {role};
        """;
}
