# Till sessions and counted close

Till sessions attribute recorded settlement to one named cashier and one physical drawer for a single shop-local business date. They freeze the opening float, included source boundary, actual counted cash and variance. Recorded UPI, card, bank transfer and cheque are operator evidence, not verified gateway settlement or bank reconciliation.

## Deployment order and gate

Apply these five additive migrations in order through the deployment migration job **before API, readers or web**:

1. `20261108110138_AddTillSessions`
2. `20261108113929_AddSaleTillEvidence`
3. `20261108120219_AddPaymentExpenseSettlementEvidence`
4. `20261108122550_AddReturnTillSettlementEvidence`
5. `20261108130102_AddTillReconciliationEvidence`

API startup does not apply migrations. The local command, when explicitly operating an authorized database, is:

```bash
dotnet ef database update \
  --project src/backend/Intelibill.Infrastructure \
  --startup-project src/backend/Intelibill.Api
```

Deploy every API pod, reader and writer that can post a sale, offline invoice, payment, reversal, expense, return or cancellation before approving a pilot. An older writer can bypass required till evidence. Web rollout alone is insufficient. No historical session or tender backfill assigns legacy cash to a drawer.

Defaults remain:

```json
{"TillSessions":{"Enabled":false,"AllowedShopIds":[],"MixedTenderEnabled":false}}
```

Both `Enabled=true` and exact shop allowlist membership are required for new till work. Mixed tender is unavailable; configuring it true fails options validation. A request that supplies new till/evidence fields cannot silently fall back to legacy untracked posting while disabled. Absent new fields retain disabled-shop legacy behavior and canonical hashes.

## Opening and daily operation

Create a physical till as Owner/Manager, count its actual opening cash, then open a session with explicit `cashierId`, `openingFloat` and operation key. Explicit zero is valid; an omitted opening float or close count is rejected. The unique open-session index allows only one open session per physical till. Handover means count and close the current session, then open another for the next cashier. Shared simultaneous drawers and reopening a closed session are unavailable.

The assigned active cashier may trade; a live Owner/Manager acting for another active cashier must record an override reason. Staff cannot use another cashier's session. Frozen cashier/till names and business date do not follow later profile edits. If the cashier is suspended or removed, a live Owner/Manager can finalize with an override reason; further trading and opening on that inactive cashier's behalf remain refused. Removing a membership retains its audit row.

Trading requires the original shop-local date. An overdue session blocks trading and another session on its till. Owner/Manager may explicitly set `finalizeOverdue=true` and provide a reason to count it late. This preserves its original business date and the actual later count timestamp; it never permits cross-day trading. The original posting month must be open. Reopen it through the existing Owner-only posting-period workflow if needed; never redate the drawer or invoice. Overdue finalization remains manager-only on saved operation replay. An ordinary same-day close replay does not become overdue finalization as time advances.

Expected cash equals opening float plus included signed cash movements. Counted cash is the operator's physical count. A nonzero variance requires a reason. Staff variance closes immediately and awaits a separate Owner/Manager review. Review appends acknowledgement, preserving the original expected/count/variance and cashier. There is no automatic balancing entry.

## Source contracts

Each enabled source owns its existing transaction and acquires sorted posting-period fences before the till/session, then party, document, credit and stock locks. `ITillPostingService` stages attribution and movements without saving or committing. Callers acquire freshly inside each owning transaction; acquisition must never be reused after commit, rollback or retry. A source advances the session version even if it has no drawer movement. Close locks that same session, reloads its version and freezes its exact included boundary. A stale expected version is refused.

| Source | Recorded effect |
|---|---|
| Online sale | Session required even all-due; exactly one known external tender for a positive paid amount, no lines for zero. Cash tendered 500 less change 200 with applied 300 contributes +300. Due and redeemed store credit are separate totals. |
| Customer collection | +receipt amount in its actual recorded method, one receipt-linked movement. Allocations have no cash effect. |
| Supplier payment | −receipt amount once. Its linked supplier-settlement expense is provenance and adds no second outflow. |
| Independent expense | −explicit known tender equal to amount on today's drawer, or explicit `NoPhysicalMovement` for bookkeeping. |
| Expense correction | Increased cost requires MoneyPaid for the absolute delta (negative drawer cash); decreased cost requires MoneyReceived for the absolute delta (positive drawer cash); zero delta requires NoPhysicalMovement. Bookkeeping-only correction adds no drawer movement. Supplier-settlement expense correction remains refused. |
| Payment reversal | Customer refund is explicit whole-amount `MoneyPaid`; supplier recovery is explicit whole-amount `MoneyReceived`; `NoPhysicalMovement` reverses bookkeeping only. Never infer reversal tender from original method. |
| Sale return | Only the actual external refund reduces drawer tender; exact known `refundTender` plus `refundConfirmed=true` required. Due reduction/store-credit issue are separate settlement totals. |
| Return void | Explicit no recovery, or confirmed whole recovery of the known original payout into the current selected session. Generic legacy `Refund` does not establish cash evidence. |
| Sale cancellation | Existing capability and `receipt-cash-due/1` eligibility remain; exact operator-confirmed original cash refund creates one outflow. |
| Manual cash-in/out | Signed cash transfer with mandatory reason; server owns source identity. This is not revenue or an expense and cannot forge a financial source link. |
| Supplier return credit / receipt reversal | No drawer effect: payable/stock corrections do not prove cash refunded. |
| Late offline sale | Separate immutable supplemental disclosure, excluded from counted cash. |

Amounts use decimal INR with at most two places; new known external methods are Cash, Upi, Card, BankTransfer and Cheque. `UnknownRecordedMethod` is never a new concrete tender. New `TillPostingContext` freezes `sessionId` and optional override reason in the source request/hash. `TillSettlementEvidence` records disposition (`NoPhysicalMovement`, `MoneyReceived`, `MoneyPaid`), reason and actual tender if physical. Neither operator confirmation nor a reference claims independently verified payment.

## Routes, authorization and recovery

- `GET /api/tills/capabilities`; `GET/POST /api/tills` (creation Owner/Manager).
- `GET /api/tills/sessions?state=&businessDate=&page=&pageSize=`; `GET /api/tills/sessions/{id}`. Staff see only their own sessions; parent financial source links are withheld from Staff.
- `POST /api/tills/{id}/sessions`: cashier, explicit float, key, override reason when applicable.
- `POST /api/tills/sessions/{id}/cash-movements`: direction 1 or −1, amount, reason, expected version, key and optional override.
- `POST /api/tills/sessions/{id}/close`: explicit counted cash, expected version, key, variance reason, optional override and `finalizeOverdue`.
- `POST /api/tills/sessions/{id}/review`: Owner/Manager, reason, expected version and key.
- `GET /api/tills/operations/{create|open|cash-movement|close|review}/{key}`: Staff's own receipts or Owner/Manager shop receipts. Generic financial lookups retain Owner/Manager access.
- Generic financial lookup adds `expense-record`, `expense-correction`, `sale-return`, `sale-return-void`; existing payment and cancellation lookup contracts remain. Payment reversal has no generic lookup slug: recover by retrying its original reversal endpoint with the exact same frozen payload and key.

Financial operation types 18–22 are till create/open/cash/close/review, 23–24 expense record/correction, and 25–26 return/void. Movement source types 1–12 end with `OfflineSale=12`. Existing enum numbers remain fixed.

Authorization occurs before committed replay. Read/report and authorized saved replay continue after closing or disabling. A changed frozen payload under the same key is refused; an unknown response or lookup 404 does not authorize a fresh key. Expense/return enabled writes need durable keys and their explicit settlement contracts.

The existing return POST still returns the **current Sale DTO**, and return void still returns HTTP 204. Saved operation outcomes for types 25/26 are typed `{saleReturnId,saleId}` identities. Recovery uses that saved identity; it does not promise a byte-identical historical current-sale response. Return and cancellation movements link their own source IDs plus separately projected parent sale IDs; never route a sale-detail URL using a polymorphic return/cancellation ID.

## Offline review

New queued invoices freeze all four fields: `Till`, `TenderLines`, `TillBusinessDate`, `TillCashierId`, alongside their original issue time and invoice number. Offline start requires verified enabled capability and the cashier's own open same-day session. Offline cannot open/close a till.

Sync first stores the immutable envelope. A valid original open same-day session includes one normal source/movement atomically. Closed, overdue, missing or inconsistent context is held as `TillReview=13`; a closed month is `ClosedPeriodReview`. Resending a held envelope does not post around its case, even after opening another drawer or disabling till sessions.

Owner/Manager `ReconcileTillSale=6` posts the unchanged original invoice through the shared poster after the original period fence and records `TillLatePosting` with `IncludedInCount=false`. Original invoice number/date, envelope and close remain frozen. It creates neither a current drawer movement nor a historical count adjustment. A valid current open session refuses supplemental review (`Till.ReviewNotRequired`); use ordinary retry to include the normal cash.

A verified-issuer TillReview with **all four frozen fields present** may offer `RetryHistoricalLease`. Empty tender lines are present/valid for a zero-paid invoice. The unchanged enforcing poster still checks enabled gate, original open period, session, cashier, interval, tender and existing invoice policies. Partial/legacy context cannot use ordinary TillReview retry to fall through to disabled legacy posting. Staff may only Escalate their own eligible case; they cannot replay a saved manager action.

Legacy evidence remains separate: `Unknown` has no tender and is visibly unknown. `Recorded` evidence must match original paid amount and unambiguous Cash/UPI/Card method. Legacy Credit with a positive paid amount is ambiguous about external method; manager may supply separately reasoned actual known tender for that exact amount or retain Unknown. Never rewrite its original Credit method/envelope. New frozen tender lines must match exactly. Later money physically added to a drawer is a separately declared manual transfer.

## Web operation and retention

`/tills` provides opening, same-day source totals, manual transfer, counted close, variance review and history. `/tills/sessions/:id/print` reads the frozen close with separately labeled late disclosures. Warn operators about locally queued invoices before count, while server safety remains independent of that warning. This release has no mobile workflow, gateway automation, bank reconciliation, shared live drawer, mixed tender or partial close.

Every new till-aware keyed source/till action persists its user/shop/action/target, frozen payload/key and revision claim in IndexedDB before HTTP. Reload, tab contention, scope changes, network uncertainty and lookup 404 retain the original key. Session selection cannot rewrite a pending payload. Shared financial draft machinery retains confirmation proofs, the existing payment intent key and separate cancellation namespace. Scope epoch checks cover persisted and cached confirmations, including A→B→A; explicit recovery in the current scope is required after detachment. For these new requests, a storage failure blocks posting. Released pre-Till frozen localStorage payment intents retain their original K1 request and exact same-key recovery even when IndexedDB is unavailable; a currently selected till must not decorate that historical payload.

Before disabling operationally, finalize/reconcile open drawers and outstanding offline invoices. To stop new work, disable the gate or remove the shop allowlist; keep schema, immutable facts, source readers, guards and replay. **Never deploy pre-feature binaries or run destructive migration Down after any till/evidence fact exists.** All five feature migrations refuse Down before emitting schema operations; Down is not an operational rollback strategy. Preserve database backups and use a forward fix.

## Verification commands

```bash
dotnet build src/backend/Intelibill.slnx -c Release -m:1
dotnet test src/backend/Intelibill.slnx -c Release --no-build
dotnet ef migrations has-pending-model-changes \
  --project src/backend/Intelibill.Infrastructure \
  --startup-project src/backend/Intelibill.Api \
  --configuration Release --no-build
```

Integration tests migrate ephemeral PostgreSQL Testcontainers under a schema owner; authenticated requests use a restricted runtime role with forced RLS. They cover actual source acceptance (1000+500+200−100−50−300=1250, counted1240/variance−10, separate UPI700), trigger/row-lock barriers, immutable evidence and tenant constraints. These commands do not imply approval to migrate an application or production database.

From `src/frontend` run `bun run test --watch=false`, `bun run build`, then `bun run audit:ui:release`. Preserve existing build budgets and warnings; record actual results with the release rather than suppressing warnings.
