# P04-08c supplier capacity

| Path | Source decision and existing behavior |
| --- | --- |
| `POST /api/suppliers` | Owner only. The handler checks `OperationalCapacity` under the account and shop licence fence before inserting any ordinary supplier, including inactive rows. |
| `PUT /api/suppliers/{id}` | Owner only. A transition from inactive to active requires current operating access. With an expiry, pending or stale-verification refusal, an edit that requests active locks the existing row and confirms it is still active. Edits that keep the supplier inactive or deactivate it remain available under ordinary edit authority. Trading and security suspensions retain their write restrictions. |
| `GET /api/suppliers`, directory, detail and ledger | Existing history permissions and tenant isolation apply. |
| Supplier payment and source-linked return | Existing roles, receipt eligibility and posting constraints apply. An authorized operator can settle a prior payable or ship a return from its original receipt under expiry, pending activation or trading suspension. Security suspension restricts access in its own scope. |
| Shop bootstrap | `CreateShopCommandHandler` provisions the single system Unknown Supplier. Ordinary supplier requests cannot create a system row. |
| Single intake and per-row bulk intake | An explicit ID resolves within the shop. A missing ID resolves the existing system supplier. A foreign ID fails and inline supplier data cannot create an identity. |
| Purchase-order create, place and receive | Draft supplier text is descriptive. Placement requires an active, nonsystem supplier ID in the shop. Receipt posting uses the placed order's supplier. |
| Batch supplier reassignment | Resolves existing supplier IDs under the inventory correction workflow. |

The merchant code has one ordinary supplier insert, in `AddSupplierCommandHandler`. The supplier directory owns the web create form. Inventory and purchase-order forms select existing supplier IDs.

The directory displays typed trading denials, keeps the attempted supplier form open with its values, and requires a fresh verified result or a confirmed observe-only state before resubmitting a blocked draft. A busy response is retryable in the form. Responses from a prior shop scope are discarded.

The PostgreSQL tests use a controlled clock, fixture grants and real merchant routes. The Chromium release spec uses built merchant assets and local HTTP fixtures. Production enrollment and provider integrations are outside this slice.
