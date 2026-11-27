# P05-08 · Refreshing offline permission on reconnect

A disconnected till keeps the deadline signed into its current permit. The merchant trading-authority read can justify a reservation or explain why renewal is unavailable. Only a pin-verified permit from the candidate lease's completed snapshot can apply a commercial extension to future issuance. A ping is a reachability signal.

## Observable reconnect

The release scenario `unseen renewal extends nothing; a verified reconnect updates future issuance` uses the production merchant build, protected worker, offline pack, Chromium Web Locks, WebCrypto and persistent IndexedDB.

1. Enable offline billing and disconnect. Issue `A/2627/000001` with the first permit.
2. Change the fixture's upstream authority while disconnected. Advance the device beyond the first signed deadline and reload. Checkout displays the ended-permission message. The original lease remains in IndexedDB and the fixture has received zero renewal reservations.
3. Restore the network. The pending invoice's sync request carries its original compact token. After the API becomes reachable, one scoped reservation and its bound stream deliver a second signed permit. Manage Shop displays the signed authority instant and deadline in IST. The earlier invoice's serialized payload is unchanged.
4. Disconnect again. Checkout issues `A/2627/000101` under the new lease. The fixture's extension is measured in minutes.

## Reproduction before the implementation

At baseline `828464f36c97776e432b914ae35386547010b5dd`, `cd src/frontend/intelibill && bun run audit:ui:release offline-permit-refresh.spec.ts` reached the reconnect assertion. After 25 seconds Chromium still held lease `11111111-1111-4111-8111-111111111111`. The assertion expected `11111111-1111-4111-8111-111111111112`. One test failed, exit 1.

On the same product source, `cd src/frontend/intelibill && bun run test --run invoice-lease-indexeddb` produced 22 passes and one failure. A later issue timestamp allowed an authority observation one microsecond older to overwrite the existing permit. The test expected a refusal and received the incoming record.

During implementation, removing the test's repeated synthetic online events exposed a second failure. Reachability could return after the pack check had already run. The successful reconnect scenario now uses the browser's network transition. A till-state regression also reproduced the open checkout retaining an exhausted lease after another tab replaced it. Checkout now reads the durable settings and usable snapshot before the finalization guards run.

## Reservation and ordering rules

| Observation | Browser action |
| --- | --- |
| Missing pin, invalid held evidence, disabled device, wrong owner, unavailable Web Locks, ineligible role or known suspension | No refresh reservation |
| Stale, malformed or unavailable trading authority | Keep the issued permit and its signed deadline |
| Fresh, scoped commercial denial | Show the refusal. Security release remains with the existing trading-authority interceptor |
| Later verified authority while the range is usable | Keep the range at an equal or shorter achievable end. A fixed commercial extension can refresh immediately. A sliding 48-hour horizon must gain at least 24 hours before reserving |
| Local limits | Cap the preview at the current fiscal boundary (April 1 at midnight IST), refresh credential expiry, device freshness and the held till-day end for the same till. A till switch on a usable grant requests a bound snapshot on its existing range |
| Server-only stock cap | If the signed candidate cannot advance a usable permit, keep the current snapshot and lease. The persisted reservation delay covers the spent request |
| Missing, refused, wrongly bound, untrusted or reordered candidate | Discard the candidate. The previous permit, clock and usable snapshot remain available inside their own bounds |
| Verified replacement | Promote the completed snapshot, compare and swap the lease and permit in IndexedDB, publish the signed times and rebase the issuance clock |

Reservations consume fiscal invoice numbers. The API accepts 1–1,000, defaults to 200, and refuses a range that would overflow its series. Automatic refresh requests the remaining count bounded to 25–200, using the existing low-range threshold. It checks for an unsettled claim before spending. A lease-scoped retry delay is persisted **before** the request so a reload after a lost response cannot immediately spend another range. The delay starts at 30 minutes and doubles up to 12 hours. Successful adoption clears it. The previous lease's unused numbers can become gaps.

The candidate must match the active shop, device, user, authentication session, till, requested grant and completed snapshot. Its signed authority must be later than the held observation and at least as recent as the preview. Issue time, revision ordering, signed bounds and the previous permit bytes are checked before replacement. The new permit obtains its deadline from the signed bytes. Issuance continues to enforce its existing clock and business checks.

## Browser failure and concurrency evidence

- A stale signed observation, regressed revision, unknown signing key, different session or stream refusal retains the old lease, clock, usable snapshot ID and contact timestamp. Reload retains the reservation delay. An offline checkout still issues the first number under the original permit.
- An earlier preview, verification outage and commercial denial spend zero ranges. A reservation refusal spends one request and starts no candidate stream. The status describes the outcome.
- Reload during a held stream keeps the prior lease and retry delay. Two reconnecting tabs spend one block. The other till can issue against the old lease while the stream is held. Its pending payload survives the eventual replacement.
- The snapshot and lease belong to separate IndexedDB databases. The test aborts the lease write after the snapshot commit. After reboot the snapshot ID differs from the old permit's signed snapshot ID. Checkout refuses, the cursor stays put, and the queue is empty. A complete snapshot alone cannot authorize a sale.
- A persisted security suspension survives a reload and an unenforced 200 from the trading-authority endpoint. The confirmation marker stays absent, checkout stays unavailable, and there are no reservation or candidate-stream requests.

## Review regressions and recovery

On reviewed commit `a463a036cf6d5ef4fc95589b93544ff62993ee07`, the new regressions produced six failures and 80 passes. Equal-end authority and a sub-day freshness gain both requested another block. Rollover into an occupied fiscal key raised an IndexedDB constraint error. A rejected permit check skipped stale-stock upkeep. Malformed settings and a disabled, restricted device both exposed an invalid notice. A settings test also reproduced a stored string `"false"` enabling the device. A separate interrupted-rollover test reproduced a refused swap where the destination already held the candidate and had advanced its cursor.

- Repeated Chromium polls at an equal end, the same till-day end, the March fiscal boundary, the refresh credential end and a sub-day sliding gain spend zero blocks. A changed till receives a bound permit on the existing range. Healthy contact clears an earlier transient warning while the signed contact and deadline remain authoritative.
- The stock-cap scenario spends one reservation, receives a candidate with the old end, and retains the usable snapshot and retry delay. Exhaustion, expiry or a changed auth session can require a new range even when the old end cannot improve. A usable candidate still requires later signed authority.
- After aborting the candidate lease write, the next online cycle repairs the snapshot/permit mismatch with a bound stream on the held lease. Checkout works again on `A/2627/000001` inside the original deadline, including during the reservation retry delay. After an interrupted settings write, reload recovers the signed replacement from IndexedDB and issues `A/2627/000101`. A missing clock checkpoint requires a fresh bound stream. Reboot alone never rebases the clock.
- Fiscal rollover retires the old permit and writes the destination in one transaction. Competing calls produce one winner. An outstanding claim prevents replacement, and recovery preserves numbers consumed in an existing destination range.

## Server boundary

The unchanged P0502 and P0402 integration classes ran against Testcontainers PostgreSQL with their fixed clocks and local signing fixtures.

| Named case | Evidence |
| --- | --- |
| `Newer_snapshot_alone_never_refreshes_stale_licensing` | Later stock snapshots and revision 2 preserve the original grant's November 22 deadline. At the boundary the old lease refuses. A new reservation adopts revision 2 and the November 24 deadline |
| `Lease_and_snapshot_evidence_agree_for_each_reachable_deadline` | Eight reachable cap/tie cases verify the signature and binding one microsecond before the end. At the exclusive end the stream refuses the permit and evidence counts stay fixed |
| `Concurrent_bound_snapshots_stay_bound_to_their_own_lease` | Four concurrent streams for one shop and two for another keep their own lease, grant, device, snapshot IDs and deadlines |
| `Restriction_before_completion_withholds_the_permit` | A restriction introduced while the stream is held prevents permit delivery at completion |
| `Fallback_reservations_stay_legacy_and_refusals_roll_back` and `Unbound_stream_is_unchanged_and_wrong_scope_writes_nothing` | Compatibility fallback issues no signed authority. Refused reservations and wrong-scope requests leave no new authority evidence |
| `Local_trading_authority_reports_503_separately_from_known_expiry` | The merchant API distinguishes verified, unavailable, expired, foreign-scope and invalid-session responses. Responses are no-store and the fixture transport sees zero calls |

Browser routes simulate the merchant API and use a throwaway RSA key. The server suite exercises real persistence with synthetic verified projections. Neither suite contacts a live licensing or payment provider. Browser clock evidence detects inconsistencies within its supported model.

## Original implementation verification from the issue worktree root

All six commands were applicable and exited 0. Build output includes Angular bundle and stylesheet budget warnings.

```sh
cd src/frontend/intelibill && bun run test --run offline-permit-refresh invoice-lease-indexeddb offline-sales-snapshot-sync offline-licence-permit offline-pack-update manage-shop-offline manage-shop-overlay offline-sales-device-settings src/app/core/i18n/i18n-coverage.spec.ts
```

11 files, 191 tests passed.

```sh
cd src/frontend/intelibill && bun run test --run offline-sale-finalization offline-sale-state offline-permit-clock offline-sales-device-enablement offline-sales-queue offline-sale-sync.mapper offline-recovery-bundle
```

9 files, 194 tests passed.

```sh
cd src/frontend/intelibill && bun run test --watch=false
```

344 Angular test files, 3,540 tests passed. The script suite also passed all 107 tests.

```sh
cd src/frontend/intelibill && bun run build
```

Merchant production, worker and server builds completed. Both offline pack manifests were written.

```sh
cd src/frontend/intelibill && bun run audit:ui:release offline-permit-refresh.spec.ts offline-permit-finalization.spec.ts offline-permit.spec.ts offline-pack.spec.ts legacy-queue-recovery.spec.ts
```

27 Chromium release scenarios passed, including all 14 reconnect scenarios.

```sh
dotnet test tests/backend/intelibill/integration/Intelibill.Integration.Tests --filter 'FullyQualifiedName~P0502LicensingTests|FullyQualifiedName~P0402LicensingTests'
```

41 PostgreSQL integration cases passed, zero skipped.

## Modifier verification

The focused frontend contract suite passed 213 tests in 11 files. The checkout and recovery regressions passed 194 tests in 9 files. The full merchant suite passed 3,562 Angular tests and 107 script tests. The merchant production build completed with budget warnings. The five release specs passed 35 Chromium scenarios, including 22 reconnect cases. The P0502 and P0402 PostgreSQL suites passed all 41 cases with zero skips.

The review-specific Vitest filters passed 32 tests for permit refresh, 58 with Manage Shop and device settings, 76 with IndexedDB lease replacement and pack upkeep, and 50 with pack upkeep and permit refresh.
