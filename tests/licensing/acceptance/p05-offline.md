# P05-09 Bounded Offline, Clock, and Legacy Recovery Acceptance

This acceptance record closes slice **P05-09** ([#908](https://github.com/chanakya-net/intelibill/issues/908)) under parent PRD [#827](https://github.com/chanakya-net/intelibill/issues/827) (InteliBill Licensing P05 — Bounded Offline Access and Historical Recovery, user stories 1–10).

## Environment, Baseline, and Evidence Boundaries

- **Parent baseline SHA**: `f618d53f85dbec2a1cc0c5bfa8dc16eb21c63c86` (includes `#900`–`#907`).
- **Verification toolchain**:
  - `.NET SDK`: `10.0.300` via `/usr/local/share/dotnet` (satisfies `global.json` pin `10.0.105`, `rollForward: latestMinor`).
  - `Bun`: `1.3.14` (locked workspace dependencies via `bun install --frozen-lockfile`).
  - `PostgreSQL`: Docker `postgres:17-alpine` via Testcontainers and isolated Docker Compose.
  - `Browser`: Playwright Chromium with real service workers enabled (`serviceWorkers: 'allow'`).
- **Attribution and scope limits**:
  - Live browser post-expiry and missed-suspension runs use real Chromium, the built protected worker (`ngsw-worker.js` wrapping `ngsw-angular-worker.js`), real IndexedDB, real loopback Kestrel merchant and licensing APIs, two separate PostgreSQL databases, signed HTTP outbox/inbox delivery, and disposable test RSA keys.
  - Exact millisecond boundary, persistent clock rollback, renewal, and legacy-upgrade worker scenarios run against the built merchant web bundle in Chromium with local HTTP fixtures and fixture RSA keys (`licensing-offline.spec.ts` + existing release specs).
  - The isolated Compose acceptance builds and runs the shipping Dockerfiles/Compose configuration under a disposable project name (`intelibill-p01-05-908-*`). Because stock Compose builds with an empty offline public-pin map and no P05 signing key, signed offline permit issuance is attributed to the pinned Kestrel/PostgreSQL host runs rather than stock Compose.
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

Before enabling the real upload path in `P0509Tests.BrowserSaleRecoversAfterOrdinaryExpiry`, the test harness ran with the earlier `#907` fixture pattern — intercepting `POST /api/sales/offline-sync` with `{ "results": [] }` — to prove that the PostgreSQL receipt assertion fails red when no browser-issued invoice actually reaches the merchant database:

| Stage | Command | Exit | Browser Result | PostgreSQL Assertion (`sales` & `offline_submissions`) |
| --- | --- | --- | --- | --- |
| **Red control** (`2026-11-23T13:28:06Z`–`13:28:33Z`) | `P0509_RUN_BROWSER=1 scripts/dev/test-integration.sh --licensing --filter 'FullyQualifiedName~P0509Tests.BrowserSaleRecoversAfterOrdinaryExpiry' --logger 'console;verbosity=normal'` (with temporary empty `{ results: [] }` route stub on `/api/sales/offline-sync`) | `1` | Playwright `1 passed (4.7s)` — browser queued `A/2627/000001` (`clientSaleId = 290ae32a-a79d-4332-a34f-7c3efa38e903`, `soldAt = 2026-11-23T13:28:33.060Z`, `validUntil = 2026-11-23T13:31:18.000000Z`) and attempted upload after `Licence.Expired` | **FAILED** at `P0509Tests.Browser.cs:line 76`: `Assert.Equal() Failure: Values differ — Expected: 1, Actual: 0` (`sales` count = `0`, `offline_submissions` count = `0`) |
| **Positive HTTP green** (`2026-11-23T13:29:06Z`–`13:29:36Z`, and full combined suite) | Same command on a fresh shop with all route interception removed | `0` | Playwright `1 passed` — real HTTP `200` to Kestrel `/api/sales/offline-sync`, IndexedDB status transitioned to `Synced` with unchanged payload bytes | **PASSED** (`22 s` isolated; `10/10` combined `P0509Tests|P0507Tests`): `1` posted sale, `1` original `offline_submissions` receipt, direct replay returned `"duplicate"` with unchanged receipt ID, `received_at`, `payload_hash`, and `payload_json` |

## Correlated Scenario Evidence

### 1. Live Browser Post-Expiry Recovery (`P0509Tests.BrowserSaleRecoversAfterOrdinaryExpiry`)

- **Merchant shop / billing account**: `shopId = a33f40b7-3d5e-48e3-9726-16a7427d85ad`, `accountId = 27a85cf7-83c5-8b50-ad1e-1565573adee4`
- **Device & opaque provider reference**: `deviceId = b7225b49-adde-4e4b-b97b-bca1d4099a80`, `deviceRef = 73c9eece6709efe1d3920ed2337fa473d6fd4104d987e071d5add121d1fb2b1b`
- **Permit / lease / grant**: `leaseId = c9cb6c25-354a-4501-9828-59eb4bda2f14`, `grantId = cb4e5538-1a85-4eae-b64c-32bcc050053f`, `permitId = 797eca33-a9aa-48e6-8450-bd1866814bce`
- **Invoice & PostgreSQL rows**: `clientSaleId = 40150990-d7b2-4d37-b1e7-2669388af1f2`, `invoiceNumber = A/2627/000001`, `submissionId = c9e560d9-7239-48d1-923f-a363236277ac`, `saleId = 87fe2b57-d686-4db2-afc4-0bd6df3f64e3`
- **Measured timestamps & decisions**:
  - `authorityVerifiedAt`: `2026-11-23T14:05:12.000000Z` (`2026-11-23 19:35:12 IST`)
  - `notBefore`: `2026-11-23T14:05:21.109901Z` (`2026-11-23 19:35:21 IST`)
  - `validUntil` (`termEnd`): `2026-11-23T14:08:12.000000Z` (`2026-11-23 19:38:12 IST`), `limitingReason = LicenceEnd`
  - `soldAt`: `2026-11-23T14:05:22.736Z` (`2026-11-23 19:35:22.736 IST`, strictly inside `[notBefore, validUntil)`)
  - `syncAt` (`received_at`): `2026-11-23T14:09:12.000000Z` (`2026-11-23 19:39:12 IST`, after commercial expiry)
  - Pre-sync lease reserve attempt at `syncAt`: `403 Forbidden` with `Licence.Expired`.
  - Browser reconnect upload at `syncAt`: `200 OK`, posted `1` sale (`total_amount = 50`, `invoice_document` populated), `offline_submissions.sale_id == sale.id`, `licence_permit_id == permitId`, `licence_grant_id == grantId`, `payload_hash == sales.request_hash`.
  - Direct replay of the wire payload: `status = "duplicate"`, `saleId = 87fe2b57-d686-4db2-afc4-0bd6df3f64e3`, `sales` count = `1`, `offline_submissions` count = `1`, original receipt immutable.
  - Provider `GET /api/admin/accounts/{accountId}/device-permits` (`Cache-Control: no-store`): `disconnectedQueues = "Unknown"`, `knownPermitEnd = 2026-11-23T14:08:12Z`, `lastSynchronizedAt = 2026-11-23T14:09:12Z`; `outstanding = true` at `termEnd - 1 µs` (`2026-11-23T14:08:11.999999Z`) and `outstanding = false`, `outstandingPermits = 0` at `termEnd` (`2026-11-23T14:08:12.000000Z`).

### 2. Stale 48-Hour Authority & Stock Snapshot Non-Extension (`P0509Tests.RefreshAndStockNeverExtendOriginalGrant`)

- **Shop / account / deviceRef**: `shopId = 4eef8e2a-8de1-4a3c-9d3e-cb07971794a8`, `accountId = 1338c5a0-ce13-8cbe-b643-9cd8cc91cae0`, `deviceRef = eb6157d1c73ba67a00311bbf1f87efc089d8df6c8df657f5bd133c4c356ec6df`
- **Original verification & hard upper bound**:
  - `authorityVerifiedAt`: `2026-11-21T14:10:24.000000Z` (`2026-11-21 19:40:24 IST`)
  - `expectedEnd` (`authorityVerifiedAt + 48 h`): `2026-11-23T14:10:24.000000Z` (`2026-11-23 19:40:24 IST`), `limitingReason = AuthorityFreshness`
- **Non-extension proofs**:
  - Repeated `GET /trading-authority` reads and signed upstream snapshot replay left `validUntil == expectedEnd`.
  - Two additional stock intakes followed by full snapshot completions issued new permit IDs on the original grant, each still capped at `expectedEnd` (`AuthorityFreshness`).
  - Delivering a strictly fresher signed `Account + Shop` revision advanced the shop's `trading-authority` beyond `expectedEnd`, yet a subsequent snapshot stream on the original reservation/grant remained capped at `expectedEnd` (`permitsIssuedOnOriginalGrant = 5`).
- **Half-open PostgreSQL & provider boundary**:
  - `lastAllowedInstant = 2026-11-23T14:10:23.999999Z` (`expectedEnd - 1 µs`): stream `status = "issued"`, `validUntil = expectedEnd`; provider projection reported `outstanding = true`, `outstandingPermits = 5`, `knownPermitEnd = expectedEnd`, `disconnectedQueues = "Unknown"`.
  - `refusedInstant = 2026-11-23T14:10:24.000000Z` (exact `expectedEnd`): stream returned `status = "refused"`, `refusalCode = "OfflinePermit.DeadlineReached"` with zero additional `offline_license_permits` or `DeviceObservation` rows; replaying the stale snapshot on a second shop left `POST /api/sales/invoice-leases/reserve` failing with `503 ServiceUnavailable` (`Licence.VerificationUnavailable`) and zero lease rows; provider projection flipped to `outstanding = false`, `outstandingPermits = 0`.

### 3. Browser Exact Millisecond Deadline & Persistent Clock Rollback (`licensing-offline.spec.ts`)

- **Exact millisecond half-open boundary (two independent Chromium contexts with pinned build and active protected worker)**:
  - `validUntil`: `2026-11-23T13:54:10.000000Z` (`2026-11-23 19:24:10 IST`, epoch `1795442050000 ms`).
  - Context 1 pinned at `validUntil - 1 ms` (`1795442049999 ms` / `2026-11-23T13:54:09.999Z`): offline checkout succeeded and queued `A/2627/000001` (`nextNumber: 2`, `remainingCount: 99`, `claim: null`, raw payload SHA-256 `19275cd6a07d9ed14ebae8ad1ad89404613fc87f5bedb332485dfd0683de00b4`).
  - Context 2 pinned at exact `validUntil` (`1795442050000 ms` / `2026-11-23T13:54:10.000Z`): offline checkout refused with `"Offline permission for this device has ended."` and IST notice (`[data-testid="offline-permit-ended"]`), zero queued rows, and byte-identical durable state (`nextNumber: 1`, `remainingCount: 100`, `claim: null`, `queueCount: 0`, unchanged shadow stock).
- **Persistent clock rollback latch**:
  - Issued `A/2627/000001` inside a valid 10-minute window (`rawPayloadSha256 = d48ff955ad3c10554c394da5ca82321ef291800fb42adb8561618b8043361997`).
  - Rolled browser wall clock backward by `-6,000 ms` (exceeding the `5,000 ms` consistency tolerance): checkout refused with `WallClockMovedBack` stored in the IndexedDB clock checkpoint and zero durable mutations.
  - Restored wall clock forward AND reloaded the page offline: `WallClockMovedBack` stayed latched and checkout remained refused with unchanged cursor, claim, queue, and shadow stock.
  - Reconnected to receive a strictly later signed verification (`permitId = 34e7ecec-9727-46e7-99f2-cf4328421311`): latch cleared (`blocked: null`), subsequent offline sale queued `A/2627/000002` under the new permit while `A/2627/000001` kept its exact original raw payload SHA-256.

### 4. Unseen Renewal & Verified Reconnect (`licensing-offline.spec.ts`)

- Issued `A/2627/000001` offline under original permit `bb1a190a-f957-4f95-a900-717d8350e6a2` (`rawPayloadSha256 = 1c06a0b170103602ed9f00cf76ed2a642f6f8d53e9f332e98d8a6dfc30b4f022`).
- After the original `validUntil`, an unseen server-side renewal left the offline device refused (`[data-testid="offline-permit-ended"]`, `reservations = 0`).
- Reconnecting against `3` stale `/trading-authority` previews kept the old lease (`reservations = 0`) while syncing `A/2627/000001` with its original wire bytes (`wireSaleSha256 = 10704c38c8af47f39448a95fbd25ce001a57453f27f780012ace6e3073d2c660`) and original signed permit.
- Only after receiving strictly fresher verified authority did reconnect reserve `1` new lease (`11111111-1111-4111-8111-111111111112`) and complete a verified snapshot (`renewedPermitId = 72d49720-05c2-4a34-a95e-2d2277f041ac`), issuing `A/2627/000101` under the new permit while leaving `A/2627/000001` byte-unchanged in IndexedDB.

### 5. Live Missed Trading Suspension & Authorized Review (`P0509Tests.MissedSuspensionRetainsOneReview`)

- **Shop / account / deviceRef**: `shopId = 97089013-6b9d-472d-960d-8258d7372ab6`, `accountId = 7391043b-0b68-81ee-973d-879d0741b7cb`, `deviceId = 8f874099-558b-45dc-a7c5-8d7c52916b3a`, `deviceRef = 074e45a13581fe1e693949bade9c684dc98a0c371b8dfa349363d11ff4ddba52`
- **Permit / lease / grant**: `leaseId = 11498967-f88f-4e92-8662-6e57111c9164`, `grantId = 3bcc36c4-7857-47b5-b20c-d7568e86c876`, `permitId = f3045866-01aa-45f5-939a-9921b571b66b`
- **Offline isolation & cutoff timeline**:
  - Verified controlling protected worker (`ngsw-worker.js`), `intelibill:offline-pack:state = on`, zero missing manifest URLs, `Driver state: NORMAL`, offline reload, and `network-error` on `fetch('/api/ping?ngsw-bypass=true')` before suspending.
  - Applied `Trading` suspension on the shop via signed provider admin action (`Pending` -> `Acknowledged` across two-way signed delivery): `effectiveAt = 2026-11-23T14:05:09.000000Z` (`2026-11-23 19:35:09 IST`).
  - Disconnected browser issued `A/2627/000001` (`clientSaleId = 224fb96f-c014-436e-bb2d-f995117a00bd`) at `soldAt = 2026-11-23T14:05:11.208Z` (`>= effectiveAt` and `< permitValidUntil = 2026-11-25T14:04:52.000000Z`).
  - Reconnect at `syncAt = 2026-11-23T14:05:16.208Z` followed by a barrier-synchronized duplicate replay across two independent owner sessions: both returned `status = "NeedsReview"`, `OfflineLicenceHistory.RestrictionConflict`, and the same `reviewCaseId = 5507805e-0bed-40b1-b88f-fcf046978180`.
- **Database, authorization, and provider invariants**:
  - Merchant PostgreSQL retained exactly `1` `offline_submissions` row (`submissionId = 77767fae-acef-4930-afa1-ffb0fd6db100`, `sale_id = NULL`, `licence_permit_id = permitId`, `licence_grant_id = grantId`), `1` `reconciliation_issues` row, `0` `sales` rows, unchanged stock quantity, and unchanged `invoice_sequences`.
  - Owner and manager `GET /api/offline-reconciliation/{caseId}` showed `issueType = "LicenseHistoryReview"`, `availableActions = ["Escalate"]`, `retryEligible = false`, `linkEligible = false`, `sourceCompletionCutoff = effectiveAt`, and zero raw `signedPermit` token leakage.
  - Disallowed actions (`RetryHistoricalLease`, `LinkExistingSale`, `PostAnyway`, `Redate`, `Renumber`, `Delete`) returned `400 BadRequest`; unrelated tenant `GET`/`POST` and foreign staff `GET` returned `404 NotFound`.
  - Staff's own held case (`A/2627/000002`) allowed `Escalate` (`200 OK` -> `"Escalated"`) and denied `RetryHistoricalLease` (`403 Forbidden`).
  - Provider `device-permits` reported `knownPermitEnd = 2026-11-25T14:04:52Z`, `lastSynchronizedAt = 2026-11-23T14:05:16.208Z`, `disconnectedQueues = "Unknown"`, and contained zero invoice numbers.

### 6. Legacy Queue Upgrade, Offline Print, Recovery Export/Import, and Archived Key Rotation

- **Browser upgrade & offline print (`licensing-offline.spec.ts` + `legacy-queue-recovery.spec.ts`)**:
  - Seeded literal pre-document (`legacy-7ff1-0001`, `INV-2026-27-000001`), v1 frozen document (`legacy-671d-0002`, `A/2627/000002`), and pre-upgrade signed (`legacy-signed-0003`) rows under a legacy Angular worker deployment, then upgraded to the current protected-worker deployment.
  - Verified protected-worker takeover, legacy protected API cache purge, full offline pack readiness, exact wire upload bytes matching pinned contract vectors, offline A4 and thermal rendering of both legacy records (preserving `"Frozen Legacy Traders Pvt Ltd"` without current shop GSTIN override), staff print denial on another issuer's invoice, and checksummed recovery bundle export/import (`exportedBundleChecksum = 0db78f997730ee97f7b54d2a999d711024bcc2be17b710e34b30c0aa8c4d950d`, `leaseNextNumber = 25`, `leaseReservations = 0`).
- **PostgreSQL archived key retention & legacy provenance (`P0506LicensingTests`)**:
  - `P0506LicensingTests.Key_rotation_preserves_archived_key_and_keeps_legacy_receipt_references_null` passed under a rotated key-B-only host: permits signed under archived key A still verified and recovered via the persisted public key fingerprint, supported legacy invoices posted once with `licence_permit_id = NULL` and `licence_grant_id = NULL`, and unprovable legacy submissions stayed held for authorized review without fabricating permit references.

## Hash-Domain Comparison

The browser, wire transport, recovery bundle, and server idempotency layers intentionally hash distinct canonical domains; none are equated across domains, and each remained invariant across reload, reprint, refresh, and replay within its own domain:

| Scenario / Record | Raw IndexedDB Payload SHA-256 (`UTF-8 JSON.stringify(row.payload)`, lowercase) | Mapped Wire Sale SHA-256 (`POST /api/sales/offline-sync` sale JSON, lowercase) | Signed Permit Token SHA-256 (lowercase) | Recovery Bundle Checksum (raw `invoices` array JSON) | Server Idempotency Hash (`offline_submissions.payload_hash` & `sales.request_hash`, uppercase) |
| --- | --- | --- | --- | --- | --- |
| Live ordinary expiry (`A/2627/000001`, `40150990-...`) | `0aa49188c5d2fa147cdf8157d88f2c9add69469ba465fce057a797069fc84131` | `dd460dc06c725f51be9f463de8125af7a5b6d3baea6d71e34b46d2bd0174f67d` | `1a1ce47470989caa595c605c5ab8bbd7492bd7afcd0431100f6987e91f45433c` | N/A (direct sync) | `099EDE2A3F17A354049CC0D0CC4DF39B3E5CB823FDED06ED53502A2529D269DC` |
| Live missed suspension (`A/2627/000001`, `224fb96f-...`) | `7b8f928935b6cd46ac6df3649f084021f83abc997d773d08ee3f77c046e596ba` | `fc1157ad525ab54c0e84ec279f59c0cc5b13cce71e70d4a75f75435a4c689f0a` | `e8fc98a2600febf5812b1f63e7b2a17ca62b390d95720e96e4b4822cff394f73` | N/A (direct sync) | `AD02B758321A6C492FBA42F381D6CC36BDA97AE000999E809820E86E4D41E00F` |
| Pinned legacy v1 frozen vector (`A/2627/000002`, `legacy-671d-0002`) | `3b64d24f7adaaa44296daec15283eb1823d95185ffeed9e672a9b35071e693b5` | `e4ff2c8bf0a4beeb1b4d87c04098f581369c0fe919e54823004a093de313fbab` | N/A (pre-permit v1 row) | `0db78f997730ee97f7b54d2a999d711024bcc2be17b710e34b30c0aa8c4d950d` (3-invoice bundle) | `D58AB44706DACAA1C8B2318F85FDFA3C9F82288164B3B4EB6546AC9F885F5390` |
| Pinned legacy pre-document vector (`INV-2026-27-000001`, `legacy-7ff1-0001`) | `c2e66bb04a7c51b266ee254233a28f09a89f1654204a64c936038b6dbfbb93ca` | `b945e31e4ffa2d2edcd287dab25ad08aef7ceb8f352d905cb9ff98497762ae0d` | N/A (unversioned pre-doc row) | `0db78f997730ee97f7b54d2a999d711024bcc2be17b710e34b30c0aa8c4d950d` (3-invoice bundle) | `CCAAF5A1F8AEAA1C119C3EAD35100704174D5BFB0D64DF8936C223CE3D64F910` |

## Ordinary Expiry vs Trading Suspension vs Security Suspension & Provenance

| Condition | New Lease / Permit Issuance | Previously Issued Valid Offline Work (`soldAt < cutoff` & inside permit) | Uncertain / Post-Cutoff Offline Work (`soldAt >= suspension_effective_at` or unprovable legacy) | Provenance & Key Retention |
| --- | --- | --- | --- | --- |
| **Ordinary commercial expiry** | Refused (`403 Licence.Expired`) | Posts once on reconnect under `LicenceAction.Recovery`; replay returns `"duplicate"` with immutable first receipt (`P0509Tests.BrowserSaleRecoversAfterOrdinaryExpiry`) | Held as `LicenseHistoryReview` when permit window or timing proof fails (`P0504`/`P0505`) | Original `licence_permit_id` / `licence_grant_id` linked when matched; archived rotated keys continue verifying (`P0506`) |
| **Trading suspension** | Refused (`403 Licence.ShopSuspended` / `AccountSuspended`) | Eligible pre-cutoff work retains historical evidence | Held as single `LicenseHistoryReview` (`OfflineLicenceHistory.RestrictionConflict`), `0` sales, `Escalate`-only until lawful resolution (`P0509Tests.MissedSuspensionRetainsOneReview`) | First receipt preserved unchanged across concurrent replay races; raw permit token never returned in review detail |
| **Security suspension** | Refused (`403`) | Ordinary recovery denied and shop data exposure blocked (`P0505LicensingTests` / `P0506LicensingTests`); local browser queue preserved without dropping unsynced rows | Denied without silent posting; reviewed restoration requires explicit authorization (`P0505`/`P0506`) | All immutable receipt and audit records preserved under tenant isolation |
| **Legacy pre-permit queue** | Requires fresh online authority (`0` automatic reservations during recovery) | Supported pre-cutoff legacy invoices post once (`P0506LicensingTests`); offline A4/thermal print works from frozen local record | Unprovable legacy invoices stay held for authorized review | `licence_permit_id = NULL`, `licence_grant_id = NULL` (no fabricated permit links) |

## Isolated Current-Source Combined Compose Stack Acceptance

- **Disposable Compose project**: `intelibill-p01-05-908-20261123140013-54485` (`2026-11-23T14:00:13Z`–`14:04:16Z`) on stock ports (`14000`, `14001`, `14025`, `14277`, `14278`, `14432`, `14825`, verified unused via `lsof` before start).
- **Built shipping images & container exit codes**:
  - `db_bootstrap` (`sha256:fb288c51623c...`), `db_runtime_role` (`sha256:fb288c51623c...`), `licensing_runtime_role` (`sha256:fb288c51623c...`), `merchant_migrate` (`sha256:d23509e0c0b6...`), `licensing_migrate` (`sha256:1c3dd0accc6d...`): all **exited `0`**.
  - `postgres` (`sha256:fb288c51623c...`), `mailpit` (`sha256:245e558b2e44...`), `backend` (`sha256:447bf0d4b326...`), `licensing_api` (`sha256:020479abaebc...`): all **running (healthy)**.
  - `frontend` (`sha256:a05e1bb94572...`), `admin_frontend` (`sha256:20bc72f9840f...`): both **running**.
- **Observed stack behaviors**:
  - Browser phase 1 (`enroll`): `2 passed (8.1s)` — merchant enrollment through Mailpit and separate-origin admin denial for signed-in merchant browser.
  - PostgreSQL RLS check: `owner=1 runtime_unscoped=0 runtime_own_shop=1 runtime_other_shop=0`; database row counts `users=1 shops=1 memberships=1 all_users=1 all_shops=1`.
  - Restart persistence (`compose down` / `compose up -d` + browser phase `restart`): `2 passed (4.6s)` with identical database counts.
  - Readiness probes: `http://127.0.0.1:14277/health/ready` -> `200 Healthy`; `http://127.0.0.1:14278/health/ready` -> `200 {"service":"intelibill-licensing","status":"ready"}`.
  - Scoped cleanup (`scripts/dev/test-licensing-stack.sh --case cleanup`) removed only the disposable project and its volumes; `diff -u default-before.txt default-after.txt` against the default `intelibill` developer stack returned exit `0` (empty diff).

## Verification Command Matrix

| # | Working Directory | Command | Exit | Observed Result |
| --- | --- | --- | --- | --- |
| 1 | root | `dotnet build src/backend/Intelibill.slnx` | `0` | Build succeeded (0 warnings, 0 errors) |
| 2 | root | `dotnet build src/licensing/Intelibill.Licensing.slnx` | `0` | Build succeeded (0 warnings, 0 errors) |
| 3 | `src/frontend` | `bun run build` | `0` | Production bundle + `ngsw-worker.js` wrapper + `ngsw.json` + `ngsw-lite.json` built |
| 4 | `src/admin-frontend` | `bun run build` | `0` | Production admin bundle built |
| 5 | root | `P0509_RUN_BROWSER=1 scripts/dev/test-integration.sh --licensing --filter 'FullyQualifiedName~P0509Tests\|FullyQualifiedName~P0507Tests' --logger 'console;verbosity=normal'` | `0` | `10 passed, 0 failed, 0 skipped` (`56.30 s`) |
| 6 | root | `scripts/dev/test-integration.sh --filter 'FullyQualifiedName~P0501LicensingTests\|FullyQualifiedName~P0502LicensingTests\|FullyQualifiedName~P0504LicensingTests\|FullyQualifiedName~P0505LicensingTests\|FullyQualifiedName~P0506LicensingTests' --logger 'console;verbosity=normal'` | `0` | `119 passed, 0 failed, 0 skipped` (`52.70 s`) |
| 7 | root | `dotnet test tests/backend/unit/Intelibill.Application.Unit.Tests --filter 'FullyQualifiedName~OfflineInvoiceArithmeticValidatorTests\|FullyQualifiedName~OfflineInvoiceDocumentContractTests'` | `0` | `234 passed, 0 failed, 0 skipped` (`0.52 s`) |
| 8 | root | `dotnet test tests/shared/P0102ConsumerRegressionTests` | `0` | `15 passed, 0 failed, 0 skipped` (`11.50 s`) |
| 9 | root | `dotnet test tests/shared/P0103ConsumerRegressionTests` | `0` | `12 passed, 0 failed, 0 skipped` (`18.74 s`) |
| 10 | root | `bash .github/scripts/tests/licensing-stack-contract.test.sh` | `0` | `Licensing stack contract passed (11 services).` |
| 11 | `src/frontend` | `bun run audit:ui:release licensing-offline.spec.ts` | `0` | `4 passed, 0 failed, 0 skipped` (`33.2 s`) |
| 12 | `src/frontend` | `bun run audit:ui:release legacy-queue-recovery.spec.ts offline-permit-finalization.spec.ts offline-permit-refresh.spec.ts offline-permit.spec.ts offline-pack.spec.ts protected-cache-isolation.spec.ts` | `0` | `46 passed, 0 failed, 0 skipped` (`4.1 m`) |
| 13 | `src/frontend` | `bun run test --run offline-permit-clock offline-permit-refresh offline-licence-permit invoice-lease-indexeddb offline-sales-snapshot-sync offline-sale-sync.mapper offline-recovery-bundle offline-sale-pricing-calculator offline-invoice-document.builder offline-invoice-print.guard sale-invoice-print-page` | `0` | `14 test files, 297 passed, 0 failed` (`2.05 s`) |
| 14 | `src/frontend` | `bun run test --run scripts/tests/angular-worker-manifest.spec.ts scripts/tests/finalize-service-worker.spec.ts scripts/tests/offline-pack-worker.spec.ts scripts/tests/protected-api-worker.spec.ts` | `0` | `4 files, 59 passed, 0 failed` (`133 ms`) |
| 15 | `src/admin-frontend` | `bun x playwright test --config tests/release/playwright.release.config.ts device-permits.spec.ts` | `0` | `2 passed, 0 failed` (`2.1 s`) |
| 16 | root | `bash scripts/dev/test-licensing-stack.sh --case browser` + probes + `--case cleanup` (isolated project `intelibill-p01-05-908-*`) | `0` | Enroll `2 passed`, RLS `PASS`, restart `2 passed`, `/health/ready` `200` on `14277`/`14278`, default stack diff empty |
