# Till sessions and day close Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Account for each physical till's opening, source-linked cash/tender activity, counted close, variance and late offline reconciliation.

**Architecture:** Add immutable session movements and frozen close snapshots under the source transaction, with posting-period → till → business-row locking. Explicit tender/settlement evidence fills source gaps; existing operation receipts and durable browser intents own retries. Late offline invoices produce separately reviewed supplemental records, never rewrite a drawer count.

**Tech Stack:** ASP.NET Core 10, C#, ErrorOr, FluentValidation, EF Core/Npgsql/PostgreSQL, xUnit/Testcontainers; Angular 21, NgRx, PrimeNG, Bun.

**Spec:** `docs/superpowers/specs/2026-11-08-till-sessions-day-close-design.md` (read in full; source matrix is normative).

## Global Constraints

- Web/API only; Domain ← Application ← Infrastructure ← Api; `IUnitOfWork`, repository interfaces, warnings as errors; Bun only.
- `TillSessions:Enabled=false`, `AllowedShopIds=[]`, `MixedTenderEnabled=false`; no automatic enablement; historical fields/hashes and feature-disabled behavior remain compatible.
- One physical till/open session; named cashier; manager override reason; handover close/open; no cross-day trading. Overdue manager finalization preserves original date and actual late timestamp, requires original period open.
- Decimal two-place INR; unique immutable source effects; explicit actual settlement evidence; no unknown-as-cash, no supplier expense double counting, no historical backfill.
- Every new request is tenant/actor scoped and idempotent; authorization precedes replay; committed replay survives session closure/rollout disablement.
- Keep original invoice, close and offline envelope immutable; late records explicitly excluded from counted cash. Mixed tender and automated settlement remain unavailable.
- This implementation does not merge, deploy or enable a shop.

## Review Focus

1. A financial source replay after its till closes or is disabled returns its original outcome without duplicating cash (Tasks 2–5).
2. A bookkeeping void/correction with no actual payout/recovery changes no drawer cash; unknown original tender cannot be guessed (Tasks 4–5).
3. Midnight/closed-month/overdue sessions cannot bypass date fences; late count is labeled and does not authorize trading (Tasks 2,6).
4. Offline historical replay and direct sync share till enforcement and cannot add already-counted money to another drawer (Task 6).
5. A stale browser tab, 404 lookup or persistence failure never produces a second financial operation or moves an unresolved payload into another scope (Task 7).

## Task map and handoff

Execute sequentially with a fresh Sol implementer and review gate per task, following the selected subagent-driven-development workflow. Tasks 1 → 2 establish contracts/schema and lifecycle. Tasks 3 (sales), 4 (payments/expenses), and 5 (returns/cancellations) consume those contracts; Task 6 also depends on 3. Task 7 consumes Task 2's DTOs and source request contracts from 3–6. Task 8 gates completion. Reviewers may investigate independent risks, but do not run concurrent implementation workers against shared DI, DbContext, migrations, common enums or source contracts.

All paths below are repository relative. New files use existing namespace/style and split validators/DTOs from handlers. Do not create giant catch-all controller/service files. Preserve existing public overloads where legacy unit fixtures rely on them; production DI must always resolve the real service, never a permissive default stub.

### Task 1: Domain accounting, persistence and shared contracts

**Create:** `src/backend/Intelibill.Domain/Entities/{Till,TillSession,TillMovement,TillClose,TillCloseReview,TillLatePosting}.cs`; `Domain/ValueObjects/{TenderLine,TillSettlementEvidence}.cs`; `Domain/Enums/{TillSessionState,TillMovementSource,TillSettlementDisposition}.cs`; `Domain/Interfaces/Repositories/ITillRepository.cs`.

**Create:** `src/backend/Intelibill.Application/Features/Tills/{TillPostingContext,TillSourcePosting,ITillPostingService,ITillSessionPolicy,TillDtos}.cs`; `Common/Errors/Errors.Till.cs`; infrastructure `Data/Configurations/*Till*Configuration.cs`, `Repositories/TillRepository.cs`, `Options/TillSessionOptions.cs`, `Services/Tills/TillSessionPolicy.cs`. Modify `ApplicationDbContext.cs`, DI registrations and add EF `AddTillSessions` migration/model snapshot.

**Interfaces:** `TillPostingContext(Guid SessionId,string? OverrideReason)`; `TillSourcePosting(SourceType,Guid SourceId,string Purpose,IReadOnlyList<TenderLine> Lines,decimal Direction,DateTimeOffset OccurredAt,string OperationKey,string? Reason,decimal StoreCreditIssued=0,decimal StoreCreditRedeemed=0,decimal ReceivableChange=0)`; define a signed non-cash settlement representation alongside tender movements rather than abusing Cash/Unknown enum. `ITillPostingService.AcquireAsync(Guid shopId,Guid actorId,TillPostingContext? context,DateOnly sourceBusinessDate,CancellationToken ct) : Task<ErrorOr<TillSession?>>`; `AppendAsync(TillSession session,TillSourcePosting source,Guid actorId,CancellationToken ct) : Task<ErrorOr<Success>>`. Null session is allowed only for disabled/no-context legacy flow. The caller fences periods first.

`ITillRepository`: tenant-scoped get/list, lock till/session returning `RowLockOutcome`, reload session after lock, get movements/totals, add entities, find source/late record. Use `xmin`; append increments sequence. `TillSessionDto` includes id/till/cashier frozen labels, businessDate, state, version, openingFloat, expectedCash, recordedTenderTotals, storeCreditIssued/redeemed, receivableChange, close/review and separate late disclosures. Detail movement pagination is explicit.

- [ ] Write Domain `TillSessionTests` and `TenderLineTests`: ₹1000+500+200−100−50−300=1250, count1240 gives−10; UPI700 does not change cash; ₹500−₹200=₹300; paired cash fields, scale, enum, negative amount, mixed-line rejection; immutable closed state.
- [ ] Run `dotnet test tests/backend/unit/Intelibill.Domain.Unit.Tests --filter 'FullyQualifiedName~Till|FullyQualifiedName~TenderLine'`; confirm meaningful red.
- [ ] Implement domain/contracts, DB checks, composite tenant FKs, unique open/source/sequence/close/late constraints, RLS and additive nullable source evidence mappings. No historical data assignment.
- [ ] Run same tests plus `dotnet build src/backend/Intelibill.slnx`; inspect generated migration and model for required columns/indexes/RLS. Add restricted-role migration fixtures.
- [ ] Commit cohesive schema/accounting changes after review.

### Task 2: Serialized lifecycle, posting service and API

**Create:** application `Features/Tills/Commands/{CreateTill,OpenTill,RecordTillCashMovement,CloseTill,ReviewTillClose}/` command/validator/handler files; `Queries/{GetTills,GetTillSessions,GetTillSession,GetTillOperation}/`; `Services/TillPostingService.cs`; API `Controllers/TillsController.cs`; integration `TillSessionTests.cs`, `TillSessionConcurrencyTests.cs`, unit `Features/Tills/TillCommandHandlerTests.cs`.

**Modify:** `Domain/Enums/FinancialOperationType.cs` append 18 TillCreate,19 TillOpen,20 TillCashMovement,21 TillClose,22 TillCloseReview; source operation values below reserve 23 ExpenseRecord,24 ExpenseCorrection,25 SaleReturn,26 SaleReturnVoid. Never renumber 1–17. Add typed routes/lookup and options registration.

**Interfaces:** exact routes/request fields in spec. Commands add server ActorUserId/ShopId. `TillOperation` lookup returns saved typed response and restricts Staff to their own actor receipt; manager/owner may read own-shop receipts. `ITillSessionPolicy.IsEnabled(shopId)` exposes allowlist; capabilities return mixed=false even if gate disabled. Normal errors include Till.Disabled, SessionRequired, SessionNotFound, SessionClosed, BusinessDateMismatch, Busy, VersionConflict, CashierMismatch, OverrideReasonRequired, TenderRequired, MixedTenderUnsupported, SettlementEvidenceRequired.

- [ ] Add tests `WrongTillOrCashier_CannotMutateSession`, `ManagerOverride_RequiresReason`, `OpenRace_OneOpenSession`, `MovementChangesVersion_StaleCloseRejected`, `StaffVarianceClose_RequiresReview`, `OverdueTradeRejected_ManagerFinalizesOriginalOpenPeriod`, `DisabledLegacy_NoTillEffects`, `DisabledExplicitTill_Refused`, `ReplayAfterCloseAndDisable_ReturnsOriginal`.
- [ ] Run targeted unit/integration filters, observe red; implement authorize-before-replay, keyed execution, period-first locks, five-second contention, sequence advancement and immutable snapshot. CreateTill also uses idempotency (operationKey/name); opening validates cashier active membership. Manual entries cannot accept arbitrary source ids.
- [ ] Verify `DoubleClose_OneSnapshot`, restricted RLS reads/writes, snapshot equality after later source changes, other cashiers' operation lookup forbidden, owner review immutable/replayable. Close reloads session after locking and sums only committed included movements.
- [ ] Run `dotnet test tests/backend/integration/Intelibill.Integration.Tests --filter FullyQualifiedName~TillSession`; commit after review. Publish stable DTOs to source/frontend workers.

### Task 3: Online sale tender conservation and offline payload contract

**Modify:** `Application/Features/Sales/Commands/RecordSale/{RecordSaleCommand,RecordSaleCommandValidator,RecordSaleCommandHandler,RecordSaleIdempotencyHasher}.cs`; API sales request mapping; `Domain/Entities/Sale.cs` and sale configuration/DTO builder for nullable till/tender evidence; `Commands/SyncOfflineSales/{OfflineSaleSyncCommand,OfflineSaleSyncIdempotencyHasher,OfflineSubmissionPayload}.cs` as applicable to actual file declarations.

**Interfaces:** append optional `TillPostingContext? Till` and `IReadOnlyList<TenderLine>? TenderLines` to new sale commands/requests. Persist newly supplied actual tender facts and session attribution. PaymentMethod compatibility is explicit mapping, never integer cast. PaidAmount equals tender sum; due/store credit remain existing settlement fields. New hash branch includes till id/override and normalized tender evidence; old branch byte-for-byte unchanged when absent.

- [ ] Add `CashTenderedFiveHundredForThreeHundred_ChangeTwoHundred`, `AllDueSale_AttributedWithoutCash`, `StoreCredit_NotPhysicalCash`, `MixedTenderDisabled_RejectsMultipleExternalLines`, `TillEvidenceChanged_SameKeyConflicts`, `LegacyHash_Unchanged`, `SaleRollback_RollsBackTill` in existing sale unit fixtures and `TillSourcePostingTests.cs` integration.
- [ ] Run filters for new tests, confirm red; acquire till after period fence before customer/invoice/credit/stock roots, stage source movement with sale before save, preserve existing replay path. Do not fail a committed replay because old till is closed; still validate live actor authority.
- [ ] Extend offline input serialization/hash compatibly with frozen optional till/tender fields; preserve existing arithmetic/calculationVersion/document contracts. Queue/client work is Task 7, enforcement Task 6.
- [ ] Run sale handler/unit/integration and arithmetic/document contract tests; commit after review.

### Task 4: Receipts, independent expenses and explicit reversal evidence

**Modify:** `Features/Customers/Commands/RecordCustomerPayment/*`, `Features/SupplierLedger/Commands/MakeSupplierPayment/*`, `Features/Payments/Commands/ReversePayment/*`, `Features/Expenses/Commands/{RecordExpense,CorrectExpense}/*`; corresponding API controllers/DTOs/hash payloads; `Domain/Entities/{Expense,PaymentReversal}.cs` and configurations for immutable settlement evidence; `Features/FinancialOperations/FinancialOperationTypes.cs` for expense-record/expense-correction recovery.

**Interfaces:** append optional `Till` to payment requests; receipt Method supplies the single external tender. Enabled payments require operation key/known method/date=today. Expense record adds optional OperationKey/Till/TenderLine plus explicit NoPhysicalMovement for historical bookkeeping-only entries; correction/reversal add optional OperationKey/Till/SettlementEvidence. Legacy absent fields retain old paths. New expense operations use 23/24; reversals retain operation10 with new hash only when evidence supplied.

- [ ] Add `CustomerCollection_AddsCashOnce`, `SupplierPaymentAndExpense_OnlyOneOutflow`, `UnknownPaymentTender_EnabledRejected`, `PaymentReversal_NoPhysicalMovementDoesNotTouchCash`, `CustomerRefundAndSupplierRecovery_UseActualTender`, `ExpenseCorrection_DoesNotAssumeRecovery`, `ExpenseCorrectionExplicitDelta_DirectionAndAmountValidated`, `SupplierExpenseCorrection_Refused`, replay/payload mismatch and transaction rollback tests.
- [ ] Run existing/new payment/expense unit filters, establish red. Integrate period→till→party locks before mutation, use receipt/reversal/expense ids as source identity, persist no-movement dispositions, and atomically stage all source effects. Fence today plus historical expense periods before taking till lock when correction touches both.
- [ ] Require explicit full-amount actual reversal tender or explicit no movement; never use receipt.Method automatically for reversal. Expense correction permits only exact signed difference for a claimed actual settlement; metadata-only correction produces zero cash. Linked supplier expense never posts its own till effect.
- [ ] Run integration source fixtures including payment-vs-close and payment-vs-period-close; update changed constructor fixtures without bypassing production services; commit after review.

### Task 5: Return/refund, return void and cancellation integration

**Modify:** `Features/Sales/Commands/{RecordSaleReturn,VoidSaleReturn}/*`; `Services/Returns/{ISaleReturnMutationCoordinator,SaleReturnMutationCoordinator}.cs`; `Services/Cancellations/SaleCancellationService.cs`; cancellation command/request hash; API `SalesController.Returns.cs` and cancellation mapping; persisted return/void tender/evidence; relevant DTOs; `Features/FinancialOperations/FinancialOperationTypes.cs` for sale-return/sale-return-void recovery.

**Interfaces:** return request adds OperationKey/Till/RefundTender/RefundConfirmed; return void adds OperationKey/Till/SettlementEvidence. Add coordinator overload with `Func<CancellationToken,Task<ErrorOr<Success>>> beforeBusinessLocks` after fence and a no-nested-transaction path for executor-owned keyed flow. Preserve existing legacy overload. Enabled keyed return/void operation25/26 authorization precedes replay. Cancellation adds Till to its existing keyed request and hash; refund cash amount comes from existing validated cancellation facts.

- [ ] Add `GenericRefundWithoutActualTender_EnabledRejected`, `UpiRefund_DoesNotReduceCash`, `CreditNoteAndDueReduction_NoCash`, `ReturnVoid_NoRecoveredMoney_NoCash`, `ReturnVoid_ConfirmedRecoveryCurrentSessionOnly`, `Cancellation_OneConfirmedCashRefund`, retry and rollback integration assertions.
- [ ] Run targeted return/cancellation tests for red. Place till acquisition in coordinator's pre-business-lock hook, not inside already sale-locked callback. Make keyed return and void atomic without nesting begin/commit; preserve sale serialization and stock/accounting semantics.
- [ ] Require whole original payout for actual recovered-refund evidence (single tender), else explicit no movement. Persist actual refund tender for future audit without modifying issued invoice or historical generic Refund facts. Validate all evidence before saving source mutations.
- [ ] Run existing return/cancellation tests plus `TillSourcePostingTests`; commit after review.

### Task 6: Late offline reconciliation that preserves counted closes

**Modify:** `Commands/SyncOfflineSales/{OfflineSalePoster,IOfflineSalePoster,SyncOfflineSalesCommandHandler}.cs`; `Domain/Enums/{ReconciliationIssueType,ReconciliationAction}.cs`; `Features/OfflineReconciliation/{ReconciliationActionPolicy,Commands/ResolveReconciliationCase/*,Queries/GetReconciliationCase/*}` and API request mapping; `TillLatePosting` repository and DTO readers. Add integration `TillOfflineReconciliationTests.cs`.

**Interfaces:** append `TillReview` issue and `ReconcileTillSale` action (append enum values). Resolution request carries separate optional `TillReconciliationEvidence` with tender evidence/reason; it never changes frozen envelope. `OfflineSalePostingRequest` gains optional server-only reviewed-till context, not client-controlled bypass boolean. Poster stages either normal open-session movement or one supplemental late record; never both. Historical lease review routes must still satisfy till policy.

- [ ] Add `OfflineBeforeClose_IncludedOnce`, `OfflineAfterClose_CreatesVisibleReconciliation`, `RetryHistoricalLease_CannotBypassTillReview`, `ReconcileTillSale_PreservesInvoiceDateAndCloseBytes`, `LateResolution_NoCurrentDrawerCash`, `LegacyMissingEvidence_RemainsExplicit`, `WrongCashierOrIssueTime_NotAssigned`, and two-manager resolution race tests.
- [ ] Run filters for red. Enforce original period first, then session; hold closed/overdue/missing/inconsistent session cases after rollback. Retain any existing unresolved TillReview on resend. Verify claimed issue instant lies within historical session interval and issuer matches cashier before linking that session; otherwise record unassigned evidence explicitly.
- [ ] Implement manager resolution using existing keyed case lock/version flow and poster; original period must be open. Unique source/supplement prevents double counting across normal sync/replay/resolution. Expose supplemental amount and `includedInCount:false` separately, retaining unknown evidence labels where historical facts are unavailable.
- [ ] Offer bounded Owner/Manager `RetryHistoricalLease` for a verified-issuer TillReview envelope with all four frozen context fields present through the unchanged enforcing poster; zero-paid empty TenderLines remain valid presence. Exclude incomplete/legacy context from ordinary retry in both action display and execution so disablement cannot trigger untracked legacy posting. Keep resends held, refuse invalid/closed/disabled context, and refuse unnecessary supplemental review for a valid current drawer. Prove disable-before-first-sync→TillReview→re-enable→ordinary retry posts one OfflineSale12 source and zero supplements, retains offline customer-credit variance filing, and authorizes every Staff non-Escalate action before saved-receipt replay while preserving own Escalate replay.
- [ ] For Recorded legacy evidence, conserve exact PaidAmount and match original Cash/UPI/Card. For wholly legacy Credit with positive paid amount, permit either Unknown/no tender or separately reasoned actual known single tender for exactly PaidAmount, without altering the frozen method/envelope. Keep exact frozen-line equality for new-field payloads. Add mismatched-method/amount refusal, matching known-method success and ambiguous-Credit regressions.
- [ ] Run offline sync/reconciliation/period integration suites; commit after review.

### Task 7: Durable till UI and every source workflow

**Create:** `src/frontend/src/app/features/tills/{services/till.models.ts,services/till.service.ts,services/till-draft.service.ts,services/till-context.service.ts,pages/till-session-page.component.ts,pages/till-session-page.component.html,pages/till-close-print-page.component.ts,components/till-posting-fields.component.ts}` plus adjacent meaningful specs.

**Modify:** `app.routes.ts`, actual navigation/menu configuration, supported `public/assets/i18n/*.json`; checkout `features/sales/pages/new-sale-page.cart-submit.service.ts` and its payment form; `services/{sale.models,offline-sale-core.types,offline-sale-finalization.service,offline-sales-queue-sync.service}.ts`, offline mapper/snapshot schema; customer/supplier payment overlays/services; expense record/correct overlays/services; sale return/void/cancellation dialogs/services; customer payment reversal controls; offline reconciliation model/console.

**Interfaces:** mirror Task2 DTOs and Tasks3–6 optional source fields. `TillContextService` owns selected session by ScopeOwner and clears UI selection on scope transition; pending requests retain frozen scope/session. `TillDraftService` uses IndexedDB durable action/target key, revision CAS and per-operation confirmations as cancellation drafts do; repetitive cash actions use unique action target ids, not a terminal “cash-movement” tombstone that blocks all later movements. Lifecycle operation lookup retains unknown on404.

- [ ] Write component/service specs: full open→cash movement→counted close→review→print; negative variance reason; non-cash labels; Staff permissions; overdue manager finalization; same frozen fields through source submit, reload, tab race and shop switch; persistence failure causes zero HTTP; retry/404 never creates new key; confirmed tombstone prevents duplicate submission.
- [ ] Run `bun run test --run src/app/features/tills` from `src/frontend` for red; implement the reachable route/navigation and reusable selection/evidence controls. Source-enabled workflows require concrete session/evidence; disabled flows retain old behavior and no phantom till defaults.
- [ ] Wire all source forms with actual tender/settlement choices including NoPhysicalMovement for accounting-only reversal/correction. Build durable intents for newly keyed expense/return/void actions; preserve existing payment/checkout/cancellation durability. Freeze online and offline till/tender context before HTTP/queue write; offline cannot start session or trade after session business date. Render late reconciliation and manager evidence inputs separately from current cash.
- [ ] Add printable frozen close reference including included boundary, cashier, original date, late timestamp status, expected/count/variance, recorded tender totals and separately labeled late supplements. Test print data comes from snapshot, not mutable source totals.
- [ ] Run focused till and changed source specs, then `bun run build` and `bun run audit:ui:release`; visually verify normal and mobile-width web layouts, empty/error/unknown/closed/overdue states. Commit after review.

### Task 8: Acceptance, races, tenancy, rollout and PR

**Create/modify:** `tests/backend/integration/Intelibill.Integration.Tests/{TillSessionTests,TillSessionConcurrencyTests,TillSourcePostingTests,TillOfflineReconciliationTests}.cs`; existing restricted-role RLS/schema fixtures; `docs/till-sessions-rollout.md`, backend/frontend architecture, AGENTS.md feature summary. No changes to mobile.

- [ ] Execute the exact acceptance scenario through real endpoints: opening1000, sale500, collection200, actual refund100, independent cash expense50, cash-out300, UPI700; expected1250/count1240/variance−10, separate UPI700 and immutable source links.
- [ ] Complete deterministic independent-context barriers for normal-offline-sync-vs-review and close-vs-offline-sync (concurrent HTTP starts alone are insufficient), plus sale-vs-close, payment-vs-close, double-close, open race, period-close-vs-till and period-close-vs-payment. Assert one coherent outcome or explicit conflict, no deadlock, no committed cash after included boundary. Cross-shop/cashier ids and forged enum/source ids must fail in API and restricted RLS fixtures.
- [ ] Run `dotnet build src/backend/Intelibill.slnx` and `dotnet test src/backend/Intelibill.slnx`; from frontend run `bun run test`, `bun run build`, `bun run audit:ui:release`. Run EF pending-model check with project's established tooling and inspect additive migration/Down risk. Preserve preexisting warnings; report actual counts and any failures honestly.
- [ ] Document migration-before-all-writers, allowlist default off, explicit opening count, known-tender evidence limits, overdue policy, mixed tender unavailable, immutable late supplements, disable/read/replay behavior and destructive rollback prohibition. Update feature docs to final names and routes.
- [ ] Fresh full-branch review checks every source-matrix row, lock order, transaction ownership, hash compatibility, replay authorization, default-disabled behavior, offline bypass, UI durability, RLS and migration. Fix findings and rerun affected checks; don't mark complete with mandatory source integrations omitted.
- [ ] Coordinator creates PR from final branch after clean checks. PR states actual scope, migration, default-off gate, tested evidence and limitations; no deployment/merge/shop enablement.
