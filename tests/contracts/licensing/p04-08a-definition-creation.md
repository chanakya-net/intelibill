# P04-08a item and service definition creation

| Merchant entry point | Licence action and existing authority |
|---|---|
| `POST /api/items` | `OperationalCapacity` in the item write transaction. Owner or Manager, including inactive definitions. |
| `POST /api/services` | `OperationalCapacity` in the service write transaction. Owner or Manager. The shop’s code sequence commits with the service. |
| `GET /api/items/details` | `History` for a local name or barcode match. A usable external barcode match enters an `OperationalCapacity` transaction before any local definition is inserted. Staff can still import when the shop is operating. |
| Item directory and stream, existing service directory | Existing history and role checks. Security suspension still blocks shop data. |

The write transaction checks live session authority before the account and shop fence and checks it again after a wait. The external catalogue call completes outside that transaction. A committed local match found after a wait uses history permissions, and the shop/barcode uniqueness constraint resolves simultaneous imports. Product-created notifications run after the definition commits.

| Enrolled shop decision | API response |
|---|---|
| Pending activation | `403 Licence.PendingActivation` |
| At the verified trading deadline | `403 Licence.Expired` |
| Trading restriction | `403 Licence.TradingRestricted` |
| Security suspension | `403 Licence.SecuritySuspended` |
| At the verification freshness limit while trading time remains | `503 Licence.VerificationUnavailable` |
| Fence contention | `409 Licence.Busy` |
| Observe-only | Existing eligibility applies |

The merchant web displays current access for Add Product, Add Service, and purchase-order quick-create. A typed refusal from the write keeps entered values and takes precedence over an older hint. A later verified response enables retry. A known refusal prevents quick-create before barcode allocation, while a local item stays selectable. An import refusal appears at the barcode field or optional cart enrichment and leaves entered stock or cart data intact.

Release fixtures use production-built merchant assets and routed local API responses in Chromium. PostgreSQL integration tests cover durable writes, source/projection ordering, service code rollback and stock history. Production enrollment remains disabled.
