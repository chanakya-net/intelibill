# P02-09: calendar access status and exact deadlines

Licensing evaluates one access status from stored facts and returns it to both
the merchant and the provider. The status is observe-only: it never blocks a
sale, stock posting, membership or any other merchant action.

## Facts and their owners

| Fact | Where it lives | Writer |
| --- | --- | --- |
| Term dates, coverage mode, purchased capacity | `subscription_coverage_terms` (P02-05) | Existing coverage seeding; future lifecycle slices |
| Shop to account and term | `shop_affiliations` (P02-05) | Owner consent delivery |
| Basis, period, local anchor and ordinal, policy versions, member allowance, selected price | `subscription_term_facts` (this slice) | None yet. Trial, grant, renewal and payment slices add their own insert policy and writer |

`subscription_term_facts` is append-only. The runtime role can read it under an
account or shop scope but cannot insert, update, delete or truncate it. Check
constraints refuse unknown bases, non-IST zones, fractional-second anchors,
missing paid prices, negative prices and zero allowances. A zero price is an
explicit price. A missing value is never treated as unlimited.

## Time rules (`tests/contracts/licensing/time-v1.json`)

- `BillingCalendar` (`calendar-month-year/1`): boundaries come from the retained
  `Asia/Kolkata` anchor plus an ordinal. 31 Jan 10:00 IST runs to 28 Feb and then
  31 Mar. 29 Feb annual runs to 28 Feb in common years and 29 Feb in 2028.
- Every interval is `[start, end)`. An instant equal to an end is outside it.
  Instants keep PostgreSQL microsecond precision.
- `GracePolicy` (`access-grace/1`): only a complete Paid term gets grace, exactly
  seven elapsed days after paid-through. Trial, courtesy, temporary, transition,
  incomplete evidence and an effective cancellation at the boundary get none.
  Withdrawing the cancellation before the boundary keeps the original grace.
- Frozen `calendar_version` selects the anchor rules and `access_policy_version`
  selects the paid-grace rules (`GracePolicy.Version`, currently `access-grace/1`).
  Unknown versions or unrepresentable paid calendar boundaries produce
  `NeedsReview`. Stored period ordinals are limited to 0–1200.
- `SubscriptionAccessPolicy` (`subscription-access/1`): Scheduled, Active, Grace,
  Expired, Suspended, EvidenceIncomplete, NeedsReview or PendingActivation.
  Suspension overrides the label but never moves a deadline. A release reveals
  only the unelapsed interval.
- `RenewalPolicy` (`renewal/1`) is a pure decision seam with no writer. A first
  purchase or trial conversion starts fresh at fulfilment. An on-time renewal
  continues the old anchor from paid-through and reports the grace days it
  includes. On-time means fulfilled before the grace end, or verified as paid
  before it with an accepted quote. A renewal after restriction starts fresh at
  restored access. Missing or contradictory evidence is `Unresolved`.

A Paid supplement whose anchor and ordinal do not reproduce the coverage dates
reads `NeedsReview`, with no basis and no grace.

## Routes and visibility

| Caller | Route | Sees |
| --- | --- | --- |
| Live member of the active shop, any shop role | `GET /api/licensing/shops/{shopId}/status` | Operational status. Adds `accountId` and `commercial` only when the same person is that account's live billing Owner or Manager |
| Billing Owner or Manager, with or without a shop | `GET /api/billing/accounts/{accountId}/subscriptions?after=` | Every term with commercial facts and covered shop names, 50 per page |
| Provider Support | `GET /api/admin/accounts/{accountId}/status?after=`, `.../shops/{shopId}/status` | Operational status, no price or account identity |
| Provider Billing or Licensing Manager | Same routes | Operational status plus commercial facts |
| Platform Owner or Security Administrator alone | Same routes | 403 `Status.AccessDenied` |

The merchant checks the session's active shop and live membership before it
signs the request, and again before it returns the answer. The signed delegation
names the action `licensing.shop-status.read` and the canonical path for that
shop. The shop read is actor-scoped. Licensing sets a transaction-local
`app.status_shop_id`, reads only that shop's affiliation, and derives the
account on the server. It then locks the actor's own billing membership with
`FOR SHARE` before projecting commercial fields. Provider reads re-check the
live identity, session and grant under the identity-then-session lock order.
Redaction happens on the server.

`evaluatedAt` is the single instant used for the response. The web apps render
every instant as absolute date and time in IST, whatever the browser zone is.
They clear old data on scope change or failed verification and drop late answers.
[P02-14 synchronization status](licensing-p02-14-synchronization-status.md)
reports the saved policy and merchant confirmation separately.

## Verification

```sh
dotnet test tests/backend/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj --filter 'FullyQualifiedName~P0209Tests'
P0209_RUN_BROWSER=1 P0209_EVIDENCE_DIR=<dir> dotnet test tests/backend/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj --filter 'FullyQualifiedName~P0209BrowserTests'
```

Build both web apps first. The browser run uses local fixture terms and prices,
real merchant and provider sign-in, real PostgreSQL and a fixed status clock in
the test host only. It does not verify a live payment provider, production
prices, entitlement delivery or enforcement.
