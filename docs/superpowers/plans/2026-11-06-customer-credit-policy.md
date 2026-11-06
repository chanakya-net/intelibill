# Customer credit policy and account UI — implementation plan (backlog 26)

Source plan: backlog 26 (market-readiness review, 2026-10-21). Re-verified against `main` at
`f5379e7c` (after plan 25, payment receipts/allocations/reversals, merged in #794).

## Analysis: what the code does today

| Area | Finding |
|---|---|
| `customers-page.component.ts` | Account credit limit = `max(outstandingDue, lifetimeRevenue)` (RF1 broken); opening balance = `ledgerEntries.at(-1).runningBalance`, which is the *oldest* entry's after-entry balance, not a balance before the window. |
| `customer-status.util.ts` / list page | Any positive balance is `overdue`; summary card "overdue count" counts every customer that owes anything (RF2 broken). |
| `GetCustomerAccountQueryHandler` | No saved limit, active flag or policy; no opening/closing; ledger ordered by date + created-at only (non-deterministic on ties); no window. Plan 25 invoice outstanding is already returned per sale. |
| `Customer` | `IsActive` + `CreditLimit` exist; 0 is ambiguous (in practice "unlimited"). No terms. |
| `Sale` | No due date. |
| `CustomerResolver` / `RecordSaleCommandHandler` | Resolve identity only; no limit, inactive or override check; customer row never locked, so two concurrent due sales both pass any check (RF3/RF4 broken). |
| Lock order today | Returns: customer → sale → credit notes → batches. Payments: customer → sales/targets. Sale posting: invoice sequence → credit notes → batches. Customer row `FOR NO KEY UPDATE`, 5 s `lock_timeout`, 55P03 → busy. |
| Offline sync (`OfflineSalePoster`) | Uses the same resolver; inactive customer at sync time already files a `CustomerVariance` reconciliation issue and still posts. |

## Decisions

1. `CreditPolicyMode { NoCredit = 1, Enforced = 2, LegacyAdvisory = 3 }` on `Customer`, plus optional
   `CreditTermsDays` (0–365).
   - Migration sets every existing customer to `LegacyAdvisory` (old behaviour, labelled in the UI).
   - API create/edit without `creditPolicyMode`: create → `LegacyAdvisory` (an ambiguous request stays
     ambiguous, never silently enforced or unlimited); edit → keeps the stored mode. The web app
     always sends an explicit mode, and offers `LegacyAdvisory` only for a customer already on it.
   - `Enforced` requires `creditLimit > 0`; `NoCredit` is "no due may be added", whatever the limit.
2. `CustomerCreditPolicy.Evaluate(state, currentExposure, newDue, override, actorRole)` — pure,
   `ErrorOr<CreditDecision>`:
   - `newDue <= 0` → allowed (cash sales are never blocked by credit settings).
   - inactive → `Customer.CreditCustomerInactive` (no override).
   - `NoCredit` → `Customer.CreditNotExtended` (no override: the owner changes the policy).
   - `LegacyAdvisory` → allowed; a sale warning when it exceeds a positive saved limit.
   - `Enforced`: `exposure + newDue <= limit` → allowed. Else an override is required:
     Staff → `Customer.CreditOverrideNotPermitted` (403); stale override
     (`exposure > observedExposure`) → `Customer.CreditOverrideStale` (409); otherwise allowed and
     audited. No override → `Customer.CreditLimitExceeded` (409).
   - Exposure = signed ledger balance (plan 25 model: unapplied payments and reversals counted once;
     a customer in credit has negative exposure).
3. Serialization: `RecordSale` locks the customer row (`FOR NO KEY UPDATE`, 5 s, then
   `lock_timeout` back to default) as the first lock of the posting transaction when the sale adds
   due, then re-reads active/policy/limit and the ledger balance and evaluates. Order becomes
   customer → invoice sequence → credit notes → batches, consistent with returns and payments
   (customer first). Customer edits are a plain `UPDATE`, which conflicts with the same lock, so the
   evaluation always sees committed settings. Busy → `Customer.CreditBusy` (409).
4. Override audit on the sale (`credit_override_reason`, `_by_user_id`, `_observed_exposure`,
   `_limit`); override is part of the idempotency hash (omitted when absent so old hashes hold), and a
   replay returns the stored sale without re-evaluating.
5. Due date: `Sale.DueDate = business date (IST) of issue + CreditTermsDays` when the sale has due and
   the customer has terms; otherwise null. Historical sales keep null (never invented).
   Overdue ⇔ `currentOutstanding > 0 && dueDate < asOfBusinessDate`.
6. Account projection gains `creditLimit`, `isActive`, `creditPolicyMode`, `creditTermsDays`,
   `availableCredit` (Enforced only), `openingBalance`, `closingBalance`, `asOfBusinessDate`,
   `overdueAmount`, optional `from`/`to` window; sales gain `dueDate`/`isOverdue`; ledger entries gain
   `signedAmount`. Opening = signed sum before `from`; closing = opening + window entries; order
   = entry date, created-at, id.
7. Customer list: the "overdue" status/count becomes "outstanding" (truthful); list rows carry the
   policy mode so legacy-advisory customers are visibly labelled.
8. Offline: bills already issued offline are never refused for credit. The poster evaluates the same
   policy (no lock; advisory) and, on a violation, posts the sale with a warning and a
   `CustomerVariance` reconciliation issue (`offline_sync.credit_policy_violation`), routing it to
   the review console. Offline credit cannot guarantee cross-device limits; this is stated, not hidden.

## Tasks

### Task 1 — truthful account projection
- [x] Unit tests (red): `account_returns_saved_limit` (limit 2000, revenue 20000, balance 500 → 2000,
      available 1500), `null_due_date_is_outstanding`, future/past due date overdue cases, opening 0 /
      closing 500 for the first sale, windowed opening includes prior facts.
- [x] Domain: `CreditPolicyMode`, `Customer.CreditPolicyMode/CreditTermsDays`, `Sale.DueDate`.
- [x] DTOs + handler + `GetCustomerAccountQuery` window; controller query params.
- [x] Angular: service types, page uses server limit/opening/closing/overdue; status util
      `outstanding`; i18n.

### Task 2 — serialized credit policy
- [x] Unit tests for `CustomerCreditPolicy` (RF4) and RecordSale handler wiring.
- [x] `ICustomerRepository.LockForCreditAsync`; errors; `RecordSaleCommand.CreditOverride`; hash.
- [x] Integration (RF3): balance 90, limit 100, two concurrent due sales of 10 → exactly one posts,
      final balance 100, one stock movement; staff override refused; manager override audited.
- [x] Add/edit customer commands + validators take mode/terms; Angular overlays.

### Task 3 — terms, offline behaviour, reconciliation
- [x] Due date captured at issue (online + offline) from terms.
- [x] Integration (RF5): opening 500 + sale 100 − payment 200 − return 50 + reversal 200 = 550 and
      filtered-window opening.
- [x] Offline poster credit review issue.
- [x] POS: map credit error codes to messages; manager/owner override prompt with reason.
- [x] Migration `AddCustomerCreditPolicyAndSaleDueDates`; docs (`backend-architecture.md`, AGENTS.md).

## Verification
- `dotnet build src/backend/Intelibill.slnx`, unit projects, integration (Docker) for Sales/Customers.
- `bun run test`, `bun run build` in `src/frontend`.

## Rollout / rollback
Existing customers become visibly `LegacyAdvisory` (no behaviour change until the merchant chooses).
Rollback of code keeps the new columns harmless (nullable / defaulted). Financial facts are untouched.
Open product decision kept as recommended: override = Owner/Manager only; offline credit = review, no
per-device budgets yet.

## Review outcome

Independent review after implementation; addressed in the same branch:
- POS preview/approval went stale after a 409 → panel refetches and drops the approval on any `Customer.Credit*` refusal.
- Override audit stored the live balance → now stores what the approver saw.
- Customer credit-settings refusals untranslated; staff saw editable credit fields → mapped in 9 locales, fields read-only for staff.
- Blank override reason answered a generic validator code → `Customer.CreditOverrideReasonRequired` on every rule.
- Staff could reactivate a credit customer → reactivation of a customer whose policy allows credit is owner/manager only.
- `overdueAmount` could exceed what is owed → capped at the account balance; `from > to` refused.
- Reviewer-approved held offline sale reopened a credit case → `CreditReviewed` skips it.

Known limits (documented, not changed): ledger entry dates are UTC dates vs IST windows; offline due
posts without the customer lock, so the limit is guaranteed between online sales only.
