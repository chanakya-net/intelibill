# P07-01 Acceptance Record — Plan Role-Correct Licence Notices

## Scope & Fresh-Start Baseline

- Issue: `#924` (Parent PRD `#829`, Slice `P07-01`).
- Fresh-start database only; no legacy shop migration, backfill, or complimentary transition paths.
- Local simulator payments and local fixtures support notice scheduling, redaction, and visibility only; no live email delivery (`#925`), send-time invalidation (`#926`), or production activation is claimed.

## Observable Demo & Verification Coverage

1. **Trial thresholds (`2028-02-15T04:30:00Z` / 10:00 IST)**:
   - Preplanned `TrialMinus3Days` (`2028-02-12T04:30:00Z`) and `TrialMinus1Day` (`2028-02-14T04:30:00Z`), zero paid grace.
   - Verified `-1 µs`, equality, and `+1 µs` due read boundaries, trial extension hiding superseded deadlines from current merchant views while retaining original rows in PostgreSQL, and converted trials excluding converted-trial thresholds at and after the conversion instant.
2. **Paid & Grace thresholds (`2028-02-29T04:30:00Z` / 10:00 IST)**:
   - Preplanned `PaidMinus7Days` (`2028-02-22T04:30:00Z`), `PaidMinus1Day` (`2028-02-28T04:30:00Z`), `GraceStart` (`2028-02-29T04:30:00Z`), `GraceMinus1Day` (`2028-03-06T04:30:00Z`), and `GraceExpiry` (`2028-03-07T04:30:00Z`).
   - Verified concurrent planner convergence across independent DB connections (`pg_stat_activity` lock overlap), fresh-DI-scope replay against persisted PostgreSQL state, routine heartbeat invariance, commit-time deadline/affiliation fence rejection, role-removal race exclusion, and bounded discovery skipping expired-only accounts with zero merchant HTTP calls.
3. **Authority, Contact Privacy & Bounds**:
   - Raw contact approval and paged list responses omit mailbox proof tokens; signed-in contacts enable `Email` only via self-consent with replay-safe operation keys and append-only `OptedOut` history; address-only approvals stay pending without `#925` mailbox transport.
   - Signed merchant recipient replies require exact `(shop, account, affiliation)` evidence and omit raw phone numbers and staff emails on the wire. Single-character email local parts are always masked (`***@domain`). Sequential member/contact overlap keeps one semantic email intent and serves authorized in-app access. Large multibyte rosters and multi-page contact lists stay within delegated HTTP bounds.
4. **Merchant Privacy (`MerchantNoticesFollowLiveShopAndBillingAuthority`)**:
   - Ordinary shop staff and non-billing shop owner receive one coherent current operational guidance and deadline (`Cache-Control: no-store`, instructing them to contact the billing owner/manager rather than renew); zero price, currency, account ID, or other-shop leakage in raw HTTP JSON, DOM, or accessibility tree.
   - Verified fixed-clock progression through every paid and grace due boundary (including one coherent `Expired` status at grace expiry), deterministic stale in-flight shop reply rejection across `North Shop -> South Shop -> North Shop`, reload, and logout in real Chromium against the pinned Bun 1.3.11 shipping merchant bundle.
   - Billing-only manager sees Account A commercial notices on `/billing/accounts/{accountA}/status`, partial-suspension safe guidance (`paymentDoesNotLiftSuspension: true`, no promise of uninterrupted trading), deterministic stale commercial Account A reply rejection after in-SPA navigation away (`/billing`), zero commercial leakage on unauthorized Account B status view (`403`), cleared superseded deadline after renewal, and `403` on shop operational notices.
   - Verified opted-in billing contact has persisted `CommercialContact` (`Email`) jobs in PostgreSQL and receives `403` on account and shop notice reads.
5. **Provider Preview (`ProviderPreviewUsesLiveCapabilityAndShowsScheduledState`)**:
   - Separate admin origin renders Billing commercial preview (exact UTC + IST due instants, due vs future badges, evidenced term price, masked destination hint), Support redacted preview (zero commercial details), and denies Security-only direct reads (`403`).
   - Verified keyboard focus order, browser language switch (`hi-IN` and `en-IN` in Chromium, paired with automated 9-locale unit asset completeness checks for both merchant and admin apps), `390px` responsive viewport with zero horizontal overflow, non-writing refresh, and deterministic stale commercial response rejection after mid-session capability removal.

## Open Follow-Up Dependencies

- Standalone address-only contact mailbox proof delivery depends on email transport delivery (`#925`); licensing stores and enforces the single-use SHA-256 verification proof hash via `IBillingContactMailboxProofDispatch` (no-op in production until `#925`), keeping address-only approvals pending without exposing secrets to approvers.
- Send-time cancellation/invalidation of already-planned jobs after subsequent payment/closure/revocation is owned by `#926`.
