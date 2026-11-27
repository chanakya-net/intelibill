# P03-08 — Renew early or in grace from the original anchor

Simulator and fixed-clock evidence only. No real payment provider is contacted and nothing is enabled in production.

## Behavior-first evidence

- Red (before product code): `dotnet test tests/backend/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj --filter 'FullyQualifiedName~P0308Tests&Category!=Browser'` — `EarlyRenewalKeepsMonthEndAnchorAndLosesNoPaidTime` failed: the renewal quote for a paid term bought through checkout answered `400 Billing.CheckoutInvalid`.
- Red (effective time): with the previous `RenewalPolicy`, `GraceRenewalVerifiedLateUsesCaptureTimeAndAddsGraceOnce(5)` recorded `grace_days_included = 5` (observation time) instead of `3` (capture time).
- Red (web): `subscription-renewal-preview.component.spec.ts` failed to resolve the missing component.

## Coverage (`tests/backend/licensing/integration/P0308Tests*.cs`, real PostgreSQL)

- Month-end anchor 31 Mar 2027 10:00 IST: early renewal on 20 Apr → 30 Apr–31 May, then 31 May–30 Jun; shop stays Active from 31 Mar with paid-through extended and grace only after the tail.
- Leap-day annual anchor 29 Feb 2028: renewals end 28 Feb 2030, 28 Feb 2031, 29 Feb 2032.
- Imported V1 paid term: renewal quote digest equals the V1 source digest; successor keeps the 31 Jan anchor.
- Grace: capture 2 days 3 hours into grace, verified 5 or 10 days after paid-through → accepted period, 3 grace days recorded, grace added once.
- Window: acceptance at exactly paid-through + 7 days is `Billing.CheckoutExpired` even though the quote has not expired; start refused the same way.
- Races: manual vs online orders for one predecessor → one successor, losing online funds `Verified`/`NeedsReview`; four concurrent verifications → one fulfilment.
- Delayed manual receipt judged by `receivedAt`.
- Migration: populated #869 schema down/up retains facts; downgrade refused (55000) after a renewal; renewal provenance immutable.

## Browser (`P0308BrowserTests`, shipping merchant build, Chromium)

`P0308_RUN_BROWSER=1 P0308_EVIDENCE_DIR=<dir> dotnet test tests/backend/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj --filter 'FullyQualifiedName~P0308BrowserTests'`

The owner creates the renewal quote in the checkout page on 2 May 2027 13:00 IST, sees previous paid-through `30 Apr 2027, 10:00:00 IST`, new end `31 May 2027, 10:00:00 IST` and the 3-grace-day disclosure (desktop and mobile), accepts, pays through the development simulator, sees `Payment confirmed` / `Applying access to 0 of 2 shops`, and after signed merchant acknowledgement `Access applied to all 2 shops`. `build-and-postgres.json` records build ID, SHA, predecessor/successor rows and the single successor.
