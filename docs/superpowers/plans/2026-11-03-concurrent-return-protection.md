# Concurrent Return Protection — Implementation Plan

Source: `pending-tasks/16-concurrent-return-protection-done.md` (plan 16, P1). Branch: `fix/concurrent-return-protection`.

## Analysis (verified against `main` @ 7e3f1fb58)

| Path | Today | Race |
|---|---|---|
| `RecordSaleReturnCommandHandler` | Validator reads active returns, then inserts. No transaction, no lock. | Two returns of the same line both read "1 returnable" and both commit. Service and wastage lines touch no versioned row, so nothing fails. Restockable lines collide on batch `xmin` and surface as a generic 409 instead of an entitlement answer. |
| `VoidSaleReturnCommandHandler` | Reads return + credit note without locks; inserts `ReturnCreditReversal`, then saves. | Two voids both see "not voided" and both post a reversal and both pull stock. |
| Void return vs. `RecordSale` redemption | Redemption locks the credit note `FOR UPDATE`; void reads it unlocked and writes `is_voided` later. | Void overwrites a just-redeemed note (lost update): a voided, redeemed credit note. |
| `VoidCreditNoteCommandHandler` | Same unlocked read/write. | Same lost update against redemption. |
| `CreditNote.Void` | Guards on `Redemptions.Count`. | A `FOR UPDATE` that waited re-reads the locked row but a joined `Include` is evaluated against the statement's earlier snapshot, so redemptions committed by the lock holder are invisible. `AvailableBalance` on the locked row is current. |

Not present in the codebase (so not in scope here): operation keys for returns (plan 12 covers payments, stock counts, cost corrections only), sale cancellation (plan 32), customer allocation locks (plan 25), posting-period fence (plan 27), tills (plan 33).

## Design

**Lock order** (documented in `docs/backend-architecture.md` § Sale Return Serialization):
customer row (when the sale has one) → sale row → credit notes of that sale's returns → inventory batch/aggregate rows (taken by `UPDATE` at save, version-checked by `xmin`).
Redemption takes credit notes → batches and never touches the parent sale, so it is compatible. Nothing locks a return or credit note and then waits on its sale.

1. `ISaleRepository.LockForReturnMutationAsync(shopId, saleId)` → `RowLockOutcome {Acquired, NotFound, TimedOut}`. Infrastructure: `SET LOCAL lock_timeout` for the rest of the transaction, lock the sale's customer (if any) then `SELECT id FROM sales WHERE id = … AND shop_id = … FOR NO KEY UPDATE`; SQLSTATE `55P03` → `TimedOut`. Shop predicate + RLS keep it tenant-scoped.
2. `ISaleReturnRepository.GetSaleIdAsync(shopId, returnId)` — untracked read of the immutable root id only.
3. `ICreditNoteRepository.GetBySaleReturnIdForUpdateWithRedemptionsAsync` — lock statement first, then a fresh load (new snapshot sees the holder's redemptions).
4. `CreditNote.HasBeenRedeemed` = redemptions exist **or** `AvailableBalance < OriginalAmount`; `Void` and return void use it.
5. `ISaleReturnMutationCoordinator` (Application) — begins the transaction, locks the sale, runs the mutation, commits; rolls back on any error/exception. Maps `TimedOut` → `SaleReturn.SaleBusy` (409, retryable) and `DbUpdateConcurrencyException` → `SaleReturn.ConcurrentChange` (409, retryable).
6. Record return: coordinator wraps validation + all writes (validation re-reads under the lock; preview stays advisory).
7. Void return: locate sale id → coordinator → reload return, re-check `IsVoided`, lock credit note, re-check redemption, write.
8. `VoidCreditNote`: transaction + `GetByCodeForUpdateWithRedemptionsAsync` + `HasBeenRedeemed`.

No schema change; no migration.

## Tasks

- [x] **T1 red** — `SaleReturnConcurrencyTests` (real PostgreSQL, separate HTTP clients/contexts, `IUnitOfWork` barrier that holds the first writer at save until the competitor either reaches save or is seen waiting on a lock in `pg_stat_activity` — no sleeps):
  `TwoServiceReturns_OnlyOneEntitlementConsumed`, `TwoWastageReturns_OnlyOneEntitlementConsumed`, `TwoRestockableReturns_StockAddedOnce`, `MixedGoodsAndServiceReturns_EachLineBounded`, `DoubleVoid_CreatesOneCreditReversal`, `VoidThenRedeem_RedemptionRefused`, `RedeemThenVoid_VoidRefused`, `VoidCreditNoteVersusRedeem_ExactlyOneOutcome`, `PreviewThenCompetingReturn_PostingRevalidates`, `LockTimeout_ReturnsRetryableConflictWithNoEffects`, `OtherShopsSale_NotFoundAndNotLocked`. Run; confirm the race tests fail.
- [x] **T2 green** — items 1–8 above; DI registration; error codes.
- [x] **T3 unit** — coordinator tests (commit/rollback/not-found/timeout/concurrency, lock carries shop id); handler tests updated (lock taken before any read, lock refusal short-circuits, re-check under lock); `CreditNote.Void` balance guard.
- [x] **T4 verify** — integration filter, Application/Domain unit suites, full non-integration suite, build warnings-as-errors.
- [x] **T5 docs** — architecture section + CLAUDE.md pointer.
- [x] **T6 review** — independent review of the diff; fix findings.

## Rollback

Code-only. Reverting restores the previous (unsafe) behaviour; release record, void and credit-note void together because they share the lock order.

## Decisions taken / open

- Exhausted entitlement keeps its existing contract (`400 SaleReturn.QuantityExceedsRemaining`, `400 SaleReturn.AlreadyVoided`) so clients need no change; only lock outcomes are new 409s.
- Lock timeout: 5 s. Clients get a readable 409 and must refresh/retry; nothing reposts automatically.
- Refund overrides above the discounted paid amount stay as they are (reason required); serialization does not change the policy.
- Review follow-up (done): the customer row is locked before the sale (cross-sale due reduction), the 5 s budget covers every lock in the transaction, and 40P01/40001 answer 409.
- Follow-ups: keyed (idempotent) returns under plan 12's executor; customer payments taking the customer lock (plan 25); deterministic ordering when one sale redeems several credit notes.
