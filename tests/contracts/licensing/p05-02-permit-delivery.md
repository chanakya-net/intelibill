# P05-02 attach permit evidence to leases and snapshots

The merchant API attaches versioned grant evidence to reserved invoice leases and delivers the signed `OfflineLicensePermitV1` through the existing NDJSON offline snapshot stream when bound to a lease. The web client keeps the existing offline enablement order (offline app pack, complete stock snapshot, invoice lease), requests one bound snapshot for an anchored lease, verifies structural and binding agreement, stores the permit on the lease record in IndexedDB, and displays the exact IST reconnect deadline in Manage Shop. Enforcement before invoice finalization, clock-rollback blocking, and historical sync validation belong to later slices (#902–#905).

## Wire contract (`offline-permit-delivery-v1.json`)

| Path | Wire addition | Compatibility |
|---|---|---|
| `POST /api/sales/invoice-leases/reserve` | Trailing optional `licenceEvidence`: `{ version: 1, grantId, grantSha256, keyId }` (`null` for legacy/unanchored leases). | Older clients ignore the extra member; new clients treat `null`/absent as a legacy lease and never send snapshot binding parameters. |
| `GET /api/sales/offline-snapshot/stream` | Optional query group `leaseId`, `grantId`, `deviceId` (all three required together; partial or empty binding answers HTTP `400`) plus optional `tillSessionId` (read only when bound). | Unbound requests remain byte-compatible (`schemaVersion: 4`, zero `licencePermit` records). Bound requests emit one `{"type":"licencePermit","licencePermit":{...}}` record immediately before `complete`, and stamp `complete.completedAt` from the capture completion instant. |
| `licencePermit` (`status: "issued"`) | `{ version: 1, status: "issued", leaseId, grantId, snapshotId, permitId, keyId, signedPermit, notBefore, validUntil, limitingReason }` | `grantSha256` inside `signedPermit` matches the lease's `licenceEvidence.grantSha256`; `snapshotCompletedAt` matches `complete.completedAt`. |
| `licencePermit` (`status: "refused"`) | `{ version: 1, status: "refused", leaseId, grantId, snapshotId, refusalCode }` | Code only, never a description; `complete` still follows so stock snapshot delivery is not broken. |

## Reservation fallback contract

Inside `ReserveInvoiceLeaseCommandHandler`'s transaction, `IOfflineLicencePermitIssuer.CaptureGrantAsync(session, lease)` runs after `invoiceLeaseRepository.AddAsync(lease)` and before `SaveChangesAsync`:

- **Legacy fallback (commits unanchored lease, `licenceEvidence: null`, zero evidence rows)**: `OfflinePermit.SigningUnavailable`, `OfflinePermit.AuthorityUnavailable`, and non-enforced `Licence.*` outcomes (excluding `Licence.Busy`) confirmed by re-reading `IShopLicenceFence.ReadTradingDenialAsync` in the same transaction.
- **Refuse and roll back (no lease, no sequence consumption, zero evidence rows)**: `Auth.SessionInvalidated` (including a session lacking a usable refresh credential), `OfflinePermit.ScopeMismatch`, `OfflinePermit.LeaseNotNew`, `OfflinePermit.KeyRebound`, `OfflinePermit.Busy`, `Licence.Busy`, `OfflinePermit.OutsideValidity`, `OfflinePermit.DeadlineReached`, and enforced `Licence.*` denials.

## PostgreSQL scenarios (`P0502LicensingTests`)

| Scenario | Observed behaviour |
|---|---|
| Licensed HTTP reservation | Returns `licenceEvidence` v1 (`grantId`, `grantSha256`, `keyId`) matching the single persisted `OfflineLicenseGrant` and `OfflineLicenseVerificationKey` rows. |
| Legacy fallback and atomic rollback | Unlicensed (observe-only unprojected) shop and missing signing key return a legacy lease with zero evidence rows. Expired refresh credential (`401 Auth.SessionInvalidated`) and enforced trading restriction (`403 Licence.TradingRestricted`) roll back the lease and invoice sequence range. |
| Reachable deadline caps over HTTP | `AuthorityFreshness`, `LicenceEnd`, scheduled suspension (`LicenceEnd`), `CurrentLicenceEnd`, `AuthDeadline`, lease ending with fiscal year (`LeaseExpiry`, contract vector `lease-ends-with-fiscal-year`), `TillDayEnd`, and `FreshnessTie` each agree across reservation `licenceEvidence`, stream `licencePermit`, `complete.completedAt`, and `IOfflineLicencePermitIssuer.VerifyAsync`; issued at `validUntil - 1µs` and refused at `validUntil` with unchanged evidence counts. |
| Stale licensing vs newer snapshot | Downloading a newer snapshot, and applying a more generous entitlement head (revision 2), keep the original grant's `authorityVerifiedAt` and deadline; at the deadline the old lease gets `refused` (`OfflinePermit.DeadlineReached`) with zero new rows, while a new reservation adopts revision 2. |
| Unbound compatibility and scope isolation | Unbound stream emits `schemaVersion: 4` with zero `licencePermit` records and zero permit rows. Partial or empty bindings return HTTP `400`. Cross-shop grant, unknown grant, wrong device, wrong lease, second Owner session, and Staff caller return `status: "refused"` (no `description`) and write zero permits. |
| Concurrency and mid-stream restriction | Four parallel bound streams on shop A and two on shop B produce isolated permits bound to their own leases/grants/deadlines (1 grant, 4 permits, 1 key on A; 1 grant, 2 permits, 1 key on B). A trading restriction committed before completion withholds the permit (`Licence.TradingRestricted`, zero permits stored). |
| Shared delivery fixture parity | Live reservation `licenceEvidence` and bound stream `issued`/`refused` records match the exact property sets in `offline-permit-delivery-v1.json`, cross-checked against `offline-permit-v1.json`. |

Strict `FiscalYearEnd`, `DeviceFreshness`, `SnapshotLifetime`, and `StockEvidenceExpiry` wins cannot be produced through `ReserveInvoiceLease` (the handler never creates a lease outliving its fiscal series and reservation always follows verification); they remain pinned by `tests/contracts/licensing/offline-permit-v1.json` and `P0501LicensingTests`.

## Browser scenarios (`offline-permit.spec.ts` and Vitest)

- **Vitest (`offline-licence-permit.spec.ts`, `offline-licence-permit.service.spec.ts`, `offline-sales-snapshot-sync.service.spec.ts`, `offline-sales-device-enablement.service.spec.ts`, `invoice-lease-indexeddb.service.spec.ts`, `manage-shop-offline.helper.spec.ts`, `manage-shop-overlay.component.spec.ts`, `locale-formatting.service.spec.ts`, `i18n-coverage.spec.ts`)**: decodes #900's signed permit vector, rejects grant tokens and every mutated binding field, verifies `attachPermit` lease/grant guards and later-`issuedAt` precedence, confirms post-promotion storage ordering, confirms existing pack -> snapshot -> lease enablement gates with one extra bound snapshot for anchored leases, formats exact IST seconds without rounding past the deadline, and checks all 9 locale files.
- **Playwright Chromium (`offline-permit.spec.ts`, `offline-pack.spec.ts`)**: against the built production app, real protected service worker (`ngsw-worker.js`), offline pack, and real IndexedDB (`intelibill-invoice-leases`), proves enabling stores `licenceEvidence` and `offlinePermit`, renders `Reconnect by` `22 Nov 2026, 5:30:00 pm IST` in Manage Shop (without showing `limitingReason`), retains the stored permit and deadline across later snapshots answered without a permit / `refused` / wrong-lease while advancing `lastCompleteSnapshotAt`, preserves the displayed deadline after offline reload with the network cut, and keeps legacy leases to a single snapshot with no permit row.

## Evidence limits

- **Server-side verification (`P0502LicensingTests`)**: entitlement projections use a synthetic verified assertion applied through `EntitlementProjectionStore.ApplyAsync` (HTTP entitlement signature verification is covered by `P0402LicensingTests`); grants and permits are signed and verified with locally generated 2048-bit RSA keys via the named signing-key facility.
- **Browser verification (`offline-permit.spec.ts`)**: uses local fixture API responses and well-formed fixture compact tokens matching `offline-permit-delivery-v1.json`; the browser checks token structure and binding fields over the authenticated TLS channel rather than verifying RSA signatures locally. Cryptographic signing and verification remain server-owned and are proved in `P0502LicensingTests` / `P0501LicensingTests`.
- No live licensing provider, payment provider, or Azure Key Vault was contacted, and production enforcement remains disabled.
