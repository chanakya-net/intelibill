# P07-01 Acceptance Record — Plan Role-Correct Licence Notices

## Scope & Fresh-Start Baseline

- Issue: `#924` (Parent PRD `#829`, Slice `P07-01`).
- Fresh-start database only; no legacy shop migration, backfill, or complimentary transition paths.
- Local simulator payments and local fixtures support notice scheduling, redaction, and visibility only; no live email delivery (`#925`), send-time invalidation (`#926`), or production activation is claimed.

## Observable Demo & Verification Coverage

1. **Trial thresholds (`2028-02-15T04:30:00Z` / 10:00 IST)**:
   - Preplanned `TrialMinus3Days` (`2028-02-12T04:30:00Z`) and `TrialMinus1Day` (`2028-02-14T04:30:00Z`), zero paid grace.
   - Verified `-1 µs`, equality, and `+1 µs` due read boundaries.
2. **Paid & Grace thresholds (`2028-02-29T04:30:00Z` / 10:00 IST)**:
   - Preplanned `PaidMinus7Days` (`2028-02-22T04:30:00Z`), `PaidMinus1Day` (`2028-02-28T04:30:00Z`), `GraceStart` (`2028-02-29T04:30:00Z`), `GraceMinus1Day` (`2028-03-06T04:30:00Z`), and `GraceExpiry` (`2028-03-07T04:30:00Z`).
   - Verified concurrent planner convergence across independent DB connections (`pg_stat_activity` lock overlap), host restart replay, routine heartbeat invariance, and role-removal race exclusion.
3. **Merchant Privacy (`MerchantNoticesFollowLiveShopAndBillingAuthority`)**:
   - Ordinary shop staff and non-billing shop owner receive own-shop operational guidance and deadline only (`Cache-Control: no-store`); zero price, currency, account ID, or other-shop leakage in raw HTTP JSON, DOM, or accessibility tree.
   - Shop switch `North Shop -> South Shop -> North Shop`, stale in-flight response rejection, reload, and logout verified in real Chromium against the shipping merchant bundle.
   - Billing-only manager sees Account A commercial notices on `/billing/accounts/{accountA}/status` and receives `403` on shop operational notices and Account B.
   - Verified opted-in billing contact has persisted `CommercialContact` (`Email`) jobs in PostgreSQL and receives `403` on account and shop notice reads.
4. **Provider Preview (`ProviderPreviewUsesLiveCapabilityAndShowsScheduledState`)**:
   - Separate admin origin renders Billing commercial preview (UTC + IST, due vs future badges, evidenced term price, masked destination hint), Support redacted preview (zero commercial details), and denies Security-only direct reads (`403`).
   - Verified keyboard focus order, localization switch across nine locales, `390px` responsive viewport with zero horizontal overflow, non-writing refresh, and immediate stale-row clearing on mid-session capability removal.

## Open Follow-Up Dependencies

- Standalone address-only contact mailbox proof delivery depends on email transport delivery (`#925`); licensing generates and enforces the single-use SHA-256 verification proof token today, while SMTP dispatch is owned by `#925`.
- Send-time cancellation/invalidation of already-planned jobs after subsequent payment/closure/revocation is owned by `#926`.
