# Supplier returns and receipt corrections design

## Purpose and evidence

Implement the attached plan 31 as an API and web workflow: return physically available receipt goods, distinguish shipment from supplier credit acceptance, reverse a wholly erroneous eligible receipt, and reconcile stock, supplier payable and purchase history. Mobile is excluded. The user authorized analysis, implementation, review/fixes and a PR in the existing checkout without an additional approval gate.

This design is grounded in `main` commit `13656e2e` (merged receiving PR #799), replacing the attachment's old `5485507e` references. Current `ReceivePurchaseOrderCommandHandler` already creates multiple receipt lots with stable client-line IDs, links each `PurchaseReceiptValue` to `PurchaseOrderReceiptLine`, and uses operation type 13. `ReceivingDraftService` already has durable cross-tab claims, frozen uncertain requests and operation confirmation evidence. Reuse those patterns without regressing receiving.

Other current integration facts:

- `VoidBatchCommandHandler` already refuses consumed or allocated receipts, writes a full reversal, and uses batch/aggregate/receipt `xmin` checks. Its legacy monetary fallback is inappropriate for a new commercial return.
- `IPaymentReceiptRepository.LockPartyAndTargetsAsync` locks supplier then sorted receipt-value rows; the receipt lock is a no-op UPDATE that advances `xmin`. Loading receipt versions before this call produces stale versions.
- Sale, count, intake and adjustment paths protect shared physical quantities using batch and inventory aggregate `xmin`; they do not all acquire one shared pessimistic stock lock.
- `InvoiceBalanceReader.GetForSupplierAsync` currently subtracts active payment allocations from effective receipt cost. It must also subtract accepted return credits.
- PO `ReceivedQuantity` and receipt line `Quantity` describe historical receiving. They must not be rewritten to reopen ordering.

## Constraints

- Web and API only; no mobile changes.
- Preserve Domain ← Application ← Infrastructure ← Api, ErrorOr, FluentValidation, repository abstractions and IUnitOfWork.
- Use decimal money with two decimal places and QuantityPolicy with the item's quantity scale (0–3).
- Require authenticated active shop membership; Owner/Manager may ship returns and read operational history; only Owner may accept supplier credit or reverse a receipt. A Manager may create a pending shipment only.
- Every new mutation requires operationKey (1–128 characters), reason (3–255 characters), expected versions, and a single atomic transaction.
- Business dates use IBusinessClock and the shop's existing business time zone; no client backdating in this feature.
- New tenant tables use explicit shop predicates and ENABLE/FORCE RLS with USING and WITH CHECK.
- No legal GST credit/debit document claim, independent tax calculation, cash refund workflow, shipping-cost subsystem, automatic procurement reopening, historical inference, or replacement-batch editing.

## Decisions and boundaries

A commercial return can contain 1–100 distinct receipt-value lines from one real supplier and shop, whether received directly or through a PO. A line identifies the immutable `PurchaseReceiptValue` and its original batch; an optional PO lineage is derived by the server. Do not accept a supplier, item or batch identity that contradicts that receipt. Anonymous/system-supplier or unresolved legacy receipts require existing source correction before returning.

The API uses a general return document endpoint rather than making the PO receipt group mandatory. Direct receipts and PO lots therefore share the same behavior. The receipt detail is keyed by receipt-value ID; the UI calls it a **receipt lot** where a PO has several lots. Provide a full PO receipt-group reversal in addition to a single receipt-lot reversal; the group command succeeds only when every lot is eligible and rolls back all lots on any failure.

Shipment status is `PendingSupplierCredit` or `CreditAccepted`. The shipment is immutable. An Owner may accept credit while posting the shipment or later. Later acceptance happens once, with a separate operation key, version, reason and optional supplier reference. Accepting zero is allowed to close an explicitly agreed no-credit return; it adds acceptance evidence but no zero ledger entry. Disputes remain visibly pending until an explicit agreement; no automatic approval or rejection is invented.

Agreed amounts may differ from proportional purchase cost. Each line's accepted credit must be between zero and the receipt's effective total less all previously accepted credits; this prevents erasing unrelated payables. A paid receipt may still be returned: acceptance can leave a credit on the supplier account. Existing payment allocations remain immutable; show the receipt's resulting supplier credit separately, never invent a cash payout or pretend the old payment became unallocated. Costs of shipping can be recorded through the existing expense workflow outside this action.

After any supplier shipment, block receipt cost correction and supplier reassignment with an actionable `PurchaseReceipt.HasSupplierReturns` conflict. This preserves agreed commercial history without inventing retrospective credit repricing. Prices may still change through their existing path. Counts may correct remaining physical goods but cannot erase shipment history. Existing batch void must also refuse any shipment. The general batch-update route already rejects supplier changes with `Inventory.BatchSupplierEditMoved`; retain that guard and test it, including the older client entry points. A future return cancellation/credit-reversal workflow is outside this request; do not hide/delete documents or tell users to reverse legitimate payments to settle a supplier dispute.

## Immutable facts and projections

Add these focused persisted facts:

- `SupplierReturn`: shop, supplier, collision-safe commercial return number, shipment instant/business date/time zone, reason, actor, status, version (`xmin`), frozen shop/supplier display details needed for print, lines.
- `SupplierReturnLine`: shop, return ID, receipt-value ID, batch/item IDs, optional PO receipt-line ID, positive quantity, proposed credit, original receipt/effective-cost snapshot, item description/UQC and source reference snapshots, stock movement ID.
- `SupplierReturnCredit`: one acceptance per return, shop, accepted instant/date/time zone, actor, reason, supplier reference; line-level `SupplierReturnCreditLine` stores accepted amount, return-line ID, receipt-value ID and nullable ledger-entry ID (zero acceptance has none). Immutable, unique by return/line.
- `PurchaseReceiptReversal`: shop, receipt-value ID (unique), optional PO receipt-group ID, reason, actor, reversal instant/date/time zone, quantity, effective amount, stock movement ID, nullable ledger-entry ID, commercial reference. Keep the source rows intact.

Append enum values without renumbering: financial operations `SupplierReturn=14`, `SupplierReturnCredit=15`, `PurchaseReceiptReversal=16`; ledger `SupplierReturnCredit=6`; stock `SupplierReturn=9`. The stock type distinguishes shipment from a sale or independent stock loss. Explicitly audit enum converters, movement displays and profit/stock-loss readers so a supplier shipment never counts as sales, COGS or wastage. Receipt reversal continues to use stock/ledger `Reversal` with explicit reversal lineage.

Returnable quantity is `max(0, min(batch.Quantity, receipt.ReceivedQuantity - sum(shipped quantities)))`; all shipments count, including pending credit. Reject voided/reversed batches and missing/ambiguous receipt provenance. Counts cannot raise the receipt quantity ceiling. Existing stock restoration from a customer return may make units available again, but never undo the supplier-shipment ceiling. Validate both item precision and positive quantity.

Preview is advisory and returns source identities/labels, receipt and batch versions, received/available/already-shipped/returnable quantity, effective cost, previously accepted credit, active allocated payments, suggested credit, policy warnings and reversal eligibility reasons. Suggested credit uses rounded proportional effective cost, capped by the remaining credit ceiling; an amount is never silently accepted on the supplier's behalf. Posting revalidates all values.

Financial projections per receipt:

- `netPayable = effectiveTotalCost - acceptedReturnCredit` (nonnegative by policy).
- `outstanding = max(0, netPayable - activeAllocatedPayments)`.
- `supplierCredit = max(0, activeAllocatedPayments - netPayable)`.
- Account balance remains the signed sum of ledger entries, including negative accepted credits. Do not add the receipt supplier-credit projection to that account balance a second time.
- Pending shipment has zero payable effect. Acceptance emits one negative, source-linked ledger entry per positive accepted line on acceptance date.
- `netRetainedQuantity = historicalReceivedQuantity - shippedQuantity - reversedQuantity`. This measures purchases retained, not current stock after sales. Show historical received and net retained side by side, with returned/reversed subtotals. PO expected/remaining/status are unchanged.

## Concurrency and transaction boundary

Authorize before replay and derive shop/actor from the session. Use `IFinancialOperationExecutor.ExecuteAsync<T>` for all new writes, with canonical payload hashes containing target, ordered line identities, versions, quantities/amounts, reason and reference. The executor owns begin/commit; callbacks only save. Replay a committed result before business version/period revalidation, so a successful yesterday operation remains recoverable today.

Inside a fresh mutation callback: period fence → supplier → sorted receipt-value roots → return root for acceptance → physical changes. Reversal of an old receipt requires both today's and the original receipt's periods open; ordinary supplier shipment/credit requires only its own posting date open. Group reversal resolves its immutable receipt lines first, then locks their common supplier and sorted receipt values. It need not lock/mutate the PO order row, avoiding the receiving order-lock path.

Use a focused repository lock that takes the same supplier and receipt row locks as payments **without a no-op receipt UPDATE**, then load/reload tracked receipt facts after locks. For acceptance acquire the return root with FOR NO KEY UPDATE after the receipt roots; all paths that touch it follow this order. Preserve/restore the caller's lock_timeout setting on successful acquisition, and always roll back a timed-out PostgreSQL transaction before reuse. This preserves the client's expected receipt version while still contending with payments. New return/reversal/acceptance writes must explicitly touch the receipt's version so legacy cost correction, reassignment and void detect races. Acceptance also writes the return version. Map lock timeout to `SupplierReturn.Busy`/`PurchaseReceipt.Busy`, stale versions to a 409, and roll back before returning.

For physical stock preserve the existing shared `xmin` protocol: update both original batch and item aggregate with one signed movement in the same save; any sale/count/intake/adjustment that read an old row loses its save and rolls back. Do not claim an isolated new `FOR UPDATE` lock serializes writers that do not take it. Do not introduce a repository-wide stock-lock refactor as incidental scope. Validate this choice with independent-context races; if tests find an uncovered quantity writer, fix that writer's use of the existing version protocol. Duplicate return lines are refused; process distinct receipts/items deterministically. No transaction can commit negative stock or a partial multi-line return.

Full reversal eligibility requires resolved provenance, active batch, quantity equal to receipt received quantity, no supplier shipment, no active allocations (past allocations whose payment was explicitly reversed remain visible audit evidence but do not block eligibility), and no consumption history even if a sale was later returned or a count restored quantity. Any non-opening stock movement/count/adjustment history makes the receipt ineligible; a simple price revision or cost correction is allowed. Reverse the exact effective total, never `remaining * rounded unit cost`. Zero-cost receipt writes stock/reversal history without a zero ledger entry. Existing batch-void UI/API must share or delegate to this eligibility policy to prevent bypass; maintain existing route response compatibility and existing valid historical cases explicitly in tests.

## API and web contract

All routes are under `/api`:

- `GET purchase-receipts/{receiptValueId}` → receipt detail, eligibility, return/reversal history and PO lineage.
- `POST supplier-returns/preview` → advisory preview for `{supplierId, lines:[{receiptValueId, quantity}]}`.
- `POST supplier-returns` → `{operationKey, supplierId, reason, creditAccepted, supplierReference?, lines:[{receiptValueId, expectedReceiptVersion, expectedBatchVersion, quantity, proposedCreditAmount}]}`; Owner required if creditAccepted. Proposed amounts become accepted only in that explicit mode.
- `GET supplier-returns?supplierId=&receiptValueId=&status=&page=&pageSize=` and `GET supplier-returns/{id}` → history/detail, accepted evidence and print snapshots; cap pageSize at 100.
- `POST supplier-returns/{id}/credit` → `{operationKey, expectedVersion, reason, supplierReference?, lines:[{returnLineId, acceptedAmount}]}`; exactly every return line once, Owner only.
- `POST purchase-receipts/{receiptValueId}/reversal` → `{operationKey, expectedReceiptVersion, expectedBatchVersion, reason}`.
- `POST purchase-order-receipts/{receiptId}/reversal` → `{operationKey, reason, lines:[{receiptValueId, expectedReceiptVersion, expectedBatchVersion}]}`; exact current receipt-lot set, Owner only, atomic.
- Existing financial-operation lookup gains `supplier-return`, `supplier-return-credit`, `purchase-receipt-reversal` slugs. Both reversal variants hash their target kind and identity.

The common frontend feature `features/supplier-returns` supplies receipt detail/actions, return editor, acceptance dialog and printable detail. Link it from inventory batch details (direct intake), PO receipt history and supplier detail/history. PO history exposes actual receipt-value IDs and net retained totals; do not make clients infer receipt-value IDs from batch IDs. The supplier ledger links accepted credits to return detail; pending shipments are visible in a separate return history section even though no ledger entry exists.

Every write uses scope-owned durable drafts and exact immutable operation payloads before the first network send. Follow #799's `ReceivingDraftService` protocol in a new focused service; do not make its receiving-specific schema generic as incidental refactoring. Unknown response, reload, tab races or shop switch keep the operation frozen. A lookup 404 is not proof the in-flight operation failed; retry only the identical key/payload. Initial definitive validation may unlock editing while retaining fields; a later error after uncertainty cannot erase the pending operation. Persistent-storage failure prevents sending. Retain per-operation confirmation evidence so one tab cannot resurrect another's confirmed action. All mutations and lookup calls use captured `ScopeOwner` and `scopedRequestContext`.

Print the frozen shipment identity/lines and acceptance evidence with the exact label “Supplier return reference” and a visible “Commercial reference — not a GST credit/debit note” disclaimer. No invoice-series reservation or purported tax document. All nine `public/assets/i18n/*-IN.json` locales require complete labels, warnings, errors, statuses and print copy; UI uses translations, accessible controls, loading/error states and responsive layouts.

## Migration, verification and rollout

Additive EF migration and snapshot: new tables, unique keys, indexes, bounded money/quantity constraints, required FKs and tenant policies. For child facts include ShopId and constrain parent/child tenancy (composite tenant FKs where feasible); application checks alone do not authorize cross-shop references. Append enum values in converters/checks. No fabricated historical supplier return or credit backfill. Existing resolved PO/direct receipt values are eligible; ambiguous legacy rows remain visibly blocked. Add migration tests for a fresh schema and upgrade with existing receipt/payment facts, plus actual restricted-role read/write isolation tests.

Deploy migration before API/web. Existing writes and #799 recovery remain compatible. Rollback disables new mutation entry points while preserving schema, shipments, credit resolution and read history; never run a destructive Down migration after posting. Document the no-cancellation limitation and outstanding pending credits rather than deleting audit history.

Release proof includes full .NET solution tests (Docker/Testcontainers), frontend unit suite, production build and release Playwright tests. Exercise online sale/return/count/payment/period races, duplicate/different-payload keys, precision/rounding, paid returns, pending acceptance, direct and PO lineage, grouped reversal atomicity, restricted-role tenancy, uncertain-retry reload/cross-tab/scope transitions, translated print and mobile-width web layouts. Run with the repository's supported Node engine (>=24 <26) and Bun. Fix review findings and re-run affected checks before opening the PR.
