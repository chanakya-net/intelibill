# P05-09 Bounded Offline, Clock, and Legacy Recovery Acceptance

This acceptance record closes slice **P05-09** ([#908](https://github.com/chanakya-net/intelibill/issues/908)) under parent PRD [#827](https://github.com/chanakya-net/intelibill/issues/827) (InteliBill Licensing P05 — Bounded Offline Access and Historical Recovery, user stories 1–10).

## Environment, Baseline, and Evidence Boundaries

- **Parent baseline SHA**: `f618d53f85dbec2a1cc0c5bfa8dc16eb21c63c86` (includes `#900`–`#907`).
- **Revisions of this record**: first published at `57d75a7147eac82518573301980b017b59ebd66c`. This revision answers review cycle 1 (`R001`–`R009`): the test sources changed (monotonic suspension cutoff, first-receipt comparison across a later replay, refusal reasons per review action, a browser case for a security-barred upload), so every scenario, hash and command count below was measured again on `2026-11-23` between `15:04Z` and `15:25Z` from the test sources of this revision. The red control in the next section is the one historical observation kept from the first publication. Identifiers, digests and timestamps are per-run values and differ on every rerun; the invariants are what a rerun must reproduce.
- **Verification toolchain**:
  - `.NET SDK`: `10.0.300` via `/usr/local/share/dotnet` (satisfies `global.json` pin `10.0.105`, `rollForward: latestMinor`).
  - `Bun`: `1.3.14` (locked workspace dependencies via `bun install --frozen-lockfile`).
  - `PostgreSQL`: Docker `postgres:17-alpine` via Testcontainers and isolated Docker Compose.
  - `Browser`: Playwright Chromium with real service workers enabled (`serviceWorkers: 'allow'`).
- **Attribution and scope limits**:
  - **Loopback host (signed permits, expiry, suspension)**: the live browser post-expiry and missed-suspension runs use real Chromium, the built protected worker (`ngsw-worker.js` wrapping `ngsw-angular-worker.js`), real IndexedDB, the shipping Node merchant web server on a loopback port in front of real loopback Kestrel merchant and licensing APIs, two separate PostgreSQL databases (Testcontainers), signed HTTP outbox/inbox delivery, and a disposable test RSA key pinned into a merchant bundle built for the run. No route interception is installed in these runs. The server's verdict on an upload is read from the IndexedDB row the application wrote from the real response.
  - **HTTP fixtures (clock, renewal, security-barred upload, legacy queue, print renderer)**: the exact millisecond boundary, persistent clock rollback, renewal, security-barred upload and legacy-upgrade scenarios run the built merchant bundle and its real worker and IndexedDB in Chromium, but against local HTTP fixture servers and Playwright route answers with fixture RSA keys (`licensing-offline.spec.ts` + existing release specs). They show what the browser does with a given server answer; they do not show that a server gives that answer. The A4 and thermal invoice renderer is observed only here: Chromium renders the print route from the frozen local record with the network offline and `window.print` stubbed; no paper or PDF output and no server-side rendering is involved.
  - **Stock Compose (shipping images)**: the isolated Compose acceptance builds and runs the shipping Dockerfiles/Compose configuration under a disposable project name (`intelibill-p01-05-908-*`). It covers image builds, the explicit bootstrap/role/migration jobs, readiness, enrollment through Mailpit, merchant/admin origin separation, row-level security and restart persistence. Stock Compose builds with an empty offline public-pin map and no P05 signing key, so no signed offline permit, expiry or suspension behaviour is claimed from Compose; those are attributed to the loopback-host runs.
  - No live external payment/SMS/provider endpoint was contacted, no production signing keys or activation flags were enabled, and no deployment or GitHub publication was performed.

## Prerequisite Setup

Run from the repository root with `.NET 10` first on `PATH` and an absolute scratch directory for private browser/PostgreSQL diagnostics:

```bash
export PATH="/usr/local/share/dotnet:$PATH"
export P0509_EVIDENCE_DIR="$(mktemp -d /tmp/intelibill-p0509.XXXXXX)"
bun install --frozen-lockfile
(cd src/frontend && bun x playwright install chromium)
dotnet build src/backend/Intelibill.slnx
dotnet build src/licensing/Intelibill.Licensing.slnx
(cd src/frontend && bun run build)
(cd src/admin-frontend && bun run build)
```

Run the two integration wrappers (`scripts/dev/test-integration.sh --licensing ...` and `scripts/dev/test-integration.sh ...`) sequentially because both generate and delete `src/backend/Intelibill.Api/Internal/Generated`.

## Behavior-First Control (Slice 1 Red Control vs Positive PostgreSQL Replay)

This is a historical observation from the first implementation of slice 1, kept as recorded. It ran on a working copy that was never committed: the live scenario then carried a temporary Playwright route that answered `POST /api/sales/offline-sync` with `{ "results": [] }` (the earlier `#907` fixture pattern). The committed sources contain no such route and no switch that restores it, so the control cannot be reproduced from a commit; its transcript and phase files are retained in private scratch and were re-read for this revision (failing invariant, counts, sale id, timestamps and exit code match the row below).

| Stage | Command | Exit | Browser Result | PostgreSQL Assertion (`sales` & `offline_submissions`) |
| --- | --- | --- | --- | --- |
| **Red control, historical** (`2026-11-23T13:28:06Z`–`13:28:33Z`, uncommitted working copy with the temporary empty-answer route) | `P0509_RUN_BROWSER=1 scripts/dev/test-integration.sh --licensing --filter 'FullyQualifiedName~P0509Tests.BrowserSaleRecoversAfterOrdinaryExpiry' --logger 'console;verbosity=normal'` | `1` | Playwright `1 passed (4.7s)` — browser queued `A/2627/000001` (`clientSaleId = 290ae32a-a79d-4332-a34f-7c3efa38e903`, `soldAt = 2026-11-23T13:28:33.060Z`, `validUntil = 2026-11-23T13:31:18.000000Z`), uploaded after `Licence.Expired`, received the empty answer and left the row `Failed` | **FAILED** on the invariant "exactly one posted sale exists for this `clientSaleId`": `Assert.Equal() Failure: Values differ — Expected: 1, Actual: 0` (`sales` count = `0`, `offline_submissions` count = `0`). In the committed sources this is the first assertion of `AssertPostedOnceAndReplayAsync` |
| **Positive, this revision** (`2026-11-23T15:04:22Z`–`15:05:18Z` inside the combined gate, and again `15:12:22Z`–`15:13:20Z`) | Commands 5 and 17 of the matrix below, no route interception | `0` | Playwright `1 passed` — real HTTP `200` from Kestrel `/api/sales/offline-sync`, IndexedDB row `Synced` with `serverSaleId` equal to the posted sale and unchanged payload bytes | **PASSED**: `1` posted sale, `1` `offline_submissions` receipt; a replay sent 30 s later returned `"duplicate"` and left receipt id, `received_at`, `payload_hash` and `payload_json` unchanged |

## Correlated Scenario Evidence

### 1. Live Browser Post-Expiry Recovery (`P0509Tests.BrowserSaleRecoversAfterOrdinaryExpiry`)

- **Merchant shop / billing account**: `shopId = b61eb169-e335-4df1-b362-d4d511588cc6`, `accountId = 70fc4dbf-cc31-8bd2-9b43-29ea8824e249`
- **Device & opaque provider reference**: `deviceId = 24998a36-e8f6-4965-be80-f18f61a3bb44`, `deviceRef = ed70abee29389c7a646333ffe63fd45fcf4a603ad3a504421ff485a21d6f5b76`
- **Permit / lease / grant**: `leaseId = 7c447d87-7592-472a-b9b6-1365f780ddde`, `grantId = a389ba11-d414-4fb9-ba37-0d7e02587467`, `permitId = 81f11794-523f-427f-ac64-6e4a8bdbd1f3`
- **Invoice & PostgreSQL rows**: `clientSaleId = f10f210c-28bd-4817-8afa-3ba7797f8326`, `invoiceNumber = A/2627/000001`, `submissionId = 88c58158-887b-4a65-ae2f-3b930f98ba6e`, `saleId = cf9b2a12-9229-4487-a12f-27755da8c464`
- **Measured timestamps & decisions**:
  - `authorityVerifiedAt`: `2026-11-23T15:04:55.000000Z` (`2026-11-23 20:34:55 IST`)
  - `notBefore`: `2026-11-23T15:05:03.720712Z` (`2026-11-23 20:35:03 IST`)
  - `validUntil` (`termEnd`): `2026-11-23T15:07:55.000000Z` (`2026-11-23 20:37:55 IST`), `limitingReason = LicenceEnd`
  - `soldAt`: `2026-11-23T15:05:05.263Z` (`2026-11-23 20:35:05.263 IST`, inside `[notBefore, validUntil)`)
  - `syncAt` (`received_at`): `2026-11-23T15:08:55.000000Z` (`2026-11-23 20:38:55 IST`, after commercial expiry)
  - `replayAt`: `2026-11-23T15:09:25.000000Z` (`syncAt + 30 s`)
  - Pre-sync lease reserve attempt at `syncAt`: `403 Forbidden` with `Licence.Expired`.
  - Browser reconnect upload at `syncAt`: `200 OK`; IndexedDB row `Synced` with `serverSaleId = saleId`; `1` sale (`total_amount = 50`, `invoice_document` populated), `offline_submissions.sale_id == sale.id`, `licence_permit_id == permitId`, `licence_grant_id == grantId`, `payload_hash == sales.request_hash`.
  - The signed permit on the wire is the one frozen into the queued invoice: SHA-256 of `wireSale.licenceEvidence.signedPermit` equals the queued token digest (`c40ef548…a229`, asserted).
  - Replay of the wire payload with both clocks at `replayAt`: `status = "duplicate"`, same `saleId`, `sales` count = `1`, `offline_submissions` count = `1`; the receipt kept its id, `payload_hash`, `payload_json` and `received_at = syncAt` (not `replayAt`).
  - Provider `GET /api/admin/accounts/{accountId}/device-permits` (`Cache-Control: no-store`): `disconnectedQueues = "Unknown"`, `knownPermitEnd = 2026-11-23T15:07:55Z`, `lastSynchronizedAt = 2026-11-23T15:09:25Z` (the device's last contact, which is the replay; the provider sees contact times, not receipts); `outstanding = true` at `termEnd - 1 µs` (`2026-11-23T15:07:54.999999Z`) and `outstanding = false`, `outstandingPermits = 0` at `termEnd`.

### 2. Stale 48-Hour Authority & Stock Snapshot Non-Extension (`P0509Tests.RefreshAndStockNeverExtendOriginalGrant`)

- **Shop / account / deviceRef**: `shopId = 0929acfb-d341-41fd-8e85-cdde2befbf4e`, `accountId = f5831d64-79cd-8000-a06a-b52bf79fe471`, `deviceRef = 2d7744577daa79f0c8306b884ce2fb5b6148caf797f3333ba81e821e0d9680b6`
- **Original verification & hard upper bound**:
  - `authorityVerifiedAt`: `2026-11-21T15:10:06.000000Z` (`2026-11-21 20:40:06 IST`)
  - `expectedEnd` (`authorityVerifiedAt + 48 h`): `2026-11-23T15:10:06.000000Z` (`2026-11-23 20:40:06 IST`), `limitingReason = AuthorityFreshness`
- **Non-extension proofs**:
  - Repeated `GET /trading-authority` reads and signed upstream snapshot replay left `validUntil == expectedEnd`.
  - Two additional stock intakes followed by full snapshot completions issued new permit IDs on the original grant, each still capped at `expectedEnd` (`AuthorityFreshness`).
  - Delivering a strictly fresher signed `Account + Shop` revision advanced the shop's `trading-authority` beyond `expectedEnd`, yet a subsequent snapshot stream on the original reservation/grant remained capped at `expectedEnd` (`permitsIssuedOnOriginalGrant = 5`).
- **Half-open PostgreSQL & provider boundary**:
  - `lastAllowedInstant = 2026-11-23T15:10:05.999999Z` (`expectedEnd - 1 µs`): stream `status = "issued"`, `validUntil = expectedEnd`; provider projection reported `outstanding = true`, `outstandingPermits = 5`, `knownPermitEnd = expectedEnd`, `disconnectedQueues = "Unknown"`.
  - `refusedInstant = 2026-11-23T15:10:06.000000Z` (exact `expectedEnd`): stream returned `status = "refused"`, `refusalCode = "OfflinePermit.DeadlineReached"` with zero additional `offline_license_permits` or `DeviceObservation` rows; replaying the stale snapshot on a second shop left `POST /api/sales/invoice-leases/reserve` failing with `503 ServiceUnavailable` (`Licence.VerificationUnavailable`) and zero lease rows; provider projection flipped to `outstanding = false`, `outstandingPermits = 0`.

### 3. Browser Exact Millisecond Deadline & Persistent Clock Rollback (`licensing-offline.spec.ts`, HTTP fixtures)

- **Exact millisecond half-open boundary (two independent Chromium contexts with pinned build and active protected worker)**:
  - `validUntil`: `2026-11-23T15:16:57.000000Z` (`2026-11-23 20:46:57 IST`, epoch `1795447017000 ms`).
  - Context 1 pinned at `validUntil - 1 ms` (`1795447016999 ms`): offline checkout succeeded and queued `A/2627/000001` (`nextNumber: 2`, `remainingCount: 99`, `claim: null`, raw payload SHA-256 `6df20c2d2f6c2a50e8ff33460f64bba0507c47c074f76bbcd92be60d0b8c5d6e`).
  - Context 2 pinned at exact `validUntil` (`1795447017000 ms`): offline checkout refused with `"Offline permission for this device has ended."` and IST notice (`[data-testid="offline-permit-ended"]`), zero queued rows, and byte-identical durable state (`nextNumber: 1`, `remainingCount: 100`, `claim: null`, `queueCount: 0`, unchanged shadow stock).
- **Persistent clock rollback latch**:
  - Issued `A/2627/000001` inside a valid 10-minute window (`rawPayloadSha256 = 6cacbf4beec9f7d3bb9145d1bdce6d39c2eef5e65e61fe486ad717d6f9d52b1f`).
  - Rolled browser wall clock backward by `-6,000 ms` (exceeding the `5,000 ms` consistency tolerance): checkout refused with `WallClockMovedBack` stored in the IndexedDB clock checkpoint and zero durable mutations.
  - Restored wall clock forward AND reloaded the page offline: `WallClockMovedBack` stayed latched and checkout remained refused with unchanged cursor, claim, queue, and shadow stock.
  - Reconnected to receive a strictly later signed verification (`permitId = b7043eb7-bd49-47f4-9da1-de0292ad67d4`): latch cleared (`blocked: null`), subsequent offline sale queued `A/2627/000002` under the new permit while `A/2627/000001` kept its exact original raw payload SHA-256.

### 4. Unseen Renewal & Verified Reconnect (`licensing-offline.spec.ts`, HTTP fixtures)

- Issued `A/2627/000001` offline under original permit `13800d0a-83a1-40de-bb5e-d6df9a37f6e2` (`rawPayloadSha256 = ba1fdb69a4074eaa17c90407e375eed3958d0dd211e5f4992d36368ffa314841`).
- After the original `validUntil`, an unseen server-side renewal left the offline device refused (`[data-testid="offline-permit-ended"]`, `reservations = 0`).
- Reconnecting against `3` stale `/trading-authority` previews kept the old lease (`reservations = 0`) while syncing `A/2627/000001` with its original wire bytes (`wireSaleSha256 = ca202295e167c1d2eba0cd3adf0165257fc187270d53762ba736cd25baa07303`) and original signed permit.
- Only after receiving strictly fresher verified authority did reconnect reserve `1` new lease (`11111111-1111-4111-8111-111111111112`) and complete a verified snapshot (`renewedPermitId = b2747367-823e-4f96-aefe-a88fbbc24536`), issuing `A/2627/000101` under the new permit while leaving `A/2627/000001` byte-unchanged in IndexedDB.

### 5. Live Missed Trading Suspension & Authorized Review (`P0509Tests.MissedSuspensionRetainsOneReview`)

- **Shop / account / deviceRef**: `shopId = b72d80fd-ef9d-4d95-a995-161022553308`, `accountId = 98f8ef6e-cea3-8fa8-b8d8-81afc9d11298`, `deviceId = 18f8a9fb-756c-42f8-a8ad-b029fc5697ba`, `deviceRef = edcdf7d9b8127db5746d58defe9367ecc2eed5765e934dda93f86575764226e3`
- **Permit / lease / grant**: `leaseId = 264bd9b0-8001-4db3-a970-38b235077b47`, `grantId = 8d825c80-fe6d-4e48-b590-0af1fd3bc804`, `permitId = 16f644a9-b3bb-4609-a5fa-d94d726094a4`
- **Offline isolation & cutoff timeline** (asserted as `notBefore < effectiveAt <= soldAt < validUntil`):
  - Verified controlling protected worker (`ngsw-worker.js`), `intelibill:offline-pack:state = on`, zero missing manifest URLs, `Driver state: NORMAL`, offline reload, and `network-error` on `fetch('/api/ping?ngsw-bypass=true')` before suspending.
  - Held permit: `authorityVerifiedAt = 2026-11-23T15:04:33.000000Z`, `notBefore = 2026-11-23T15:04:50.918879Z` (`2026-11-23 20:34:50 IST`), `validUntil = 2026-11-25T15:04:33.000000Z` (`2026-11-25 20:34:33 IST`), `limitingReason = AuthorityFreshness`.
  - Applied `Trading` suspension on the shop via signed provider admin action (`Pending` -> `Acknowledged` across two-way signed delivery). The harness suspends at the next whole second strictly after both the merchant clock and real time, so no clock moves backward: `effectiveAt = 2026-11-23T15:04:52.000000Z` (`2026-11-23 20:34:52 IST`), `1.081121 s` after the permit's `notBefore`.
  - Disconnected browser issued `A/2627/000001` (`clientSaleId = d14256fd-9a75-4c47-96b9-c92805f7f710`) at `soldAt = 2026-11-23T15:04:54.204Z`.
  - Reconnect at `syncAt = 2026-11-23T15:04:59.204Z`: real HTTP `200`, IndexedDB row `NeedsReview` with `OfflineLicenceHistory.RestrictionConflict`, `reviewCaseId = e673b1bf-e880-4df0-8880-600f4270bc12`, payload bytes unchanged, and the wire permit digest equal to the queued token digest (`61b16c48…ff3e`, asserted).
- **First receipt across a later replay race**:
  - Before the race the test read the receipt (`submissionId = 4794b266-980c-4ef8-b70c-2b85161dfb35`, `received_at = syncAt`) and the owner's case view (id, status `Open`, actions, version).
  - Both clocks then moved to `replayAt = 2026-11-23T15:05:29.204Z` (`syncAt + 30 s`), and two independent owner sessions sent the byte-identical wire payload behind a start barrier: both returned `NeedsReview`, `OfflineLicenceHistory.RestrictionConflict` and the same `reviewCaseId`.
  - After the race: the same receipt id, `received_at` still `2026-11-23T15:04:59.204Z` (a rewritten or reinserted receipt would carry `replayAt`), unchanged `payload_hash` and `payload_json`, exactly `1` `offline_submissions` row, exactly `1` `reconciliation_issues` row whose id is the browser's `reviewCaseId`, an unchanged case view including its version, `0` `sales` rows, unchanged stock quantity and unchanged `invoice_sequences`.
- **Authorization and provider invariants**:
  - Owner and manager `GET /api/offline-reconciliation/{caseId}` showed `issueType = "LicenseHistoryReview"`, status `Open`, `availableActions = ["Escalate"]`, `retryEligible = false`, `linkEligible = false`, `sourceCompletionCutoff = effectiveAt`, and no `signedPermit` in the body.
  - Refused owner actions, each `400` with exactly one error: `RetryHistoricalLease` and `LinkExistingSale` -> `Reconciliation.ActionNotAllowed` ("This action is not available for the case."), decided by the case policy. `PostAnyway`, `Redate`, `Renumber` and `Delete` are not reconciliation actions and are rejected by request validation before any policy runs; on the wire that failure is keyed by the request property, code `Action` with the description "The action is not recognised." (the `Reconciliation.UnknownAction` text, not its code).
  - Unrelated tenant `GET`/`POST` and foreign staff `GET` returned `404 NotFound`.
  - Staff's own held case (`A/2627/000002`) allowed `Escalate` (`200 OK` -> `"Escalated"`) and denied `RetryHistoricalLease` (`403 Forbidden`).
  - After all of the above the shop still had `0` sales, the original case view was unchanged (`Open`, `["Escalate"]`, same version) and the original receipt still had `sale_id = NULL` and `received_at = syncAt`.
  - Provider `device-permits` reported `knownPermitEnd = 2026-11-25T15:04:33Z`, `lastSynchronizedAt = 2026-11-23T15:05:29.204Z` (last device contact, the replay), `disconnectedQueues = "Unknown"`, and contained no invoice number.

### 6. Legacy Queue Upgrade, Offline Print, Recovery Export/Import, and Archived Key Rotation

- **Browser upgrade & offline print (`licensing-offline.spec.ts` + `legacy-queue-recovery.spec.ts`)**:
  - Seeded literal pre-document (`legacy-7ff1-0001`, `INV-2026-27-000001`), v1 frozen document (`legacy-671d-0002`, `A/2627/000002`), and pre-upgrade signed (`legacy-signed-0003`) rows under a legacy Angular worker deployment, then upgraded to the current protected-worker deployment.
  - Verified protected-worker takeover, legacy protected API cache purge, full offline pack readiness, exact wire upload bytes matching pinned contract vectors, offline A4 and thermal rendering of both legacy records (preserving `"Frozen Legacy Traders Pvt Ltd"` without current shop GSTIN override), staff print denial on another issuer's invoice, and checksummed recovery bundle export/import (`exportedBundleChecksum = 0db78f997730ee97f7b54d2a999d711024bcc2be17b710e34b30c0aa8c4d950d`, `leaseNextNumber = 25`, `leaseReservations = 0`).
- **PostgreSQL archived key retention & legacy provenance (`P0506LicensingTests`)**:
  - `P0506LicensingTests.Key_rotation_preserves_archived_key_and_keeps_legacy_receipt_references_null` passed under a rotated key-B-only host: permits signed under archived key A still verified and recovered via the persisted public key fingerprint, supported legacy invoices posted once with `licence_permit_id = NULL` and `licence_grant_id = NULL`, and unprovable legacy submissions stayed held for authorized review without fabricating permit references.

### 7. Security-Barred Upload Keeps the Local Queue (`licensing-offline.spec.ts`, HTTP fixtures)

- Queued `A/2627/000001` offline under a valid fixture permit (row `Pending`, raw payload SHA-256 `8d2a4c415e4d6d3ed3b5740a3631dd78813fe5fd0953ec48437ec61604b1f575`).
- On reconnect a Playwright route answered `POST /api/sales/offline-sync` with `403` and `code = "Licence.SecuritySuspended"`. The upload carried `A/2627/000001` (`1` refused attempt observed).
- The IndexedDB row stayed present as `Failed` (unsent, retryable) with the same payload SHA-256; after a reload while still barred it was still present, still `Failed`, same SHA-256.
- Scope: this shows the browser keeps an unsent invoice when the upload is refused with that code. That the server refuses a security-suspended shop with `403 Licence.SecuritySuspended` on sync, bundle import and replay is shown separately by `P0505LicensingTests` / `P0506LicensingTests` on PostgreSQL. No single run joins a real security suspension to a real browser queue.

## Hash-Domain Comparison

The browser, wire transport, recovery bundle, and server idempotency layers intentionally hash distinct canonical domains; none are equated across domains, and each remained invariant across reload, reprint, refresh, and replay within its own domain:

| Scenario / Record | Raw IndexedDB Payload SHA-256 (`UTF-8 JSON.stringify(row.payload)`, lowercase) | Mapped Wire Sale SHA-256 (`POST /api/sales/offline-sync` sale JSON, lowercase) | Signed Permit Token SHA-256 (lowercase) | Recovery Bundle Checksum (raw `invoices` array JSON) | Server Idempotency Hash (`offline_submissions.payload_hash` & `sales.request_hash`, uppercase) |
| --- | --- | --- | --- | --- | --- |
| Live ordinary expiry (`A/2627/000001`, `f10f210c-...`) | `f9c16f3ce2ba58027c2a3f5815a04ab9019ea5d3d583de97abad99a347f58c07` (same after sync) | `ab4f8e8d24c5f7605ffeb025d6f0eb597b9fb23076a58163cea89d2abf5c2de6` | `c40ef54816c1823dbe37a78127edd3ac049bf9fd5c0441b45f632213dfc3a229` (queue = wire) | N/A (direct sync) | `1AFEAFCEA06AD446AB78CA635AD14A8C7083BB73B9583BCE35088AE677DBE186` |
| Live missed suspension (`A/2627/000001`, `d14256fd-...`) | `9714b80bc2e03cdce781eac9556fdfb56454fac364ab3eecfecf4117d48560af` (same after sync) | `400dc5b70792dd70fa2784e9d76d63ae2eb17285fd04d825a89014d836924a9f` | `61b16c488c5ad25e92843f914555f2f4424a8607ebf28a940f35d3163b7eff3e` (queue = wire) | N/A (direct sync) | `86411724C5761F3A0C055103F57CDBD2B38B8D9443700A2604FDA5738DBBBDC0` (receipt only, no sale) |
| Fixture security-barred upload (`A/2627/000001`) | `8d2a4c415e4d6d3ed3b5740a3631dd78813fe5fd0953ec48437ec61604b1f575` (same after refusal and reload) | not hashed | not hashed | N/A | N/A (fixture answer, no server) |
| Pinned legacy v1 frozen vector (`A/2627/000002`, `legacy-671d-0002`) | `3b64d24f7adaaa44296daec15283eb1823d95185ffeed9e672a9b35071e693b5` | `e4ff2c8bf0a4beeb1b4d87c04098f581369c0fe919e54823004a093de313fbab` | N/A (pre-permit v1 row) | `0db78f997730ee97f7b54d2a999d711024bcc2be17b710e34b30c0aa8c4d950d` (3-invoice bundle) | `D58AB44706DACAA1C8B2318F85FDFA3C9F82288164B3B4EB6546AC9F885F5390` |
| Pinned legacy pre-document vector (`INV-2026-27-000001`, `legacy-7ff1-0001`) | `c2e66bb04a7c51b266ee254233a28f09a89f1654204a64c936038b6dbfbb93ca` | `b945e31e4ffa2d2edcd287dab25ad08aef7ceb8f352d905cb9ff98497762ae0d` | N/A (unversioned pre-doc row) | `0db78f997730ee97f7b54d2a999d711024bcc2be17b710e34b30c0aa8c4d950d` (3-invoice bundle) | `CCAAF5A1F8AEAA1C119C3EAD35100704174D5BFB0D64DF8936C223CE3D64F910` |

## Ordinary Expiry vs Trading Suspension vs Security Suspension & Provenance

| Condition | New Lease / Permit Issuance | Previously Issued Valid Offline Work (`soldAt < cutoff` & inside permit) | Uncertain / Post-Cutoff Offline Work (`soldAt >= suspension_effective_at` or unprovable legacy) | Provenance & Key Retention |
| --- | --- | --- | --- | --- |
| **Ordinary commercial expiry** | Refused (`403 Licence.Expired`) | Posts once on reconnect under `LicenceAction.Recovery`; replay returns `"duplicate"` with first receipt unchanged by a replay 30 s later (`P0509Tests.BrowserSaleRecoversAfterOrdinaryExpiry`) | Held as `LicenseHistoryReview` when permit window or timing proof fails (`P0504`/`P0505`) | Original `licence_permit_id` / `licence_grant_id` linked when matched; archived rotated keys continue verifying (`P0506`) |
| **Trading suspension** | Refused (`403 Licence.ShopSuspended` / `AccountSuspended`) | Eligible pre-cutoff work retains historical evidence | Held as single `LicenseHistoryReview` (`OfflineLicenceHistory.RestrictionConflict`), `0` sales, `Escalate`-only until lawful resolution (`P0509Tests.MissedSuspensionRetainsOneReview`) | First receipt compared before and after a later concurrent replay race: same id, `received_at`, `payload_hash`, `payload_json`; raw permit token never returned in review detail |
| **Security suspension** | Refused (`403`) | Server side: ordinary recovery denied and shop data exposure blocked (`P0505LicensingTests` / `P0506LicensingTests`). Browser side, HTTP fixture only: an upload refused with `403 Licence.SecuritySuspended` leaves the queued invoice on the device, unsent and byte-identical across reconnect and reload (`licensing-offline.spec.ts`, "security-barred upload keeps the queued invoice…", scenario 7) | Denied without silent posting; reviewed restoration requires explicit authorization (`P0505`/`P0506`) | All immutable receipt and audit records preserved under tenant isolation |
| **Legacy pre-permit queue** | Requires fresh online authority (`0` automatic reservations during recovery) | Supported pre-cutoff legacy invoices post once (`P0506LicensingTests`); offline A4/thermal print works from frozen local record | Unprovable legacy invoices stay held for authorized review | `licence_permit_id = NULL`, `licence_grant_id = NULL` (no fabricated permit links) |

## Isolated Current-Source Combined Compose Stack Acceptance

Attribution: shipping Dockerfiles and Compose configuration only. No signed offline permit, expiry or suspension behaviour is exercised here (see the scope limits above).

- **Disposable Compose project**: `intelibill-p01-05-908-20261123151324-74089` (`2026-11-23T15:13:24Z`–`15:15:15Z`) on stock ports (`14000`, `14001`, `14025`, `14277`, `14278`, `14432`, `14825`; `lsof` showed no listener on any of them before start).
- **Explicit jobs, images and container states** (`docker inspect`, before cleanup):
  - `db_bootstrap` (`sha256:fb288c51623c...`), `db_runtime_role` (`sha256:fb288c51623c...`), `licensing_runtime_role` (`sha256:fb288c51623c...`), `merchant_migrate` (`sha256:369502d0fff4...`), `licensing_migrate` (`sha256:f59d1c0b0cf7...`): all **exited `0`**.
  - `postgres` (`sha256:fb288c51623c...`), `mailpit` (`sha256:245e558b2e44...`), `backend` (`sha256:4c5f38aee3fd...`), `licensing_api` (`sha256:f22a4d30152c...`), `frontend` (`sha256:8c4cc0880834...`), `admin_frontend` (`sha256:d07947e3adee...`): all **running**.
- **Observed stack behaviors**:
  - Browser phase 1 (`enroll`): `2 passed (8.5s)` — merchant enrollment through Mailpit and separate-origin admin denial for signed-in merchant browser.
  - Separation and RLS: `owner=1 runtime_unscoped=0 runtime_own_shop=1 runtime_other_shop=0`; database row counts `users=1 shops=1 memberships=1 all_users=1 all_shops=1`.
  - Restart persistence (`compose down` / `compose up -d` + browser phase `restart`): `2 passed (4.6s)` with identical database counts.
  - Readiness probes: `http://127.0.0.1:14277/health/ready` -> `200 Healthy`; `http://127.0.0.1:14278/health/ready` -> `200 {"service":"intelibill-licensing","status":"ready"}`.
  - Scoped cleanup (`scripts/dev/test-licensing-stack.sh --case cleanup`) exited `0` and left `0` containers of the disposable project; `diff -u default-before.txt default-after.txt` against the default `intelibill` developer stack returned exit `0` (empty diff).

## Verification Command Matrix

Measured on `2026-11-23` (`15:04:11Z`–`15:15:15Z`) with `.NET SDK 10.0.300` and `Bun 1.3.14`, from the test sources of this revision, run sequentially.

| # | Working Directory | Command | Exit | Observed Result |
| --- | --- | --- | --- | --- |
| 1 | root | `dotnet build src/backend/Intelibill.slnx` | `0` | Build succeeded (0 warnings, 0 errors) |
| 2 | root | `dotnet build src/licensing/Intelibill.Licensing.slnx` | `0` | Build succeeded (0 warnings, 0 errors) |
| 3 | `src/frontend` | `bun run build` | `0` | Production bundle + `ngsw-worker.js` wrapper + `ngsw.json` + `ngsw-lite.json` built |
| 4 | `src/admin-frontend` | `bun run build` | `0` | Production admin bundle built |
| 5 | root | `P0509_RUN_BROWSER=1 scripts/dev/test-integration.sh --licensing --filter 'FullyQualifiedName~P0509Tests\|FullyQualifiedName~P0507Tests' --logger 'console;verbosity=normal'` | `0` | `10 passed, 0 failed, 0 skipped` (`52.79 s`) |
| 6 | root | `scripts/dev/test-integration.sh --filter 'FullyQualifiedName~P0501LicensingTests\|FullyQualifiedName~P0502LicensingTests\|FullyQualifiedName~P0504LicensingTests\|FullyQualifiedName~P0505LicensingTests\|FullyQualifiedName~P0506LicensingTests' --logger 'console;verbosity=normal'` | `0` | `119 passed, 0 failed, 0 skipped` (`49.98 s`) |
| 7 | root | `dotnet test tests/backend/unit/Intelibill.Application.Unit.Tests --filter 'FullyQualifiedName~OfflineInvoiceArithmeticValidatorTests\|FullyQualifiedName~OfflineInvoiceDocumentContractTests'` | `0` | `234 passed, 0 failed, 0 skipped` |
| 8 | root | `dotnet test tests/shared/P0102ConsumerRegressionTests` | `0` | `15 passed, 0 failed, 0 skipped` |
| 9 | root | `dotnet test tests/shared/P0103ConsumerRegressionTests` | `0` | `12 passed, 0 failed, 0 skipped` |
| 10 | root | `bash .github/scripts/tests/licensing-stack-contract.test.sh` | `0` | `Licensing stack contract passed (11 services).` |
| 11 | `src/frontend` | `bun run audit:ui:release licensing-offline.spec.ts` | `0` | `5 passed, 0 failed, 0 skipped` (`40.5 s`), including the security-barred upload case |
| 12 | `src/frontend` | `bun run audit:ui:release legacy-queue-recovery.spec.ts offline-permit-finalization.spec.ts offline-permit-refresh.spec.ts offline-permit.spec.ts offline-pack.spec.ts protected-cache-isolation.spec.ts` | `0` | `46 passed, 0 failed, 0 skipped` (`4.0 m`) |
| 13 | `src/frontend` | `bun run test --run offline-permit-clock offline-permit-refresh offline-licence-permit invoice-lease-indexeddb offline-sales-snapshot-sync offline-sale-sync.mapper offline-recovery-bundle offline-sale-pricing-calculator offline-invoice-document.builder offline-invoice-print.guard sale-invoice-print-page` | `0` | `14 test files, 297 passed, 0 failed` (`1.82 s`) |
| 14 | `src/frontend` | `bun run test --run scripts/tests/angular-worker-manifest.spec.ts scripts/tests/finalize-service-worker.spec.ts scripts/tests/offline-pack-worker.spec.ts scripts/tests/protected-api-worker.spec.ts` | `0` | `4 files, 59 passed, 0 failed` |
| 15 | `src/admin-frontend` | `bun x playwright test --config tests/release/playwright.release.config.ts device-permits.spec.ts` | `0` | `2 passed, 0 failed` (`1.8 s`) |
| 16 | root | `bash scripts/dev/test-licensing-stack.sh --case browser` + `docker inspect` + probes + `--case cleanup` (isolated project `intelibill-p01-05-908-20261123151324-74089`) | `0` | Enroll `2 passed`, RLS `PASS`, restart `2 passed`, `/health/ready` `200` on `14277`/`14278`, five jobs exited `0`, default stack diff empty |
| 17 | root | `P0509_RUN_BROWSER=1 scripts/dev/test-integration.sh --licensing --filter 'FullyQualifiedName~P0509Tests' --logger 'console;verbosity=normal'` | `0` | `3 passed, 0 failed, 0 skipped` (`50.55 s`) |
| 18 | root | `P0509_RUN_BROWSER=1 scripts/dev/test-integration.sh --licensing --filter 'FullyQualifiedName~P0509Tests.MissedSuspensionRetainsOneReview' --logger 'console;verbosity=normal'` | `0` | `1 passed, 0 failed, 0 skipped` (`32.92 s`) |
