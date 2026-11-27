# P05-03 require valid permission before offline finalization

The merchant web client verifies the stored signed `OfflineLicensePermitV1` before it takes an invoice number and again before it writes the issued invoice to the offline queue. A forged, untrusted, out-of-scope, edited, missing or expired permit, and a device clock that disagrees with its own earlier readings, consume no number and create no issued invoice. A valid issuance keeps the existing arithmetic (`calculationVersion: 1`), document (`document.version: 2`), issuer and queue ownership contracts, and freezes the permit it was issued under.

Scope of this slice: the browser only. The sync wire contract and the receiver that judges the frozen evidence belong to #903; nothing here changes what is sent to the API.

## What the browser trusts

| Item | Rule |
|---|---|
| Trust anchor | `IB_OFFLINE_PERMIT_PUBLIC_KEYS`, an Angular build `define` (`src/frontend/intelibill/angular.json`). Default: the literal `{}`. A release supplies `{"<kid>":"<base64 DER SPKI>"}` through build configuration or one `--define` argument. Public halves only. Overlapping entries cover key rotation. |
| No pins | A build with an empty map issues nothing offline (`OFFLINE_PERMIT_INVALID`). There is no API to fetch keys, no key kept in browser storage and no default fixture key. |
| Signature | WebCrypto `RSASSA-PKCS1-v1_5` / SHA-256 over the original `base64url(header).base64url(payload)` bytes, key chosen by the permit header's `kid` among own entries of the map, modulus at least 2048 bits. |
| Canonical form | Exact permit header (`intelibill-offline-permit+v1`), payload byte-identical to the fixed member order with no whitespace, lowercase hyphenated GUIDs, six-digit UTC instants. A validly signed non-canonical token is refused. |
| Stored fields | The lease's stored display fields must repeat the signed payload exactly. The display summary in device settings authorizes nothing. |

Delivery is unchanged from P05-02: a delivered permit is still stored after structural and binding checks, so the "Reconnect by" row keeps working in a build without pins. The signature is checked where it matters, at issuance, and when the clock checkpoint is established.

## Two guards, one critical section

`OfflineSaleFinalizationService.finalizeAndQueue` captures `ShopContextService.scope()`, the tab epoch, user, shop and session id before its first await, then runs the whole decision under one exclusive Web Lock per shop (`intelibill:offline-issuance:<shopId>`). Replacing a lease, attaching a permit and promoting a snapshot take the same lock, so they wait for an issuance in flight in any tab. Without Web Locks nothing is issued (`INVOICE_UNAVAILABLE`); enabling, snapshots and the offline pack keep working.

| Step | Check | On refusal |
|---|---|---|
| Interrupted claim | A number claim left by a cut-off issuance is settled with the cursor left advanced. Its number is never handed out again. If the queue cannot be read, nothing new is issued. | No number taken. |
| Guard 1 (before allocation) | Stored token verifies against a pin; stored fields equal signed fields; signed shop, user, session, device, lease, grant id and digest, promoted snapshot id and completion instant, frozen till session all equal the live ones; `validUntil` not after the lease expiry; clock evidence consistent; sale instant and now both in `[notBefore, validUntil)`. | No number taken, no queue row, no stock change. |
| Allocation | In one lease transaction: the stored lease id, cursor and permit bytes must be exactly the verified ones; the cursor advances and a claim `{leaseId, number, clientSaleId}` is recorded; the answer waits for the transaction to commit. | No number taken. |
| Guard 2 (before persistence) | Everything in guard 1 again, against the lease the number came from (same lease id, same permit bytes, cursor exactly one further), the same promoted snapshot, the same epoch and session. | The exact claimed number is given back. |
| Queue write | After the queue database opens and in the same task as the insert: owner, session and signed window, synchronously. A scope detach aborts the transaction while it can still abort. | Nothing stored; the number is given back. |
| Settle | Only after the queue transaction completes: the claim is cleared, then stock is reduced and the sale confirmed. | - |

Refusal reasons shown at checkout, with the cart kept: `OFFLINE_PERMIT_INVALID`, `OFFLINE_PERMIT_EXPIRED` (with the verified signed deadline, formatted as an exact IST instant) and `OFFLINE_CLOCK_INCONSISTENT`, in all nine locales.

## Frozen evidence (`OfflineQueuedSalePayload.licenceEvidence`, version 1)

```json
{ "version": 1, "permitId": "...", "grantId": "...", "grantSha256": "...", "leaseId": "...", "snapshotId": "...", "keyId": "...", "signedPermit": "<the original compact token, byte for byte>" }
```

Optional member. Present only on invoices issued by this code; never added to, defaulted on or recomputed for an older queued row. The existing sync mapper names its fields explicitly, so this member is not on the wire until #903 adds it.

## Clock evidence

A checkpoint is stored beside the lease (same IndexedDB database and store, key `clock::<shopId>::<deviceId>::<userId>`): owner and session, the verified permit id, the signed server issue time, the highest wall-clock reading seen, and a latch. Reads and writes happen in one readwrite transaction with the sample taken inside it.

| Event | Result |
|---|---|
| A permit arrives over the authenticated bound stream, its signature verifies against a pin for this session, and the device wall clock is within 5 minutes of the signed server time | Checkpoint established or rebased. Only a signed time later than the stored one is adopted; a replay changes nothing, a latch included. |
| No checkpoint, another owner or session, or a checkpoint for another permit | `unverified`: issuance refused until verified online. |
| Wall clock more than 5 s below the stored high-water | Latched `WallClockMovedBack`. |
| Within one document, wall clock advanced more than 5 s less than the monotonic clock | Latched `WallClockStalled`. A wall clock that ran ahead is not flagged. |
| Non-finite or negative reading | Latched `InvalidSample`. |
| Latched | Refused, whatever the permit says. Restoring the clock, reloading, a ping, a new stock timestamp or a new lease do not release it. Only a later verified signed observation does. |

The 5 s reading tolerance is measurement precision and is separate from the signed deadline, which is compared exactly (nanoseconds).

**The browser is not tamper-proof time.** A device whose clock is changed while it is switched off, or whose storage is edited, can present any time it likes; a device up to 5 minutes slow when verified gets that much longer. This evidence refuses issuance once the browser's own readings disagree with what it recorded, and nothing more. The server judges the frozen permit against its own clock on reconnect (#903).

## Behavior-first evidence (RED before product code)

Run on the #901 base `75910dc83`, product code untouched, with only the spec and its signing fixture added. The cart is genuinely issuable: the control test in the same run, identical except for the signature bytes, issued.

```sh
cd src/frontend/intelibill && bun run test --run src/app/features/sales/services/offline-sale-finalization.service.spec.ts
```

Observed: exit 1, `Tests 1 failed | 36 passed (37)`. Failing test `refuses a forged permit signature before consuming a number, queueing or reducing stock`:

```text
AssertionError: expected { result: { ok: true, …(2) }, …(3) } to deeply equal { result: { ok: false, …(1) }, …(3) }
-   "numbersConsumed": 0,      +   "numbersConsumed": 1,
-   "queueInserts": 0,         +   "queueInserts": 1,
-     "ok": false, "reason": "OFFLINE_PERMIT_INVALID"      +     "ok": true, (issued A/2627/000001)
-   "stockReductions": 0,      +   "stockReductions": 1,
```

The control `issues a genuinely sellable cart under a validly signed stored permit (control)` passed in that run. After the change the same command passes (see below); the only edit to that test was providing the throwaway public key through the new `OFFLINE_PERMIT_PUBLIC_KEYS` token, which did not exist on the base.

## Vitest scenarios

| Spec | What it proves |
|---|---|
| `offline-licence-permit.spec.ts` | #900's signed vector verifies in WebCrypto against its pinned public key and returns the original bytes. All 18 negative vectors are refused: altered signature, extended deadline, grant as permit, wrong purpose, validly signed whitespace / reordered / unknown member / unknown version / second-precision instant / uppercase GUID, padded signature, two segments, empty, unknown key id, four malformed headers. Empty map, other key id and an inherited property trust nothing. Overlapping pins select by the permit `kid`. A genuine 1024-bit signature is refused. |
| `offline-sale-finalization.service.spec.ts` | Forged signature, nine genuinely signed wrong bindings (shop, user, replacement session, device, lease, grant, grant digest, older snapshot, till), untrusted key, edited stored fields, missing permit, legacy lease, missing session id: refused with zero numbers, queue inserts and stock reductions. Fixed-clock window: issues at `notBefore` and at `validUntil - 1µs`, refuses at `notBefore - 1µs` and at `validUntil`, refuses a sale dated before the window. Clock `blocked` / `unverified` refuse. Between the guards: clock turning inconsistent, session replaced, newer snapshot promoted, last synchronous question refused: the exact claim is given back, nothing queued. No serialization: nothing issued. Interrupted claim: settled, never reused; unreadable queue: nothing issued. Valid issuance freezes the exact evidence, orders queue → settle → stock, keeps issuer, number, idempotency key, arithmetic v1 and document. |
| `offline-permit-clock.spec.ts`, `offline-permit-clock-indexeddb.service.spec.ts` | Every row of the clock table at fixed instants, including one millisecond either side of the tolerance, sibling tabs, reload, replayed and older observations, and release only on a later observation. |
| `invoice-lease-indexeddb.service.spec.ts` | Guarded allocation and claim in one durable transaction; another lease, a moved cursor or other permit bytes take nothing; rollback restores only the exact claim; two queued allocations never share a number. |
| `offline-sales-queue-indexeddb.service.spec.ts` | The write guard is asked after the database opens; refusal, a detached scope and an abort mid-transaction store nothing; an existing row stays byte-identical. |
| `offline-licence-permit.service.spec.ts`, `offline-sale-state.service.spec.ts` | Clock evidence is adopted only from a delivered signature that verifies against a pin; an unverifiable delivered permit is still stored. Each refusal maps to its message and keeps the verified deadline. |

## Browser scenarios (`offline-permit-finalization.spec.ts`, Chromium)

The spec builds the merchant app once into `src/frontend/intelibill/.ui-audit/offline-permit-build` (git-ignored) with production options and `--define IB_OFFLINE_PERMIT_PUBLIC_KEYS={"p0503-browser-key-1":"<SPKI>"}`, applies the exported `finalizeServiceWorker` and `writeOfflinePackManifests` to that output and serves it with the protected worker. The RSA-2048 key is made in the test process; its private half signs the fixture permits and never leaves that process. Lease, queue and stock stores are read raw with `page.evaluate`, not through Angular services.

| Scenario | Observed |
|---|---|
| Valid sale | Offline billing enabled through the normal online UI, network cut, page reloaded from the pack, sale submitted on `/sales/new`. Dialog "Sale Queued" shows `A/2627/000001`. Raw lease row: `nextNumber` 2, `remainingCount` 99, no claim. One `Pending` queue row for the captured shop, device and issuer, `licenceEvidence.signedPermit` equal to the delivered token, `pricing.calculationVersion` 1, `document.version` 2 under the lease's seller. Shadow stock 10 → 9. The offline print view opens with that number. |
| Expired next sale | The lease row's permit replaced by a genuinely signed one with the same identity whose window has ended. Message "Offline permission for this device has ended…", line "Offline permission ended: … IST". Cursor, remaining count, queue bytes and stock identical before and after; the earlier pending row byte-identical. |
| Forged signature; valid signature under an unpinned key; genuinely signed for another device; stored deadline edited with signed bytes untouched; no stored permit | Each: message "This device has no valid offline permission for this sale…", cart kept, raw `nextNumber` 1 and `remainingCount` 100 unchanged, no claim, pre-seeded pending row and pre-seeded stock row (quantity 7) byte-identical. With the genuine permit restored the same cart issues `A/2627/000001`. |
| Clock set back inside the window | Sale 1 issued five minutes into the ten-minute window. Clock set back four minutes (still inside the window): refused with the clock message, checkpoint latched `WallClockMovedBack`, durable state identical. Still refused after the clock is restored and after an offline reload. After the clock is corrected and a new bound snapshot delivers a later signed permit, the latch is cleared and the cart issues `A/2627/000002` under the new permit. |
| Two tabs | Tab A held between its guards by a test-side barrier in front of the native `SubtleCrypto.verify` (it waits, then delegates): claim `{number: 1}` visible, tab B's checkout queued behind the lock with nothing written. Released: A gets `000001`, B gets `000002`, cursor 3, no claim, no gap or duplicate. |
| Deadline passes while held | Tab A held with number 3 claimed; the page clock moves past the signed `validUntil`; released: refused with the ended message and deadline line, cursor back at 3, no claim, queue rows unchanged. |

## Commands and results

From `src/frontend/intelibill` unless stated. Results observed on the commit that adds this file.

| Command | Result |
|---|---|
| `bun run test --run src/app/features/sales/services/offline-sale-finalization.service.spec.ts` | 1 file, 66 tests passed |
| `bun run test --run offline-licence-permit offline-permit-clock invoice-lease-indexeddb offline-sales-queue-indexeddb offline-sale-finalization offline-sale-state offline-sales-snapshot-sync offline-sales-device-enablement` | 10 files, 194 tests passed |
| `bun run test --run offline-sale-pricing-calculator offline-invoice-document.builder offline-invoice-print.guard sale-invoice-print-page.component offline-sale-sync.mapper offline-recovery-bundle src/app/core/i18n/i18n-coverage.spec.ts` | 7 files, 231 tests passed |
| `bun run test --watch=false` | 342 files, 3492 tests passed; 107 script tests passed |
| `bun run build` | Exit 0. Initial total 756.89 kB; the same nine budget warnings as on the base, no new warning. |
| `bun run audit:ui:release offline-permit-finalization.spec.ts offline-permit.spec.ts offline-pack.spec.ts` | 11 passed: 4 new, 2 P05-02 delivery, 5 offline pack |
| Repository root: `dotnet test tests/backend/intelibill/integration/Intelibill.Integration.Tests --filter 'FullyQualifiedName~P0502LicensingTests\|FullyQualifiedName~P0501LicensingTests.Historical_validation_rejects_wrong_scope_and_signature\|FullyQualifiedName~P0501LicensingTests.Rotation_preserves_only_public_historical_proof'` | 17 passed, 0 failed, 0 skipped. Run with the .NET 10 SDK directory first on `PATH` (`/usr/local/share/dotnet`, SDK 10.0.300 by `global.json` roll-forward). The machine's default `dotnet` is an SDK 11-only install and exits 155 (`A compatible .NET SDK was not found`) before building anything; that is the workstation's SDK lookup, not a test result. |

The PostgreSQL command is #901's unchanged suite on real PostgreSQL (Testcontainers): server deadline calculation, parallel bound streams, scope and signature rejection, retained public keys. No backend file changed in this slice. It does not exercise browser atomicity; the browser scenarios above do not exercise the server.

## Evidence limits

- Browser scenarios use local fixture API answers and a throwaway signing key made in the test process. They run the production build, the protected service worker, the offline pack, WebCrypto RSA verification, Web Locks and IndexedDB for real. This is not live provider verification: no licensing provider, payment provider or key vault was contacted, and no production key exists in this repository.
- Vitest uses #900's immutable vectors (`offline-permit-v1.json`) for the signature rules and throwaway keys for scope and window cases.
- The two-tab and clock scenarios use Playwright's page clock and a barrier in front of the native verify call. They show ordering and refusal in a real browser; they are not a proof about physical time or about a browser whose storage or script was altered.
- Production release builds must supply the public half of the configured `OfflineLicensePermit` signing key (and of any key still in rotation) through `IB_OFFLINE_PERMIT_PUBLIC_KEYS`. Until they do, offline issuance is refused in that build. Leases without grant evidence (legacy, or reserved while the server fell back) cannot issue offline either; they need a new online reservation. Nothing here enables production enforcement on the server.

## Review regressions

On reviewed commit `54df1bfab`, the regression run reported 41 failures and 158 passes before the fixes. The original #901 RED run above remains the baseline for issuance without a valid permit.

| Review | Regression covered |
|---|---|
| R001 | Leave and return to the same shop while auth grace is pending. The finalizer receives no call. Detach after a durable issue and only the original shop's stored lease count changes. A stale page result preserves the active cart, error and deadline. Reset and early payment validation clear a previous deadline. |
| R002 | WebCrypto verifies the canonical signed control. A signature with unused base64url bits set and signed noncanonical header and payload segments are refused. A malformed segment reaches neither allocation nor the queue. |
| R003 | Start a competing finalization at snapshot promotion. Its queued invoice carries the newly attached permit and the clock accepts the new permit ID. |
| R004 | Observe successful IDB puts during save, attach and rollback. All three operations settle on transaction completion. An abort after the put rejects each operation. |
| R005 | Missing session or restricted scope refuses before the snapshot is read. Stock availability is read without seeding a row. In Chromium a refused first sale leaves an empty shadow store empty. |
| R006 | A till-less request under an enabled till and an unknown capability both refuse before allocation. Clearing selection after allocation or inside the queue write restores the claim and leaves stock untouched. |

After the fixes, the full merchant suite passed all 3,511 Vitest tests and 107 script tests. The production build completed with its nine existing budget warnings. All 11 Chromium release scenarios and all 17 selected PostgreSQL integration tests passed.
