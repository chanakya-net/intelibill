# P05-07 — Device contact and outstanding offline permission

Provider operators see, per shop and opaque device reference, what the merchant service observed about offline
permission. Pinned by `offline-device-observation-v1.json` and `tests/backend/licensing/integration/P0507Tests*.cs`.

## Rollout order

1. Licensing migration `20261124100000_OfflineDeviceObservations`, then the licensing API.
2. Merchant migration `20261124090000_AllowDeviceObservationMerchantEvents`, then the merchant API.

Merchant first is unsafe: without its migration every new permit and offline upload fails the outbox kind check, and
against an older licensing API the observations are rejected, dead-lettered after 12 attempts and never re-sent.
Both `Down` migrations refuse (SQLSTATE `55000`) while any device observation is retained. No production activation is implied.

## What crosses services

One signed merchant event (`MerchantEvents` assertion, existing leased outbox and inbox, strict JSON, ≤ 8192 bytes) at
`POST /internal/licensing/v1/merchant-events/device-observation`, message kind `DeviceObservation`:

`schemaVersion, eventId, accountId, shopId, affiliationRequestId, deviceRef, observation, observedAt, permit`

- `observation = PermitIssued`: appended in the transaction that stores a new offline permit (never on a redelivered or
  refused permit). `permit` carries `permitId, notBefore, validUntil, limit, accountRevision, shopRevision, authorityVerifiedAt`;
  `observedAt = notBefore`.
- `observation = OfflineSync`: appended in the transaction that retains an offline upload's first envelope
  (`POST /api/sales/offline-sync`), at most once per device per request whatever the number of invoices; `permit` is null and
  `observedAt` is the server's receipt instant. Recovery-bundle imports are not device contact and report nothing.
- `deviceRef` is lowercase hex SHA-256 of UTF-8 `"{shopId:D}\n{deviceId}"`; the client's device identifier stays with the merchant.
- `limit` is `LicenceEnd`, `VerificationFreshness` or `MerchantLimit` (see the fixture's mapping).
- Never sent: invoice, customer, stock, amount, queue size, user, session, lease, grant, snapshot, till, signed permit bytes.
- Every instant is the merchant server's clock. Licensing validates structure only; the two services' clocks are not compared.
- A shop with no licence binding reports no upload. Permits and uploads from before this rollout are not backfilled.

Licensing keeps each event once in append-only, account-scoped `offline_device_observations` (forced RLS, update/delete/truncate
refused, `permit_id` unique). An event is accepted only for the shop's current `(account, shop, affiliation request)` binding;
anything else answers 409 and stores nothing. Replayed assertions answer 401; a re-signed identical event answers 204.

## Provider read

`GET /api/admin/accounts/{account}/device-permits?shop=&after=` (`no-store`; live Support, Licensing Manager or Security
Administrator grant re-read under lock; Billing or Platform Owner alone → 403 `Status.AccessDenied`; a shop outside the
account → 404 `Status.ShopNotFound`). 50 devices per page ordered by `(shopId, deviceRef)`. In the admin console,
Security Administrator alone navigates to `/accounts` via `Device permissions` and queries only `/device-permits`
(never `/status` or `/closures`), keeping commercial/subscription terms inaccessible.

| Field | Meaning |
|---|---|
| `evaluatedAt` | Licensing status clock at the read. |
| `lastPermitIssuedAt` | Latest `PermitIssued` observation. |
| `lastSynchronizedAt` | Latest `OfflineSync` observation; `null` when none was observed. Never a permit's time. |
| `lastContactAt` | Latest observation of either kind. |
| `knownPermitEnd` | Latest `validUntil` among the device's permits; kept after it passes. |
| `outstanding`, `outstandingPermits` | Half-open: outstanding strictly before the end, evaluated at `evaluatedAt`. |
| `limit`, `accountRevision`, `shopRevision`, `authorityVerifiedAt` | Those of the permit that sets `knownPermitEnd`. |
| `disconnectedQueues` | Always `Unknown`. A received upload does not show that a device holds no unsent invoices. |
| `observedSince` | The account's earliest retained observation, `null` when none. Devices whose last contact was earlier are absent, which is not proof that no permission is outstanding. |

A suspension, replacement or transfer does not revoke an issued permit: the read keeps showing its window, and the
merchant service issues no further permit under the restriction.

## Verification boundaries

- PostgreSQL: both services on Testcontainers with fixed clocks and locally generated signing keys; no live licensing
  provider, key vault or device is contacted.
- Browser: `src/admin-frontend/tests/release/device-permits.spec.ts` runs the production build in Chromium at 1440 px and
  390 px against routed fixture HTTP. It shows what the built panel renders for those facts, not live provider behaviour.
