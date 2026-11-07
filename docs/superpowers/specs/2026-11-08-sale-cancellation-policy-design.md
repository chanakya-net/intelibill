# Sale cancellation policy design

Date: 2026-11-08. Analysis base: `569a763b` on `codex/sale-cancellation-policy`.

## Intent and authorization

Correct an eligible mistakenly posted sale once, with an immutable original receipt, an explicit cancellation record, exact stock restoration, and truthful settlement evidence. The user supplied the earlier “Sale cancellation and void policy Implementation Plan” and requested Astra analysis/planning followed immediately by Sol implementation, review, fixes, and a PR. This design records engineering defaults for that authorized work; it does not claim accountant approval for live use.

Web and API for Indian retail public SaaS; mobile is excluded. Keep Domain ← Application ← Infrastructure ← Api, ErrorOr, repository abstractions, IUnitOfWork, decimal money, warnings-as-errors, Bun frontend tooling, and existing offline invoice arithmetic/document contracts.

## Current evidence and dependencies

The old proposal was based on `5485507e`. Its dependencies have substantially landed:

| Dependency | Current owning implementation | Consequence |
|---|---|---|
| Durable financial commands | `Application/Common/Operations/IFinancialOperationExecutor.cs`, `Infrastructure/Services/Operations/FinancialOperationExecutor.cs` | Reserve operation receipt and commit result with effects; callers must authorize before replay. Next unused operation number is 17. |
| Return serialization | `Application/Features/Sales/Services/Returns/SaleReturnMutationCoordinator.cs`, `Infrastructure/Repositories/SaleRepository.cs` | Reuse `LockForReturnMutationAsync`: customer then sale, five-second lock budget. The coordinator owns its own transaction; do not nest it inside the operation executor. |
| Return accounting | `Application/Features/Exports/Accounting/ReturnAdjustmentReader.cs` | Separate dated immutable adjustments are established. A cancellation is not a SaleReturn and must not inflate return counts. |
| Issued documents | `Domain/ValueObjects/IssuedInvoiceSnapshot.cs`, `Sale.AttachInvoiceDocument`, `docs/gst-invoice-capability-matrix.md` | Preserve the v2 snapshot and issued number. No existing statutory cancellation/credit-note policy is established. |
| Payments | `Infrastructure/Services/Payments/InvoiceBalanceReader.cs`, `Repositories/PaymentReceiptRepository.cs` | Allocations use customer→sale locks; unallocated payments also reduce account debt. Check both invoice and account state. |
| Periods and business dates | `IPostingPeriodPolicy`, `IBusinessClock` | Fence before customer/sale locks; cancellation gets its own posting date. |
| Recent durable web workflow | `features/supplier-returns/services/supplier-return-draft.service.ts` | IndexedDB claim, revision, frozen payload and recovery survive tabs/reload/scope changes. Prefer this correctness pattern over the older localStorage payment-intent implementation. |
| Till sessions | No implemented till-session dependency in this branch | Record an operator-attested cash refund; do not fabricate a till transaction, bank payment, or cash-drawer reconciliation. |

`Sale` has no cancellation state or version token. `SaleDto` has the immutable document and returns. Sales history derives paid/refunded state; P&L uses `ProfitLossReportBuilder`; exports use `SalesExportDatasetBuilder`; dashboard trends and customer metrics also aggregate directly in `SaleRepository`. All must learn cancellation without removing the original sale.

## Chosen initial policy

This is a deliberately bounded mistaken-entry correction, not a universal void. Engineering chooses the following defaults; broadening them requires a new policy version and evidence/tests.

1. An active Owner or Manager may preview and submit. Staff cannot preview private settlement decisions or mutate. All shop/actor identity comes from authenticated context. Read-only sale history retains its existing authorization.
2. Only `SaleSource.Online`, with a valid version-2 issued `Receipt` whose frozen seller is `Unregistered`, zero tax in header/lines/document components, zero rounding adjustment, and a snapshot whose line totals/revenue exactly reconcile to the sale header, is eligible. Legacy documents, tax invoices, bills of supply, invoice-cum-bills, malformed snapshots, and any offline-origin sale are blocked with distinct reasons. Judge the original seller, never today's shop tax profile.
3. Sale business date and cancellation business date must belong to the same shop-local calendar month/year; that month must be open. No backdating. An older-month sale is directed to the existing return/financial adjustment workflow, not silently moved into the current period. The exact business date used for the fence is also stored in the fact, even across midnight while waiting.
4. Every line is inventory goods with an existing, nonvoided, same-shop batch and aggregate. All original quantities must be recoverable in restockable condition; the actor explicitly confirms this. No services, wastage, partial cancellation, quantity editing, or substitute batch. Missing or invalid links block the whole operation.
5. Any sale-return history, including a voided return, blocks cancellation. This avoids a second correction channel over a previously corrected document and protects issued/redeemed return credit notes.
6. Any positive `CreditNoteAppliedAmount` or persisted redemption blocks cancellation with `CreditRestorationRequired`; preview names the applied amount and explains that no balance is restored. Do not delete a redemption, increase a note balance, issue replacement credit, or invent an authorized restoration event. This is an explicit unsupported case, not an omission.
7. Any active payment allocation to the sale blocks cancellation. For a sale with due, require current invoice outstanding equal to original due, customer balance at least original due, and no active unallocated customer receipt amount. Otherwise show `CollectionsRequireReview`. Reversed receipts no longer count as active allocations; unrelated fully allocated invoices do not by themselves block cancellation. No receipt or allocation is deleted or reversed by cancellation.
8. Original paid amount is supported only when `PaymentMethod.Cash`. Require `cashRefundConfirmed=true` for positive paid amount and record the exact original paid amount as an operator-confirmed cash refund. For zero paid amount, require `cashRefundConfirmed=false` and record no refund. `goodsRecoveredConfirmed=true` is always required. Total cancellation settlement must equal original total: original due reduction + confirmed cash refund. Never reduce only part of the due to make the command fit.
9. Any positive paid amount with UPI, Card, Credit, or an unknown tender blocks with `ExternalRefundWorkflowRequired`, even if the caller sets the cash checkbox. Preview shows the amount and **“External refund is not recorded by this cancellation workflow.”** A pending external refund is never presented as paid back. Supporting pending refunds and their later confirmation is outside this version; no partially completed cancellation is persisted.
10. Cancellation is terminal: no uncancel, replacement invoice, renumbering, cancellation deletion, automatic refund, or credit restoration endpoint.

Preview checks the month using read-only `IPostingPeriodRepository.GetRangeAsync`; an absent month row is open. It must not call `AuthorizeForWriteAsync` or create a period row. The posting callback still takes the actual shared period fence before business locks.

Business ineligibility returns actionable code/message pairs in preview; posting returns 409 with the applicable code. Invalid confirmation/reason/version/key shape returns 400. Missing/foreign sale returns 404, insufficient role 403, lock contention 409. Request reason is trimmed, 3–255 characters; operation key trimmed, 1–128 characters; expected version nonzero.

## Rollout and legal boundary

Add `SaleCancellation:Enabled` (default `false`) and `SaleCancellation:AllowedShopIds` (default empty). Posting a **new** cancellation requires both enabled and the current shop explicitly listed. Preview returns `postingEnabled` independently from business `eligible`; history, print and recovery remain readable while posting is disabled. New writes answer 503 `SaleCancellation.PostingDisabled`. Apply this gate inside the executor's new-operation callback so an authorized retry can still replay a committed result after disablement.

The supplied proposal requires user/accountant approval of document applicability, refund evidence and credit rules before enabling live posting. This PR prepares a reviewable implementation; it does not enable any production shop. An operator configures an approved pilot shop after migration and deployment, initially with owner procedures. Cash confirmation is evidence of the actor's declaration, not proof that software transferred funds. Cancellation references and Tally journals are commercial accounting records, not statutory GST documents.

## State, persistence, and snapshots

Add `SaleStatus.Posted=0` and `Cancelled=1`, persisted on `sales` with existing rows defaulting to Posted, plus PostgreSQL `xmin` mapped to `Sale.Version` (`uint`). `Sale.MarkCancelled()` changes status only; original issued amounts, items, tax snapshot, dates, number, idempotency key and source identifiers never change. The version is an equality precondition, not a monotonic counter. Dependent returns/payment state does not need to change this token: it is always reread under locks and may independently refuse the command.

Add immutable `SaleCancellation` and `SaleCancellationLine` tables. Header: id, shop/sale/actor, operation key, reference `SC-` + GUID N, reason, cancelled instant, business date/zone, policy `receipt-cash-due/1`, original invoice reference/date, total/revenue/tax (zero)/due-reduction/cash-refund amounts, nullable linked customer ledger entry, original frozen seller/buyer identity for the reference, and confirmation facts. Lines: id, shop/cancellation/sale-item/item/batch/stock-transaction links, original quantity, frozen description/UQC, original cost price, original line revenue, rounded recorded cost. Snapshot monetary precision is 18,2; quantities 18,3; retain original cost precision required for legacy metrics. Frozen facts come from original sale/document; do not replace original names or quantities with current master data.

One cancellation per sale (unique SaleId), one line per original sale item, unique shop/reference. Header and lines use composite tenant FKs for all linked records, Restrict deletion, and FORCE RLS with both USING and WITH CHECK on active shop. Extend required alternate keys on existing parent tables. Header check: amounts nonnegative, tax zero, total = due reduction + cash refund; positive due requires a ledger link; positive cash requires confirmation. Lines require positive quantity and nonnegative cost. No destructive production down-migration is a rollback procedure.

Append enum members without renumbering: financial operation `SaleCancellation=17` / slug `sale-cancellation`; customer ledger `SaleCancellationCredit=6` / provider `SALE_CANCELLATION_CREDIT` / negative balance delta; stock transaction `SaleCancellation=10` / provider `SALE_CANCELLATION` / positive quantity. Update provider conversions and SQL constraints. This stock movement is not a sale, purchase, independent loss, supplier return, or ordinary customer return.

## Atomic command and competing writers

`POST /api/sales/{saleId}/cancellation-preview` has no body; response contains original document, status/version, business eligibility/reasons, postingEnabled, proposed posting date, stock lines, exact due/cash/credit/external-refund obligations and confirmation requirements. `POST /api/sales/{saleId}/cancellations` accepts `{operationKey, expectedVersion, reason, goodsRecoveredConfirmed, cashRefundConfirmed}` and returns `SaleCancellationDto`. Detail supplies current `status`, `version`, and nullable `cancellation`; the cancellation DTO includes its frozen lines and linked effects. Existing `GET /api/financial-operations/sale-cancellation/{key}` performs read-only outcome recovery.

Execution order:

1. Validate shape, authorize live membership/role and tenant target before the operation executor, without retaining a tracked pre-lock sale. Hash canonical target/version/trimmed reason/confirmations (not key or actor).
2. Executor reserves the operation receipt and owns the transaction. New-operation callback checks rollout; capture one `now` and corresponding business day; fence its month, then reuse customer→sale `LockForReturnMutationAsync`.
3. Load a fresh tracked sale and current dependencies after locks. Check terminal state first, then expected version, then complete policy. Do not let EF's pre-lock identity map return stale state. Never invoke the transaction-owning return coordinator here.
4. Restore each original line once with `InventoryBatch.AddQuantity` and `Inventory.AddQuantity`, append one positive cancellation movement per line, preserving same-batch/multi-line totals. Existing batch/aggregate xmin catches concurrent sales/counts; any conflict rolls everything back. Use deterministic batch/item ordering.
5. Append `SaleCancellationCredit` for exactly original due, build immutable cancellation header/lines and mark the sale Cancelled. Save all through the same IUnitOfWork. Executor stores response and commits. Translate concurrency/deadlock/serialization/lock failures only after rollback into retryable 409; never continue an aborted transaction. Publish inventory changes after successful commit using the established authorized realtime path.
6. Same key+same payload replays original result even when now cancelled, month later closed or rollout disabled. Changed payload is `Operation.PayloadMismatch`; another key on the cancelled sale is `SaleCancellation.AlreadyCancelled`.

`SaleReturnValidator` must reject Cancelled while holding the existing lock; detail reports zero returnable quantity with explicit cancelled state. `InvoiceBalanceReader` exposes zero current outstanding and an explicit cancellation-credit amount for cancelled invoices, so locked allocation planning cannot allocate to them. Customer ledger balance/credit limits/overdue use the appended credit once. Add cancellation labels to customer statements. Do not relabel cancellation credit as return credit.

Return versus cancellation: if return wins, history blocks cancellation; if cancellation wins, validator blocks return. Collection/allocation versus cancellation: both use customer→sale locks; either collection makes cancellation ineligible or cancellation leaves no allocatable amount. Unallocated payment collection uses the customer lock too. Applied credit redemptions are immutable sale facts and always block; returns that might issue credit also hold the sale lock. No credit-note restoration mutation exists, so there is no new reverse lock order.

Offline-origin sale cancellation is explicitly unavailable. Offline replay contracts and calculationVersion stay unchanged. An online RecordSale replay after cancellation returns the same original sale with current cancellation state; it never recreates stock consumption. `by-idempotency-key` already routes through detail. Existing offline deduplication must still return the existing sale identity and never reactivate a terminal sale (exercise a defensive cancelled fixture); do not change sync success strings or frozen payloads to add this feature. Cancellation UI is unavailable on a device-only queued invoice; local abandonment remains distinct.

## Reading, printing, reports, and exports

Do not hide cancelled sales from repository date-range reads. An original sale remains positive on its issue business date. A separate cancellation adjustment reverses revenue and COGS on cancellation business date. Cancellation policy version and exact recorded-cost snapshot are the common source; never recalculate using current batch costs or prices. Recorded COGS is the sum of `SaleEconomicsCalculator.RecordedCost(originalQuantity, originalCostPrice)` per original line; legacy raw-cost metrics reverse their original raw products separately. This prevents fractional-cost residuals.

| Reader | Required behavior |
|---|---|
| Detail/history/search/latest dashboard sales | Explicit Cancelled state, original amount/document retained, cancellation reference and date visible; `status=cancelled`; paid/partially-paid/refunded filters exclude cancelled. Original invoice counts remain document counts. |
| Customer account/credit/balances | Original issue due retained, cancellation credit separate, current outstanding/overdue zero, account credit reduced once; lifetime/current-month revenue includes dated cancellation effects, document purchase count retained. |
| History summary/dashboard trend | Dedicated cancellation amount/count; period net sales subtract cancellations posted in the queried dates, including original sales outside the date window. Keep return totals/counts distinct. |
| P&L/dashboard economics | New row type `saleCancellation`; negative original revenue and COGS, zero stock loss/wastage; include in all totals, filters, search, export and previous-period comparisons. No inflation of return counts. |
| Sales XLSX/PDF | Original rows preserved with current status and linked reference; dedicated cancellation rows/section with dated stock and settlement facts, including adjustment-only windows. Net/tax-breakup totals reconcile without pretending cancellations are returns. |
| Tally XML | Add a validated balanced **Journal**, number = cancellation reference, referencing original invoice/date. Debit original sales revenue; credit original customer's ledger for due and Cash for confirmed refund. Zero tax; no GST credit note, guessed bank/refund-clearing settlement, or statutory claim. Cancellation-only periods must produce the journal without requiring original invoice rows in the same export. |
| Original receipt print (A4/thermal) | Preserve frozen original content, add separate visible Cancelled banner/reference/date and cancellation facts/disclaimer. Print both linked facts; do not rewrite the invoice's issue-time paid/due fields. Later customer/shop edits must not alter the reference. |

Exact disclaimer: **“Commercial cancellation record — not a GST credit note. Cash refund is recorded from the operator's confirmation.”** For zero cash, omit the second sentence. No statutory or payment-completion claim beyond the stored facts.

## Web operation durability

Owner/manager action in sale detail opens a preview and reason/confirmation dialog. Show all refusal reasons and settlement amounts, plus posting-disabled state. No editable amount. Positive cash confirmation is separate from goods recovery; both default false. Cancelled sales show immutable facts and no return/cancel action. Ineligible cases offer existing return/payment navigation without implying that those workflows automatically solve legal/credit issues.

Use a sale-specific IndexedDB draft service patterned on the supplier-return durable implementation, keyed by user+shop+sale and action `sale-cancellation`. Before any HTTP write atomically freeze the complete payload and operation key; a competing tab must recover the existing pending operation. Quota/blocked/storage errors prevent HTTP. Unknown response, navigation, reload, tab/shop changes retain frozen payload. Recovery looks up the key; 404 means “no committed result visible”, never “failed”. Resend only the exact payload and key. Once unknown, later 400/403/404/409 must not discard uncertainty or mint a new key without committed outcome evidence. Guard HTTP and asynchronous UI completion with `REQUEST_SCOPE` and the originating scope/epoch. Preserve a confirmation tombstone to prevent stale tabs clearing/replacing a completed operation. Refresh sale/history/customer/dashboard data only in the original active scope.

## Acceptance and validation

Core fixture: unregistered online receipt, 2 units, original ₹200 total, ₹120 recorded cost. Cash cancellation adds exactly 2 units and one ₹200 confirmed refund fact, original invoice unchanged, one cancellation, zero net revenue/COGS across both dates. Pure due cancellation adds ₹200 cancellation ledger credit and no refund. Mixed ₹80 cash + ₹120 due reverses exactly those two legs. Fractional cost/quantity fixtures prove per-line rounded COGS reversal. Two original lines sharing a batch restore their sum once.

Required real PostgreSQL tests cover auth-before-replay, changed-key/payload rules, stale versions, same-key concurrency, return and collection races in both winning orders, credit-note block, unsupported external pending-refund block, failure rollback, month boundary/closed fence, foreign tenant links and restricted-role RLS reads/writes. Reports/export fixtures include original day only, cancellation day only, both days, no original rows in window, and subsequent master-data edits. UI tests cover storage failure, double tab, reload/404 recovery, unknown then refusal, scope change, checkboxes, disabled rollout and A4/thermal print. Full backend/frontend/release checks follow focused tests; review SQL/RLS and all readers independently before PR.

## Non-goals and rollout risks

No GST cancellation legal determination, e-invoice cancellation, paid external refund workflow, applied credit-note restoration, till sessions, partial cancellation, services, offline-origin cancellation, closed/older-month correction, mobile workflow, uncancel, destructive rollback, or automatic payment transfer. These are visible refusal paths. Migration must precede API/web; all API pods must understand Cancelled before a shop is enabled. Disable writes to roll back, retain schema and readers. Rolling back to pre-cancellation binaries after posting would reopen return/allocation paths and misreport figures, so it is not supported.
