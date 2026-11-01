# Tenant policy matrix

Every mapped table, and how the tenant boundary treats it. Generated from
`src/backend/Intelibill.Infrastructure/Data/TenantPolicyManifest.cs`, which is
compared for exact set equality against the EF model by
`TenantPolicyManifestTests.EveryMappedTable_HasExplicitSecurityClassification`:
a table added by a future migration fails that test until it is classified here.
The manifest states intent; `pg_class`, `pg_policy` and `pg_index` are the
authority on what is enforced, and the tests read them directly.

## What the boundary covers

It catches a business query or write that forgets its `shop_id` predicate — the
realistic failure across 31 tables and many handlers. It does **not** protect the
identity cluster, and it does **not** protect against a compromised process that
can set its own session context. Both limits are deliberate.

Every tenant policy takes one shape, so an absent context sees nothing and
writes nothing instead of raising a cast error:

```sql
ALTER TABLE <t> ENABLE ROW LEVEL SECURITY;
ALTER TABLE <t> FORCE ROW LEVEL SECURITY;
CREATE POLICY <t>_shop_isolation ON <t>
    USING      (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid)
    WITH CHECK (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid);
```

## Tenant business data — own `shop_id` (29)

| Table | Policy | Predicate column | Required index |
|---|---|---|---|
| `bank_accounts` | `bank_accounts_shop_isolation` | `shop_id` | `shop_id` leading |
| `credit_note_redemptions` | `credit_note_redemptions_shop_isolation` | `shop_id` | `shop_id` leading |
| `credit_notes` | `credit_notes_shop_isolation` | `shop_id` | `shop_id` leading |
| `customer_ledger_entries` | `customer_ledger_entries_shop_isolation` | `shop_id` | `shop_id` leading |
| `customers` | `customers_shop_isolation` | `shop_id` | `shop_id` leading |
| `discount_rules` | `discount_rules_shop_isolation` | `shop_id` | `shop_id` leading |
| `expense_categories` | `expense_categories_shop_isolation` | `shop_id` | `shop_id` leading |
| `expenses` | `expenses_shop_isolation` | `shop_id` | `shop_id` leading |
| `inventory` | `inventory_shop_isolation` | `shop_id` | `shop_id` leading |
| `inventory_adjustments` | `inventory_adjustments_shop_isolation` | `shop_id` | `shop_id` leading |
| `inventory_batches` | `inventory_batches_shop_isolation` | `shop_id` | `shop_id` leading |
| `invoice_leases` | `invoice_leases_shop_isolation` | `shop_id` | `shop_id` leading |
| `invoice_sequences` | `invoice_sequences_shop_isolation` | `shop_id` | `shop_id` leading |
| `item_barcode_sequences` | `item_barcode_sequences_shop_isolation` | `shop_id` | `shop_id` leading |
| `items` | `items_shop_isolation` | `shop_id` | `shop_id` leading |
| `purchase_order_receipt_sequences` | `purchase_order_receipt_sequences_shop_isolation` | `shop_id` | `shop_id` leading |
| `purchase_order_receipts` | `purchase_order_receipts_shop_isolation` | `shop_id` | `shop_id` leading |
| `purchase_order_sequences` | `purchase_order_sequences_shop_isolation` | `shop_id` | `shop_id` leading |
| `purchase_orders` | `purchase_orders_shop_isolation` | `shop_id` | `shop_id` leading |
| `reconciliation_issues` | `reconciliation_issues_shop_isolation` | `shop_id` | `shop_id` leading |
| `sale_items` | `sale_items_shop_isolation` | `shop_id` | `shop_id` leading |
| `sale_return_items` | `sale_return_items_shop_isolation` | `shop_id` | `shop_id` leading |
| `sale_returns` | `sale_returns_shop_isolation` | `shop_id` | `shop_id` leading |
| `sales` | `sales_shop_isolation` | `shop_id` | `shop_id` leading |
| `service_code_sequences` | `service_code_sequences_shop_isolation` | `shop_id` | `shop_id` leading |
| `services` | `services_shop_isolation` | `shop_id` | `shop_id` leading |
| `stock_transactions` | `stock_transactions_shop_isolation` | `shop_id` | `shop_id` leading |
| `supplier_ledger_entries` | `supplier_ledger_entries_shop_isolation` | `shop_id` | `shop_id` leading |
| `suppliers` | `suppliers_shop_isolation` | `shop_id` | `shop_id` leading |

## Tenant child data — parent-derived (2)

| Table | Policy | Predicate column | Parent | Required index |
|---|---|---|---|---|
| `purchase_order_lines` | `purchase_order_lines_shop_isolation` | `purchase_order_id` | `purchase_orders` | `purchase_order_id` leading |
| `purchase_order_receipt_lines` | `purchase_order_receipt_lines_shop_isolation` | `purchase_order_receipt_id` | `purchase_order_receipts` | `purchase_order_receipt_id` leading |

## Exempt, by decision (12)

| Table | Classification | Why it is exempt |
|---|---|---|
| `auth_sessions` | Global identity | Read by LiveSessionValidator inside OnTokenValidated, before HttpContext.User is assigned, so the session context is empty on every authenticated request. |
| `email_ownership_challenges` | Global identity | Address ownership is proved before a session exists, or across a change of the address a session is keyed to. |
| `hsn_cache` | Global reference | Public HSN tax reference, identical for every shop and populated from an external lookup rather than by any merchant. |
| `identity_mail` | Global identity | Delivery outbox drained by a background worker: no request, no token, no shop context. |
| `identity_sms` | Global identity | Delivery outbox drained by a background worker: no request, no token, no shop context. |
| `password_reset_tokens` | Global identity | Recovery runs for an account that cannot authenticate; there is no session and therefore no shop. |
| `phone_challenges` | Global identity | Phone enrolment and phone login run before any session exists. |
| `refresh_tokens` | Global identity | Rotation and sign-out present a refresh credential and no access token, so there is no shop claim to scope by. |
| `shop_invitations` | Global identity | Carries shop_id and is exempt anyway, deliberately: the invited account is not a member of the shop yet, so no shop-scoped predicate can admit the row it has to read to accept. |
| `shop_memberships` | Global identity | Carries shop_id and is exempt anyway, deliberately: switching writes the target shop's row while the request is still scoped to the previous one, the picker must read every row or no second shop can ever be chosen, and invitation acceptance must lock the inviter's row in a shop the actor has not joined. A self-referencing policy would also recurse. Guarded instead by the authority-lock order and the live per-request membership re-check. |
| `shops` | Global identity | Read before any shop is active — by the picker, and by shop creation before the rescope that makes the new shop current. |
| `users` | Global identity | Enrolment, login, refresh and token validation all read and write the account row with no shop context; an account belongs to no shop and may hold memberships in several. |

## Not in the EF model (2)

| Table | Classification | Why it is exempt |
|---|---|---|
| `__EFMigrationsHistory` | Global reference | Schema bookkeeping written by the migration job under the owner identity. |
| `cache_entries` | Global reference | The distributed cache. It must stay exempt while it shares the process NpgsqlDataSource: cache connections are opened without the EF interceptor, so no session context is set on them and a policy would reject every read. Per-tenant cache keying is owned elsewhere. |

## Sanctioned cross-shop access

Four operations legitimately touch a tenant table for a shop that is not the
request's active one, and they are the only ones permitted to: creating a shop
(which writes the new shop's system supplier before the rescope), reading shop
details, updating a shop (whose response carries its bank details), and writing
shop bank details. They acquire a transaction-local scope through
`ITenantWriteScope`.

The shop id is either one the handler has just created, or one the caller named
in the route. In that second case what makes it safe is not the origin of the
value but the gate in front of it: the handler re-reads a live membership of
that shop for the authenticated account — Owner where the operation requires
Owner — before entering the scope. A new call site that skipped that re-read
would admit a shop the caller has no claim to. There is no request-controlled
bypass flag, so a caller cannot ask for elevation directly.

## Verifying a deployed environment

`scripts/db/verify-tenant-policies.sql` produces the evidence for one database.
Run it after the migration job and before traffic is shifted, and keep the
output with the release artifact. Every query is written so that empty output is
the pass: a row is a finding.

```bash
psql "host=<server> user=<migrator> dbname=<database> sslmode=require" \
  -v app_role=id-app-prod \
  -f scripts/db/verify-tenant-policies.sql
```

It covers tenant tables missing `ENABLE`/`FORCE`, policies that omit a clause or
mis-parse an absent shop context, identity tables that have been brought under
row-level security by mistake, predicate columns that do not lead an index, the
runtime role's own attributes, its grants, and tables it cannot read or write.
Pooling is not answerable from the catalog — the shop context is a session
variable on a connection the pool hands round — so that half is proved by
`PooledConnectionTenantContextTests` and recorded alongside.

At startup `RuntimeRoleGuard` repeats the role check in the running process and
refuses to serve traffic if the connection's role is a superuser or holds
`BYPASSRLS`. It is on everywhere except Development and can be forced either way
with `Database:RequireRestrictedRuntimeRole`. A production error is never to be
resolved by granting `BYPASSRLS`.

`UpgradedSchemaRlsTests` covers what a fresh-schema suite cannot: it migrates to
the revision before this work, seeds a row the old way, upgrades in place, and
requires that the row is then invisible without the right shop context — and
that the upgraded catalog matches the manifest, not just the fresh one.
