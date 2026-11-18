# P04-05 sale posting and recovery

The merchant API evaluates `NewTrading` inside the source transaction for online goods, service and mixed sales, and for new invoice-lease reservations. The existing fence reads the local entitlement and holds the account and shop locks through commit. Existing business validation and posting locks still apply.

## PostgreSQL scenarios

| Scenario | Expected outcome |
|---|---|
| Enrolled shop reaches its access end | Goods, service and mixed requests receive `403 Licence.Expired`. Sale, stock, customer ledger, till and sequence writes are absent. |
| Authority reaches its 48-hour limit before access ends | `503 Licence.VerificationUnavailable`. |
| Pending activation | `403 Licence.PendingActivation`. |
| Sale reaches its insert before a restricting projection | The projection waits for the sale backend. The sale commits and subsequent new keys are refused. |
| Projection holds its fence before the sale enters | The sale waits for the projection backend and evaluates the committed restriction. |
| First attempt is inserting when its retry arrives after expiry | The retry waits for its own earlier attempt and returns the same sale and invoice. |
| Recovery after expiry or trading suspension | The same actor, shop, key and payload recover the committed invoice. Changed payloads conflict. Security suspension and membership removal prevent disclosure. |
| Existing commerce, tax and role gates | Stock, no-credit, invoice tax and lease role refusals retain their own codes. A document refusal after allocation rolls back its invoice number. |
| Previously issued offline invoice | It can still reach the existing historical sync path. New lease requests are subject to current trading access. |
| Observe-only shop | Online checkout remains available. Trading-authority denials include `enforced: false`. |

The same-key advisory wait uses the existing database command timeout and request cancellation. A lost response or server fault leaves the earlier outcome unknown to old checkout clients. A definitive licensing refusal is returned only after the sale path has checked for a committed key. A security suspension can refuse access before lookup, so current checkout clients retain the original intent in that case.

## Production-build Chromium fixture

The browser exercises the merchant checkout at desktop and mobile widths with routed API fixtures. It verifies retained carts and distinct expiry and verification-outage messages, owner-scoped IndexedDB recovery after reload, the same key and body on retry, and a single fixture sale when the first request committed. An early status lookup returning 404 leaves the checkout pending. Observe-only expiry leaves the submit button usable.

The fixture validates the browser flow with service workers disabled for these cases. PostgreSQL scenarios provide the transaction, rollback and duplicate-sale evidence. Provider behavior was not live verified. Production enforcement enrollment remains disabled.
