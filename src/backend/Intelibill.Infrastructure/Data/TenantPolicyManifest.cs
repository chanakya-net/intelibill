namespace Intelibill.Infrastructure.Data;

/// <summary>
/// How one table is treated by the tenant boundary.
/// </summary>
public enum TenantSecurityClassification
{
    /// <summary>Merchant data carrying its own <c>shop_id</c>: isolated by a policy on that column.</summary>
    TenantBusinessData,

    /// <summary>A line table with no <c>shop_id</c> of its own: isolated through the parent it hangs off.</summary>
    TenantChildData,

    /// <summary>
    /// Accounts, sessions, credentials and shop identity. Exempt: these are read
    /// and written with no usable shop context, and a shop-scoped predicate on
    /// any of them breaks login, switching or invitation acceptance.
    /// </summary>
    GlobalIdentityData,

    /// <summary>Reference data shared by every shop. Exempt: it is owned by no tenant.</summary>
    GlobalReferenceData,
}

/// <summary>
/// One row per mapped table, naming the classification and — for tenant tables
/// — the policy that enforces it and the index its predicate depends on.
/// </summary>
/// <param name="Table">Physical table name.</param>
/// <param name="Classification">How the tenant boundary treats this table.</param>
/// <param name="PolicyName">Row-level security policy, or <c>null</c> for an exempt table.</param>
/// <param name="PredicateColumn">Column the policy compares, or <c>null</c> for an exempt table.</param>
/// <param name="ParentTable">For child data, the table whose <c>shop_id</c> decides ownership.</param>
/// <param name="RequiredIndexColumn">Leading index column the predicate needs, since it runs on every row touched.</param>
/// <param name="ExemptionReason">Why an exempt table is exempt. Required for exempt tables, absent for tenant tables.</param>
public sealed record TenantPolicyEntry(
    string Table,
    TenantSecurityClassification Classification,
    string? PolicyName,
    string? PredicateColumn,
    string? ParentTable,
    string? RequiredIndexColumn,
    string? ExemptionReason)
{
    public bool IsTenantTable =>
        Classification is TenantSecurityClassification.TenantBusinessData
            or TenantSecurityClassification.TenantChildData;
}

/// <summary>
/// The checked-in classification of every mapped table. It exists so that a
/// table added by a future migration cannot quietly arrive without a decision:
/// the manifest is compared for exact set equality against the EF model, and a
/// new table fails the build's test run until someone classifies it here.
/// </summary>
/// <remarks>
/// The manifest describes intent. The catalog is the authority on what is
/// actually enforced, which is why the tests read <c>pg_class</c>,
/// <c>pg_policy</c> and <c>pg_index</c> rather than trusting this file.
/// </remarks>
public static class TenantPolicyManifest
{
    private const string PolicySuffix = "_shop_isolation";

    /// <summary>
    /// Tables that exist in the database but in no EF entity, so they are not
    /// part of the model comparison. Both are exempt.
    /// </summary>
    public static IReadOnlyList<TenantPolicyEntry> UnmappedInfrastructureTables { get; } =
    [
        Exempt(
            "cache_entries",
            TenantSecurityClassification.GlobalReferenceData,
            "The distributed cache. It must stay exempt while it shares the process NpgsqlDataSource: cache connections are opened without the EF interceptor, so no session context is set on them and a policy would reject every read. Per-tenant cache keying is owned elsewhere."),
        Exempt(
            "__EFMigrationsHistory",
            TenantSecurityClassification.GlobalReferenceData,
            "Schema bookkeeping written by the migration job under the owner identity."),
    ];

    public static IReadOnlyList<TenantPolicyEntry> Entries { get; } =
    [
        // ── Tenant business data: isolated on its own shop_id ────────────────
        Tenant("bank_accounts"),
        Tenant("batch_price_revisions"),
        Tenant("credit_note_redemptions"),
        Tenant("credit_notes"),
        Tenant("customer_ledger_entries"),
        Tenant("customers"),
        Tenant("discount_rules"),
        Tenant("expense_categories"),
        Tenant("expenses"),
        Tenant("inventory"),
        Tenant("inventory_adjustments"),
        Tenant("inventory_batches"),
        Tenant("invoice_leases"),
        Tenant("invoice_sequences"),
        Tenant("item_barcode_sequences"),
        Tenant("items"),
        Tenant("offline_submissions"),
        Tenant("operation_receipts"),
        Tenant("payment_allocations"),
        Tenant("payment_receipts"),
        Tenant("payment_reversals"),
        Tenant("posting_period_events"),
        Tenant("posting_periods"),
        Tenant("purchase_order_receipt_sequences"),
        Tenant("purchase_order_receipts"),
        Tenant("purchase_receipt_cost_adjustments"),
        Tenant("purchase_receipt_values"),
        Tenant("purchase_receipt_reversals"),
        Tenant("supplier_returns"),
        Tenant("supplier_return_lines"),
        Tenant("supplier_return_credits"),
        Tenant("supplier_return_credit_lines"),
        Tenant("purchase_order_sequences"),
        Tenant("purchase_orders"),
        Tenant("reconciliation_case_events"),
        Tenant("reconciliation_issues"),
        Tenant("sale_cancellation_lines"),
        Tenant("sale_cancellations"),
        Tenant("sale_items"),
        Tenant("sale_return_items"),
        Tenant("sale_return_settlements"),
        Tenant("sale_returns"),
        Tenant("sales"),
        Tenant("service_code_sequences"),
        Tenant("services"),
        Tenant("stock_counts"),
        Tenant("stock_transactions"),
        Tenant("supplier_ledger_entries"),
        Tenant("suppliers"),

        // ── Tenant child data: ownership derived from the parent ─────────────
        Child("purchase_order_lines", "purchase_order_id", "purchase_orders"),
        Child("purchase_order_receipt_lines", "purchase_order_receipt_id", "purchase_order_receipts"),

        // ── Global identity data: exempt, by decision ────────────────────────
        Exempt(
            "users",
            TenantSecurityClassification.GlobalIdentityData,
            "Enrolment, login, refresh and token validation all read and write the account row with no shop context; an account belongs to no shop and may hold memberships in several."),
        Exempt(
            "shops",
            TenantSecurityClassification.GlobalIdentityData,
            "Read before any shop is active — by the picker, and by shop creation before the rescope that makes the new shop current."),
        Exempt(
            "shop_memberships",
            TenantSecurityClassification.GlobalIdentityData,
            "Carries shop_id and is exempt anyway, deliberately: switching writes the target shop's row while the request is still scoped to the previous one, the picker must read every row or no second shop can ever be chosen, and invitation acceptance must lock the inviter's row in a shop the actor has not joined. A self-referencing policy would also recurse. Guarded instead by the authority-lock order and the live per-request membership re-check."),
        Exempt(
            "shop_invitations",
            TenantSecurityClassification.GlobalIdentityData,
            "Carries shop_id and is exempt anyway, deliberately: the invited account is not a member of the shop yet, so no shop-scoped predicate can admit the row it has to read to accept."),
        Exempt(
            "auth_sessions",
            TenantSecurityClassification.GlobalIdentityData,
            "Read by LiveSessionValidator inside OnTokenValidated, before HttpContext.User is assigned, so the session context is empty on every authenticated request."),
        Exempt(
            "refresh_tokens",
            TenantSecurityClassification.GlobalIdentityData,
            "Rotation and sign-out present a refresh credential and no access token, so there is no shop claim to scope by."),
        Exempt(
            "password_reset_tokens",
            TenantSecurityClassification.GlobalIdentityData,
            "Recovery runs for an account that cannot authenticate; there is no session and therefore no shop."),
        Exempt(
            "phone_challenges",
            TenantSecurityClassification.GlobalIdentityData,
            "Phone enrolment and phone login run before any session exists."),
        Exempt(
            "email_ownership_challenges",
            TenantSecurityClassification.GlobalIdentityData,
            "Address ownership is proved before a session exists, or across a change of the address a session is keyed to."),
        Exempt(
            "identity_mail",
            TenantSecurityClassification.GlobalIdentityData,
            "Delivery outbox drained by a background worker: no request, no token, no shop context."),
        Exempt(
            "identity_sms",
            TenantSecurityClassification.GlobalIdentityData,
            "Delivery outbox drained by a background worker: no request, no token, no shop context."),
        Exempt(
            "realtime_connection_leases",
            TenantSecurityClassification.GlobalIdentityData,
            "Carries shop_id and is exempt anyway, deliberately: neither reader has a shop context to be scoped by. Outbound dispatch runs on the Wolverine local queue and the revocation sweep runs on a background worker, so in both there is no HttpContext, app.active_shop_id is empty, and a shop-scoped predicate would hide the candidate rows from the dispatcher and the closable rows from the sweep — realtime would stop delivering anything at all. The row holds no credential and no merchant data: a connection id, the process holding it, and the session and version numbers a live authority check needs. Guarded instead by re-checking that live authority before every outbound payload, which is the control the group broadcast it replaced never had."),

        // ── Global reference data: exempt ────────────────────────────────────
        Exempt(
            "hsn_cache",
            TenantSecurityClassification.GlobalReferenceData,
            "Public HSN tax reference, identical for every shop and populated from an external lookup rather than by any merchant."),
    ];

    /// <summary>Tables a policy must exist on.</summary>
    public static IReadOnlyList<TenantPolicyEntry> TenantTables { get; } =
        Entries.Where(entry => entry.IsTenantTable).ToList();

    /// <summary>Tables that must stay outside row-level security.</summary>
    public static IReadOnlyList<TenantPolicyEntry> ExemptTables { get; } =
        Entries.Where(entry => !entry.IsTenantTable).ToList();

    private static TenantPolicyEntry Tenant(string table) => new(
        table,
        TenantSecurityClassification.TenantBusinessData,
        table + PolicySuffix,
        "shop_id",
        ParentTable: null,
        RequiredIndexColumn: "shop_id",
        ExemptionReason: null);

    private static TenantPolicyEntry Child(string table, string parentColumn, string parentTable) => new(
        table,
        TenantSecurityClassification.TenantChildData,
        table + PolicySuffix,
        parentColumn,
        parentTable,
        RequiredIndexColumn: parentColumn,
        ExemptionReason: null);

    private static TenantPolicyEntry Exempt(string table, TenantSecurityClassification classification, string reason) => new(
        table,
        classification,
        PolicyName: null,
        PredicateColumn: null,
        ParentTable: null,
        RequiredIndexColumn: null,
        ExemptionReason: reason);
}
