# Licensing P02-12: dated complimentary access

A Licensing Manager can give named shops of one billing account explicit, time-limited courtesy access. A grant
records no payment, invoice, price or revenue, and no grace period follows its end. Phase 2 stays observe-only:
nothing here blocks or unblocks any merchant work.

## What a grant is

- One billing account, 1 to 100 distinct shops already affiliated with that account, an explicit start and an
  end-exclusive end (ISO 8601 with an offset; at most microsecond precision), a member allowance of 1 to 100000 and
  a reason of 1 to 500 characters. One shop gives `Independent` coverage with capacity 1; several give `Grouped`
  coverage with capacity equal to the named shops. There are no floating slots and no defaults.
- The start is never moved: an already-started interval keeps its recorded start. A new grant must end in the future.
- Windows for one shop never overlap (adjacent windows are allowed); disjoint shops are independent.
- Policy `complimentary-access/1`. Contract: `tests/contracts/licensing/complimentary-grant-v1.json`; the status
  example is `complimentaryExample` in `subscription-status-v1.json`.

## Source precedence (one evaluation instant, no scheduler)

`ComplimentaryAccessPolicy.Select` runs inside the shared `SubscriptionStatusProjection`, so merchant and provider
show the same result:

1. A baseline (affiliation or trial) term that is suspended, incomplete or needs review always stands.
2. A baseline term currently supplying Active or Grace access wins; a grant never replaces its price, allowance,
   dates, trial use or grace clock, and capacities never add up.
3. Otherwise a grant covering the instant is shown, with its frozen dates and allowance and no grace.
4. Otherwise the next scheduled source, then the latest ended one.

Security suspension is applied to whichever source is chosen, so a grant cannot conceal it, and releasing it
reveals only the original unelapsed interval. This base has the pure suspension seam only (contract vectors);
there is no production suspension writer yet. The real-runtime proof is that merchant sessions, shop roles and
existing product gates are unchanged while a grant is active and at its end.

## Provider routes (real provider session only)

- `POST /api/admin/accounts/{accountId}/grants/complimentary/preview` — read-only; returns canonical facts, each
  named shop's current status, the account grant `expectedVersion` and a `sourceHash` binding the preview.
- `POST /api/admin/accounts/{accountId}/grants/complimentary` — `operationKey`, the same facts, `expectedVersion`,
  `sourceHash` and `confirmed: true`. 201 when written, 200 for an exact replay by the same administrator.
- `GET /api/admin/accounts/{accountId}/grants/complimentary/operations/{operationKey}` — the original outcome.

Persisted Licensing Manager only (Platform Owner, Security Administrator, Support and Billing alone are refused);
confirm and recovery also need MFA within five minutes. Writes need the admin origin, CSRF proof and strict JSON
(16 KiB); a pinned `X-Admin-Session` must match the cookie session. Authority is re-read under lock before any
receipt is disclosed, after the account fence wait and before commit. Errors: `Grant.Invalid`,
`Grant.ShopNotAffiliated`, `Grant.AlreadyEnded` (400), `Grant.AccessDenied`, `Grant.RecentMfaRequired` (403),
`Grant.OperationNotFound` (404), `Grant.Overlap`, `Grant.VersionConflict`, `Grant.PayloadMismatch`, `Grant.Busy`
(409), `Grant.Unavailable` (503).

Lock order: provider identity → session → billing account fence (shared with trial activation) → per-shop guard.
A committed replay is answered before any current-time or version check, so recovery works after expiry.

## Persistence

Migration `20261116180000_AddComplimentaryAccessGrants` (SQL-owned, no EF model change) adds
`complimentary_grant_scopes` (per-account revision; only advances by one), `complimentary_grants` (operational),
`complimentary_grant_receipts` (provider-only: administrator, session, operation key, reason, digests) and
`complimentary_grant_shops` (named shops frozen to their affiliation). All are forced-RLS: writes need the
transaction-local provider grant context; account-scoped reads see operational rows only; a named shop sees only its
own grant. Grant, receipt and link rows refuse update/delete/truncate; a deferred check requires the receipt, every
named link and the revision at commit; a grant's coverage term is reserved from affiliation assignment.

Apply the migration through the migration job before deploying API or web. Down refuses once any grant or revision
exists (`55000`); a rollback keeps schema and facts.

## Web

- Provider Console → Account access status: Licensing Managers get the grant editor (IST date-times, preview,
  explicit confirmation). Any edit, including one made while a preview is still loading, discards that preview, so
  only a preview of the current values can be confirmed. The exact confirmation is stored in `localStorage` before
  sending, one record per administrator, account and operation key; if that fails nothing is sent. A tab never sends
  while another operation for the same administrator and account is unresolved in this browser: it shows that
  original operation instead. A lost response or lookup 404 keeps the original key: "Check grant outcome" and "Retry
  original grant" only. A 4xx to the first send is definitive and clears only that operation's record.
- Both apps show courtesy terms as "Courtesy access ends" and "Included capacity", to the exact second (and any
  recorded fraction) in IST, never "Paid through", a price or a grace end.

## Verification (local fixtures only)

```sh
dotnet test tests/backend/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj --filter 'FullyQualifiedName~P0212Tests'
P0212_RUN_BROWSER=1 P0212_EVIDENCE_DIR=<dir> dotnet test tests/backend/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj --filter 'FullyQualifiedName~P0212BrowserTests'
```

The browser demo builds nothing itself: build `src/frontend/intelibill/licensing` and `src/frontend/intelibill/intelibill` first. It uses real Kestrel
hosts, PostgreSQL, provider password + TOTP and merchant sign-in, and fixes only the licensing lifecycle clock. No
live payment provider, production enforcement or entitlement delivery is exercised.
