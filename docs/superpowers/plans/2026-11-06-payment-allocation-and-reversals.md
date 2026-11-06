# Payment Allocation and Reversals — Implementation Plan

Source: `pending-tasks/25-payment-allocation-and-reversals-done.md` (plan 25). Branch `feat/payment-allocation-reversals` off `main@d68a6742e`.

## Analysis (verified on main, 6 November 2026)

| Area | Today | Gap |
|---|---|---|
| Customer payment | `RecordCustomerPaymentCommandHandler` writes one `PAYMENT_RECEIVED` ledger row (`sale_id` null); keyed through `IFinancialOperationExecutor` | No tender, no reference, no invoice link, no reversal; takes **no customer lock**, so it can race a return whose due reduction is capped by the account balance |
| Supplier payment | `MakeSupplierPaymentCommandHandler` writes `PAYMENT_MADE` + an `Expense` (`SupplierLedgerEntryId`) | Same gaps; a reversal must void the linked expense too |
| Invoice due | `Sale.DueAmount` is issue-time; `GetCustomerAccount` shows it as current | A paid account still shows every original due |
| Returns | `SaleReturnValidator` caps the due reduction at the customer's account balance, under customer → sale `FOR NO KEY UPDATE` | Must also respect the invoice's current outstanding once payments settle invoices |
| Supplier liabilities | `PurchaseReceiptValue.EffectiveTotalCost` per batch (resolved receipts only) | Natural allocation target; void / supplier reassignment / downward cost correction can strand allocations |
| Ledger types | Customer: `SALE_DUE`, `PAYMENT_RECEIVED`, `RETURN_CREDIT`, `RETURN_CREDIT_REVERSAL`. Supplier already has `REVERSAL` | Customer needs `PAYMENT_REVERSAL` |

## Decisions (plan 25 "user decisions", taken with stated defaults)

1. **Excess payment**: accepted; whatever is not allocated stays visible as unallocated credit on the receipt and the account. Never auto-attached.
2. **Allocation**: explicit only. The web form offers an oldest-first suggestion the operator must tick; the server never allocates on its own. Legacy payments are never allocated by the migration.
3. **Reversal**: whole-receipt only, **owner only**, reason required (3–255 chars), operation key required. A partial correction = reverse + record again. Recording and allocating stay owner/manager.
4. **Receipt numbering**: none in this change (receipt id is the reference); numbering belongs with plan 20/50 documents.
5. **Opening-balance targets**: deferred to plan 38 (enum value reserved, not accepted).
6. **External reference** (UPI/card ref) is cashier evidence only; no gateway verification.

## Model

- `PaymentReceipt` (immutable): `Id, ShopId, PartyType (Customer|Supplier), PartyId, Amount, Method (Unknown|Cash|Upi|Card|BankTransfer|Cheque), BankAccountId?, ExternalReference?, BusinessDate, ReceivedAt, Notes?, LedgerEntryId, ExpenseId?, IsLegacy, CreatedBy`.
- `PaymentAllocation` (immutable): `Id, ShopId, ReceiptId, TargetType (CustomerInvoice|SupplierReceipt), TargetId, Amount, CreatedBy, CreatedAt`. Active while its receipt is unreversed.
- `PaymentReversal` (immutable, unique per receipt): `Id, ShopId, ReceiptId, Reason, LedgerEntryId, ReversedBy, ReversedAt`. Customer: `PAYMENT_REVERSAL` (+amount). Supplier: `REVERSAL` (+amount) and the linked expense is voided.
- Invariants: Σ allocations ≤ receipt amount; allocation ≤ target outstanding at allocation time; target belongs to the receipt's party and shop; reversed receipt cannot be allocated.
- Projection `IInvoiceBalanceReader`:
  - customer invoice: `OriginalDue = Sale.DueAmount`, `ReturnCredit = Σ active returns' DueReductionAmount`, `Allocated = Σ active allocations`, `Outstanding = max(0, OriginalDue − ReturnCredit − Allocated)`.
  - supplier receipt: `Original = EffectiveTotalCost` (resolved, unvoided batch), `Allocated`, `Outstanding`.
  - `UnallocatedCredit = Σ active receipts' (Amount − Allocated)`.
- Lock order (plan 27 shared order, after operation replay serialization): party row (`customers`/`suppliers`) → targets (`sales` / `purchase_receipt_values`, sorted by id) `FOR NO KEY UPDATE`, 5 s `lock_timeout` → 409 `Payment.Busy`.

## Contracts

- `POST customers/{id}/payments` / `POST suppliers/{id}/payments`: optional `method`, `bankAccountId`, `externalReference`, `allocations[{targetId, amount}]`. Response unchanged + `receiptId`. Request hash includes the new fields only when present, so pre-deploy retries still replay.
- `GET payments/{receiptId}`: receipt, party, actor, allocations (with target label), reversal, unallocated amount.
- `POST payments/{receiptId}/allocations` (owner/manager, optional `operationKey`).
- `POST payments/{receiptId}/reversal` (owner, required `reason`, `operationKey`).
- `GET payments/balances?partyType=&partyId=`: target balances + unallocated credit.
- `GET customers/{id}/account`: sales gain `allocatedPaymentAmount`, `returnCreditAmount`, `currentOutstandingAmount`; account gains `unallocatedCredit`, `receipts[]`; ledger rows gain `receiptId`.
- Guards: return validator uses `min(account balance, invoice outstanding)`; batch void, supplier reassignment and a cost correction below the allocated amount answer 409 when active allocations exist.
- New `FinancialOperationType`: `PaymentAllocation = 9`, `PaymentReversal = 10`.

## Tasks

### Task 1 — Domain + persistence
- [ ] Enums, entities, `CustomerLedgerEntryType.PaymentReversal`; domain unit tests.
- [ ] EF configurations, repositories, DbSets, migration `AddPaymentReceiptsAndAllocations` (tables, check constraints, RLS, legacy backfill with `UNKNOWN` method and `is_legacy`), `TenantPolicyManifest`.

### Task 2 — Application
- [ ] `IPaymentLockRepository`, `IInvoiceBalanceReader`, `PaymentAllocationPlanner` (validates allocations).
- [ ] Record customer/supplier payment: receipt + optional allocations in one transaction under party/target locks.
- [ ] `AllocatePayment`, `ReversePayment`, `GetPaymentReceipt`, `GetPartyBalances`; account DTO projection.
- [ ] Guards in return validator, batch void, supplier reassignment, cost correction.
- [ ] Unit tests for handlers and planner.

### Task 3 — API + integration tests
- [ ] `PaymentsController`, request records, error codes.
- [ ] `PaymentAllocationTests`: part-payment then settlement; two invoices; overpayment credit; cross-party/shop refusal; legacy backfill; reversal restores once (+ supplier expense); concurrent collections cannot over-allocate; payment vs return; duplicate reversal replays/conflicts.

### Task 4 — Web
- [ ] `customer.service.ts` types (method/reference/allocations, receipts, balances) + spec.
- [ ] Customer account: method + reference fields, oldest-first allocation suggestion (operator-confirmed), current outstanding column, unallocated credit, receipt list with owner-only reverse (reason) action; i18n in every locale.

### Task 5 — Verify, review, docs
- [ ] `dotnet build`, unit + integration suites, `bun run test`, `bun run build`.
- [ ] Code review; fix findings. Update `docs/backend-architecture.md` § Payment Receipts and Allocations, `CLAUDE.md`/`AGENTS.md` snapshot.

## Rollout / rollback
Migration adds tables and backfills legacy receipts (no allocations, method `UNKNOWN`). Old API instances keep writing bare ledger payments during a rolling deploy; `scripts/db/backfill-payment-receipts.sql` (same idempotent statement) closes that gap after rollout. Rollback = stop calling allocation/reversal endpoints; receipt facts stay.

## Out of scope (follow-ups)
Printable receipt view / download (plan 50), supplier allocation UI, credit-limit enforcement (plan 26), till movements (plan 33), opening balances (plan 38), receipt numbering.
