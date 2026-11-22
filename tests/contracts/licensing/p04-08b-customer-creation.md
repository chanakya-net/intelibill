# P04-08b Customer creation

`POST /api/customers` and `AddCustomerCommandHandler.HandleAsync` are classified as `OperationalCapacity`. The handler checks the existing credit-settings authority, validates the live merchant session, enters the account/shop licence fence inside the customer transaction, and revalidates the session before inserting. An allowed create retains its HTTP 201 response.

| Current decision for an enforced shop | Create response |
| --- | --- |
| Trial, active or grace with verified authority | 201 when existing validation and authority checks pass |
| Pending activation | 403 `Licence.PendingActivation` |
| Trading deadline reached | 403 `Licence.Expired` |
| Trading suspension | 403 `Licence.TradingRestricted` |
| Security suspension | 403 `Licence.SecuritySuspended` |
| Verified authority unavailable | 503 `Licence.VerificationUnavailable` |
| Licence fence contention | 409 `Licence.Busy` |

Unenrolled and observe-only shops retain their existing creation behavior. Existing customer history, credit reads and eligible payments retain their route authority. The regression records a 2.00 allocated payment against a real 3.00 due and checks replay against the original receipt. Existing customer edits retain their current rules, including the trading suspension filter. Restricted settlement eligibility for advances and overpayments belongs to P04-12.

The customer API is the sole merchant customer insert entry point. Checkout and offline posting resolve existing customers by ID or phone. Cash walk-in details are sale text. Credit checkout with an unknown customer needs an existing customer record. The merchant checkout has an existing-customer search and walk-in fields. Its sale submission uses the P04-05 gate.

The customer directory keeps existing rows visible and disables Add Customer when the shared trading authority hint is blocking. A source refusal keeps the form values and offers Check again. A busy response remains retryable on the form. Responses from a previous shop epoch are ignored.

`P0408bLicensingTests` exercises the API and direct handler with a fixed clock and real PostgreSQL. The races inspect `pg_blocking_pids` for both admission orders and check revocation and contention. `licensing-customer-creation.spec.ts` exercises production-built merchant assets in Chromium with local routed API fixtures. The browser logs record simulated HTTP responses and screenshots at desktop and mobile sizes.
