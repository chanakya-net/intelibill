# P03-04 Razorpay through the neutral checkout adapter

Run from the repository root with Docker and the repository's Playwright
Chromium installed. Build the shipping merchant application first.

```sh
(cd src/frontend && bun run build)
export P0304_EVIDENCE_DIR=/absolute/path/to/issue-local/evidence/p0304
mkdir -p "$P0304_EVIDENCE_DIR"
dotnet test tests/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj --filter 'FullyQualifiedName~P0304Tests'
P0304_RUN_BROWSER=1 dotnet test tests/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj --filter 'FullyQualifiedName~P0304BrowserTests' --logger 'console;verbosity=normal'
```

The backend suite drives the real API and PostgreSQL against `FakeRazorpay`, an
in-process HTTP transport that imitates the documented order and payment
endpoints with fixture credentials. The browser fixture serves a stand-in for
`checkout.razorpay.com/v1/checkout.js` through Playwright routing. Phases:
`forged` (browser callback with a forged signature stays pending), `authorized`
(provider authorization alone stays pending), `captured` (a server-side read of
a captured full payment confirms and shows applying access), `applied`
(signed merchant acknowledgement).

Fixture limits: nothing here contacts Razorpay, validates the hosted checkout
page, or proves account activation, enabled methods or webhook reachability.
Those need the human-supplied test account described in the P03 PRD.

Razorpay payments expose `created_at` as payment creation time, not capture time. A
payment may be authorized and captured later, including with automatic capture
([payment lookup](https://razorpay.com/docs/api/payments/fetch-with-id/),
[payment timeout](https://razorpay.com/learn/payment-timeout/),
[capture settings](https://razorpay.com/blog/capture-settings-manage-failed-transactions)).

Time provenance: the adapter reports `created_at` only as the creation time and
never as the capture instant. A first purchase starts at fulfilment and is
unaffected. A renewal is granted only when classifying at the payment's creation
time and at the verification instant gives the same early/grace result;
otherwise the verified funds are kept as `PaidNeedsReview` and no term is
granted. `verified_payments.capture_time_basis` is `ObservedAtVerification` for
Razorpay (`captured_at` is the verification instant, a no-later-than bound) and
`ProviderCapture` where the provider proves the instant (the simulator).
Whole-second creation times are compared with the attempt time truncated to
the second.

Evidence rules: fulfilment needs one exact full capture. `amount_captured`
(when present) must equal the amount, `amount_refunded` must be zero, the order's
`amount_paid`/`amount_due` must agree, a present `partial_payment` must be a
boolean, and the payment `method` must be one of the configured methods
(`card`, `netbanking`, `wallet`, `upi`). Provider requests use only
`https://api.razorpay.com/` with redirects disabled; `MaxAmountPaise` is the
account's per-order maximum and must be configured. Dashboard-side method
enablement and the saved checkout configuration remain a human gate.
