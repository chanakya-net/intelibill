# Sale cancellation rollout

Policy `receipt-cash-due/1` corrects an eligible mistakenly posted receipt once. It preserves the original invoice and writes separate immutable stock, settlement and accounting facts. This implementation is prepared for review; it does not establish accountant or legal approval for a live shop.

## Supported corrections

An active Owner or Manager may cancel an online version-2 **Receipt** issued by a frozen **Unregistered** seller, with zero tax and zero rounding adjustment, in the same open shop-local calendar month. Every original line must be inventory goods linked to its original valid batch. The operator must confirm that all goods have been recovered in restockable condition. No partial cancellation or substitute lot is supported.

The entire original total must reconcile to original due plus original paid cash. Original due is credited to the customer account exactly once. Positive paid cash requires an explicit cash-paid-back confirmation; zero paid requires no cash confirmation. Cash confirmation records the operator's declaration; the application does not transfer money or reconcile a till.

Refusals include offline/queued sales, services, tax documents, legacy/mismatched snapshots, older or closed months, any return history (including voided returns), applied or redeemed credit notes, active allocations and ambiguous collections. Positive UPI, Card, Credit or unknown tender requires an external refund workflow and cannot be relabelled as cash. There is no external refund completion, credit-note restoration, cancellation deletion, uncancel, automatic refund, replacement invoice or mobile workflow.

## Deployment order and defaults

1. Keep both settings at their safe defaults:

   ```json
   {
     "SaleCancellation": {
       "Enabled": false,
       "AllowedShopIds": []
     }
   }
   ```

   A **new** posting requires `Enabled=true` **and** the current shop's exact GUID in `AllowedShopIds`. Either gate alone leaves posting disabled. No production shop is enabled by this change. Environment-variable equivalents are `SaleCancellation__Enabled` and indexed `SaleCancellation__AllowedShopIds__0` entries.
2. Apply `20261107201224_AddSaleCancellations` using the established migration job before API/web deployment. Inspect generated SQL first. Do not apply it automatically through API startup. The migration adds `sales.status`, maps PostgreSQL `xmin` as the sale version, widens the stock movement representation and adds immutable `sale_cancellations`/`sale_cancellation_lines` with tenant composite foreign keys, restrictive deletion, indexes, and ENABLE/FORCE RLS with both USING and WITH CHECK. It does not infer historical cancellations.
3. Update **all API pods and all readers**, including report/export workers, before allowlisting any approved pilot. Older writers cannot enforce the terminal sale guards and older readers misstate cancellations. Deploy the matching web after the cancellation-aware API. Preserve operation receipts and browser drafts across deployments.
4. Before enabling a pilot, obtain the required user/accountant review of document applicability, cash evidence and credit rules. Agree owner procedures and review representative original and cancellation dates, stock restoration, customer balance and exports. Only then configure the approved shop on both gates. This document records no such approval.

## API and operator evidence

- `POST /api/sales/{saleId}/cancellation-preview` is read-only. It returns eligibility reasons, independent `postingEnabled`, the sale's status and expected version, frozen document/lines, exact due/cash/external/credit obligations and required confirmations. Missing posting-period rows remain absent.
- `POST /api/sales/{saleId}/cancellations` accepts `operationKey`, `expectedVersion`, trimmed `reason` (3–255 characters), `goodsRecoveredConfirmed` and `cashRefundConfirmed`. The key is trimmed to 1–128 characters and the expected version must be nonzero.
- Sale detail/history retains the issued number, original amounts and document, adding `status`, `version` and the linked cancellation. Version is an `xmin` equality precondition, not a sequence number.
- `GET /api/financial-operations/sale-cancellation/{key}` recovers a committed outcome. Authorization and tenant target checks precede replay. A committed same-key/same-payload retry remains readable after disabling posting or closing the month. A changed payload conflicts. A new key cannot cancel a terminal sale again.

A new posting with either rollout gate closed answers 503 `SaleCancellation.PostingDisabled`. Preview, history, print and authorized committed replay remain available. Business refusals and stale versions answer 409; malformed requests answer 400. An unknown browser outcome retains its exact operation key and payload through reload, tab and shop changes. A lookup 404 does not prove failure and must not cause a new key. Recover or retry the saved request; do not delete IndexedDB drafts to resolve uncertainty.

The original receipt remains a historical issued fact. A separate `SC-` reference carries frozen seller/buyer, original invoice/date, policy, reason, actor, cancellation date, original goods/costs and exact settlement evidence. Printed references and accounting exports state:

> Commercial cancellation record — not a GST credit note. Cash refund is recorded from the operator's confirmation.

The cash sentence is omitted when the recorded cash amount is zero. No statutory GST credit note is issued.

## Accounting and concurrency

Operation type 17 is `SaleCancellation`; customer ledger type 6 is `SaleCancellationCredit` (`SALE_CANCELLATION_CREDIT`); stock movement type 10 is `SaleCancellation` (`SALE_CANCELLATION`). Existing enum values remain unchanged. Stock restoration is one positive linked movement per original line, including multiple lines sharing a batch. Due credit is separate from return credit and confirmed cash is separate from external refunds.

The executor transaction locks operation receipt → posting-period fence → customer → sale → deterministic batches/items. It reloads sale/dependencies after locking and commits status, restored stock, due credit, snapshots and operation result atomically. Return and payment writers share the customer/sale lock order and refuse a cancelled sale. Batch/aggregate `xmin` protects competing quantity writers. The captured posting business day supplies both the fence and cancellation fact.

Reports retain positive original revenue/cost on issue date and subtract the frozen cancellation revenue/cost on cancellation date, including periods with no original sale rows. Recorded COGS reverses original per-line rounded cost; legacy raw-cost metrics reverse raw products separately. Cancellation does not inflate returns, wastage or independent stock loss. XLSX/PDF contain dedicated cancellation facts. Tally emits a balanced commercial Journal linked to the original invoice: debit Sales, credit original customer due and confirmed Cash.

## Release acceptance

Run the backend build and full suite against Docker PostgreSQL, the frontend test/build with supported Node `>=24 <26` and Bun, and `bun run audit:ui:release`. Do not suppress warnings. Release evidence must include:

- `SaleCancellationTests.DefaultConfiguration_DisablesNewPostingButAllowsEligiblePreview` for untouched false/empty defaults, and `Preview_IsReadOnly_AndDisabledPostingNeedsBothGates` for each independent gate and read-only preview; authorized replay after disablement; auth-before-replay and changed-payload refusal.
- `TenantPolicyManifestTests`, `RestrictedRoleRlsTests` and `SaleCancellationSchemaTests`, covering both new tenant tables, production-shaped restricted-role enforcement and tenant composite links on the migrated real integration database.
- Both winning orders of return/collection versus cancellation, stale version and atomic rollback; cash-only, due-only, mixed and zero-paid confirmation cases; unsupported external/applied-credit cases.
- Original-only, cancellation-only and combined date windows, fractional cost, frozen master data, every report/export format, generated PDF visual inspection, and rendered A4/thermal receipt and dialog inspection.
- Browser storage failure, two-tab claim/tombstone behavior, reload and lookup404 recovery, unknown-then-refusal and shop epoch changes without replacing the saved command.

Review the migration SQL, applied migration history and catalog RLS/foreign-key checks on the isolated integration database. Do not migrate an existing developer or production database as part of this release verification.

## Rollback after a cancellation

Disable new writes by setting `Enabled=false` and clearing the allowlist. Retain the migration, original receipts, cancellation facts, operation receipts, browser recovery state, cancellation-aware readers and mutation guards. Authorized committed replay must continue working. Review stock, customer credits and dated exports from the stored evidence.

Deploying a pre-feature binary after any cancellation is unsafe: it can reopen return/allocation paths, omit the due credit meaning or misreport stock/revenue. Do not run migration Down, drop the facts or erase terminal status. Roll forward with cancellation-aware code while keeping posting disabled.
