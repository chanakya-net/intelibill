# Durable Checkout Idempotency Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One cashier checkout produces one sale despite timeouts, reloads, retries and parallel tabs.

**Architecture:** The browser freezes each checkout as an immutable `CheckoutIntent` in IndexedDB *before* sending it, and every send or retry replays that stored `RecordSaleRequest` (same idempotency key). The server already converges replays on `(ShopId, ActorUserId, IdempotencyKey)`; a new read-only `GET api/sales/by-idempotency-key/{key}` lets the cashier reconcile without sending. An unresolved online checkout never becomes a second online or offline invoice.

**Tech Stack:** Angular 21, NgRx, PrimeNG, Bun, Vitest; ASP.NET Core 10, Wolverine, ErrorOr, EF Core/PostgreSQL, xUnit + NSubstitute, Testcontainers.

**Spec:** Pasted plan 11 "Durable checkout idempotency" (P1 before real trading); `docs/reviews/2026-10-21-market-readiness.md`.

## Global Constraints

- Domain ← Application ← Infrastructure ← Api; repository abstractions; ErrorOr, no exception-driven control flow; warnings-as-errors.
- Frontend uses Bun only; mobile excluded.
- No schema change: the lookup reuses `ISaleRepository.GetByIdempotencyKeyAsync` and the existing unique scope.

## Gap analysis (plan 11 → `main` at `3ce547e8`)

| Requirement | State on `main` | This plan |
|---|---|---|
| Server replay by `(shop, actor, key)`, hash conflict | Done in `RecordSaleCommandHandler` | Reuse; add RF1–RF3 integration proofs |
| Read-only reconciliation in caller scope | **Missing** | Task 1 |
| Key outlives one click | **Missing** — `createSaleIdempotencyKey()` per submit | Task 2 |
| Persist before send; storage failure sends nothing | **Missing** | Task 2 |
| Transport loss / 5xx marked uncertain, not a plain retryable error | **Missing** — effect maps every failure to `recordSaleFailed` | Task 3 |
| In-flight POST never cancelled | **Missing** — `switchMap` cancels a previous sale POST | Task 3 (`mergeMap`) |
| Cross-tab single sender | **Missing** | Task 2 (CAS send lease + `BroadcastChannel`) |
| Recovery UI, cleanup bound to intent | **Missing** | Task 4 |
| Browser proof (`checkout-recovery.spec.ts`) | Blocked — plan 48 release harness not on `main` | Deferred |

## Review focus

- RF1 lost committed response then reload → same sale, one stock/ledger mutation.
- RF2 two tabs, one intent → one sale, both see its identity.
- RF3 payload edit after uncertain delivery → original kept, replacement blocked.
- RF4 logout/shop switch while waiting → result stays with its own user/shop intent.
- RF5 IndexedDB failure or offline transition → nothing unpersisted or duplicate sent.

---

### Task 1: Scoped reconciliation endpoint

**Files:** `Application/Features/Sales/Queries/GetSaleByIdempotencyKey/*`, `Errors.Sale.IdempotencyKeyNotFound`, `SalesController.GetSaleByIdempotencyKey`, `SalesControllerTests`, `GetSaleByIdempotencyKeyQueryHandlerTests`.

- [x] Handler checks user, shop, membership, key bounds (1–128 after trim), then looks up in the caller's own `(shop, actor)` scope; returns the sale id or `Sale.IdempotencyKeyNotFound` (404).
- [x] Controller (`OwnerManagerOrStaff`) resolves the id then reuses `GetSaleDetailQuery`, like `returns/{returnNumber}`.
- [x] Integration: lost-response → lookup + retry return the original sale/invoice with one sale, one stock `Out`, one ledger entry (RF1); concurrent same-intent posts → one sale (RF2); changed payload → 409 with no second mutation, lookup still returns the original (RF3); other staff in same shop and other shop → 404; unknown key → 404 with code.

### Task 2: Durable intent boundary

**Files:** NEW `core/storage/checkout-intent-indexeddb.service.ts`, NEW `features/sales/services/checkout-intent.service.ts`, colocated specs.

- [x] Store (`intelibill-checkout-intents`, keyPath `intentId`): `add` resolves only on transaction commit; `update` is read-modify-write in one readwrite transaction (compare-and-set across tabs).
- [x] `prepare(scope, request)`: requires user+shop, deep-freezes a clone, stores `prepared` before any transport.
- [x] `claimForSend` (60 s lease; a live lease from another tab returns null), `markUncertain`, `confirm` (never after reject), `reject` (never after confirm), `loadUnresolved(scope)`, `recover(scope, id)` → `replayed | pending | conflict`.
- [x] `classifyCheckoutSendFailure`: 0/408/429/5xx/non-HTTP → uncertain; other 4xx → rejected.

### Task 3: Effect and store

- [x] `recordSaleRequested` carries `intentId`; effect claims, sends the stored request, confirms/rejects/marks uncertain; new `recordSaleUncertain`; `lastRecordedIntentId` in state; `mergeMap` instead of `switchMap`.

### Task 4: Page wiring and recovery UI

- [x] Submit: an unresolved checkout bound to the cart blocks any new submit — online or offline (RF3, RF5); otherwise `prepare` then dispatch; storage failure shows `pendingCheckout.persistFailed` and sends nothing.
- [x] Pending-checkout banner (amount, lines, time, status) with Check status, Retry same checkout, Start new sale (detaches cart; intent stays recoverable).
- [x] After reload the restored cart rebinds to its intent by `clientLineKey` set; scope change detaches; success clears the cart only when `lastRecordedIntentId` matches the cart's intent (RF4); a rejection frees the cart; a confirmation seen only via storage (other tab) is reconciled and shown.
- [x] i18n `sales.newSale.pendingCheckout.*` in all nine locales.

### Deferred

- `tests/ui-audit/release/checkout-recovery.spec.ts` (Playwright) — needs plan 48's production browser harness.
- Support procedure for legacy unknown checkouts (product decision).
- No automatic expiry of unresolved intents (per plan recommendation); confirmed intents are retained as minimal recovery history.

## Rollout / rollback

Ship API before UI (same PR is fine: the UI only calls the lookup on explicit "Check status"). The IndexedDB store is additive. Rollback may hide the banner but must keep intent-based sending; reverting to per-click keys is unsafe for unresolved intents.
