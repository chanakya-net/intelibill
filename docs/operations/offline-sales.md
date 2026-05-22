# Offline Sales Operations

Offline billing is a controlled fallback for shops that need to keep one billing counter running during short connectivity loss. Billing correctness depends on explicit IndexedDB stores and sync logic: the service worker may cache the app shell, but offline sales must not rely on stale cached API responses.

## Enablement

- Enable offline billing only while the browser can reach the API. The setup downloads a complete sales snapshot, reserves an invoice lease, and stores the enabled-device settings locally for the active shop.
- Only Owner and Manager users can enable a browser profile/device. Staff users can use a previously enabled device, but they cannot enable a new browser profile/device.
- Enablement is scoped to one browser profile on one device. A different browser profile, OS account, private browsing profile, or cleared browser storage requires a fresh Owner/Manager enablement while online.
- Pilot recommendation: run one offline billing counter per shop until multi-device reconciliation is validated in production operations.

## PWA And API Cache Safeguards

- The production service worker caches the app shell so the installed app can open offline.
- Connectivity ping (`GET /api/ping`), snapshot stream (`GET /api/sales/offline-snapshot/stream`), invoice lease reservation (`POST /api/sales/invoice-leases/reserve`), and offline sync (`POST /api/sales/offline-sync`) are configured ahead of the broad `/api/**` data group with `maxAge: 0s`.
- The connectivity ping uses `fetch(..., { cache: 'no-store' })`, and the snapshot stream also uses `cache: 'no-store'`.
- Both browser fetch calls append the Angular service-worker bypass query (`ngsw-bypass=true`) so connectivity and snapshot writes do not come from stale service-worker API entries.
- POST requests are not a source of offline correctness. Invoice lease reservation and sync are online-only operations and must fail or wait when the API is unreachable rather than using stale cached data.

## Staff Use

- Staff can continue billing on an enabled device when the API is unreachable and the offline requirements are still valid.
- Staff should not switch browser profiles or devices during an outage. If the enabled profile is unavailable, wait for an Owner or Manager to enable another device after connectivity is restored.
- Staff should keep the offline counter visible to the shop lead so pending sync, warning, and review states are checked before closing the shift.

## Invoice Leases

- Default lease size is 200 invoice numbers. The backend maximum accepted block size is 1000.
- The frontend renewal threshold is 25 remaining numbers. When the API is reachable, renew before the count drops below 25.
- A lease expires 7 days after reservation. Expired or exhausted leases block offline sale finalization.
- Invoice numbers are consumed locally at finalization time. Numbers already printed or queued must not be reused. If a local sale is abandoned before it is queued, rollback is attempted only when the local lease cursor still matches; otherwise the number should be treated as abandoned and not manually reused.
- Support should reconcile abandoned numbers by looking at the device ID, lease ID, fiscal year, invoice number range, local queue state, and server sales/reconciliation records.

## Snapshot Freshness

- Offline pricing, customer-lite data, active leases, batches, stock, and discount rules come from the last completed snapshot in IndexedDB.
- The sales page shows stale snapshot warnings every 4 hours of snapshot age.
- Offline sale finalization is blocked after 48 hours or when the snapshot is missing/invalid.
- When online, refresh the snapshot before starting an offline-capable shift and after a long outage.

## Payments And Due Sales

- Offline payment methods are Cash, UPI, Card, and Credit/Due.
- Offline UPI is cashier-attested: the cashier is asserting that they verified the payment outside the app, for example by checking the UPI app, bank notification, terminal, or other shop-approved proof. The app cannot verify UPI while offline.
- Offline Card is also an attested payment unless the shop has a separate online terminal confirmation path.
- Credit/Due sales require a cached customer from the offline snapshot. Offline customer creation is not allowed, and a due sale without a cached customer is blocked.

## Sync States

- Pending: sale is queued locally and waiting for API reachability.
- Syncing: sale is currently being sent to the API.
- Synced: sale was accepted by the server.
- SyncedWithWarnings: sale was accepted, but one or more reconciliation warnings were retained for follow-up.
- NeedsReview: the server could not safely accept the sale without human review, for example invoice conflict or idempotency conflict.
- Failed: sync request failed or the server returned a retryable failure.
- Pending-sync invoices remain operational sales records. Do not reprint or recreate them manually unless support has confirmed the local record cannot sync.
- Retained support data includes device ID, client sale ID, invoice number, sold-at timestamp, customer-lite fields, line snapshots, totals, warnings, error codes/messages, and server sale ID when available.

## Stock Variance And Reconciliation

- Offline sale finalization uses snapshot stock and local shadow-stock checks. The server revalidates current stock during sync.
- If current server stock is lower than the printed offline quantity, the server consumes the available quantity and records a stock variance reconciliation issue.
- A stock variance means the printed customer invoice may not match the stock quantity the server could consume. Operations must review physical stock movement, later online sales, wastage, returns, and manual adjustments before closing the issue.
- Pricing, discount, validation, customer, and invoice warnings should be reviewed with the same client sale ID and invoice number so the printed invoice, local queue record, and server record stay tied together.

## Local Data Retention

- Fully synced queued records (`Synced`) are retained for at most 3 days before local cleanup.
- Records requiring follow-up (`SyncedWithWarnings`, `Pending`, `Syncing`, `Failed`, and `NeedsReview`) are retained until they are resolved or manually handled by support.
- Clearing site data, changing browser profile, or uninstalling the PWA can delete local offline data. Do not clear browser storage on an offline-enabled device until pending and review records are resolved.

## Device And Browser Security

- Treat an offline-enabled browser profile as shop-sensitive because IndexedDB contains sales-flow data, queued invoices, invoice lease state, customer-lite records, and snapshot pricing/stock data.
- Use a dedicated browser profile or OS account for the billing counter.
- Require OS login, screen lock, and physical control of the device.
- Do not share the enabled browser profile with personal browsing, extensions, or untrusted users.
- Do not leave an enabled offline counter unattended during an outage.
- If a device is lost, stolen, repurposed, or handed to another shop, revoke user sessions, review pending offline records, and clear the browser profile only after support confirms no unresolved records remain.

## Production Browser QA

Run this checklist against a production build because Angular's service worker is not fully exercised in development mode.

- Build with `cd src/frontend && bun run build`.
- Serve the production output over `http://localhost:<port>` or another local HTTPS/localhost origin, for example `PORT=4207 node dist/INVENTORY/server/server.mjs`.
- Open the app, verify `ngsw-worker.js` registers, and verify `ngsw.json` contains the app shell and the no-cache offline correctness data groups.
- Reload once after registration so the controlled page uses the service worker.
- Toggle the browser offline in DevTools or browser automation and verify the installed/app-shell route still loads.
- Verify no API response from `/api/ping`, `/api/sales/offline-snapshot/stream`, `/api/sales/invoice-leases/reserve`, or `/api/sales/offline-sync` is served stale from the service-worker API cache.
- While online with a test shop, enable offline billing as Owner/Manager and confirm snapshot completion plus invoice lease persistence.
- As Staff on the same enabled device, confirm offline sale flow can open after API loss.
- As Staff on a new browser profile/device, confirm enablement is blocked.
- Create test offline invoices for Cash, UPI, Card, and Credit/Due with a cached customer; confirm due sale without cached customer is blocked.
- Restore API connectivity, confirm queued sales advance through sync states, and review any `SyncedWithWarnings` or `NeedsReview` records.

Current QA status for issue #367: automated production app-shell QA was attempted locally on May 22, 2026 and should be recorded with the implementation result. Human QA remains required for role-specific enablement, live offline sale payment flows, and operations wording review against shop procedures.
