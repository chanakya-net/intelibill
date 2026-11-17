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
`checkout.razorpay.com/v1/checkout.js` through Playwright routing. First run,
phases: `forged` (browser callback with a forged signature stays pending and
grants nothing; a proof-free recheck on reload proves the same order unpaid and
offers it again, with the pinned method display configuration), `authorized`
(provider authorization alone stays pending), `captured` (a server-side read of
a captured full payment confirms and shows applying access), `applied`
(signed merchant acknowledgement). Second run (`recovery-build-and-postgres.json`):
`lost` (lost create response and a failed first verification read; Check again
and reload recover the original unpaid order), `tabs` (a second tab's older
awaiting read cannot reopen a newer pending claim; a fresh recheck reopens the
same order; a checkout callback left outstanding across a change to a second
billing account sends no verification), `recheck` (the original order fulfils
through Check again with one provider order POST).

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

Evidence rules: fulfilment needs one exact, unrefunded full capture stated
identically by three authenticated reads. The order must be `paid` with explicit
`amount_paid` equal to the amount and explicit `amount_due` of zero; the order's
payment collection must hold exactly one captured candidate; the payment's own
read must equal that collection entry field for field, state `captured`, carry
an explicit `amount_refunded` of zero and no refund status, and use one of the
configured methods. Only `amount_captured` (which must equal the amount when
present) and `partial_payment` (which must be a boolean when present) may be
absent. Anything absent, null, contradictory or ambiguous is held as pending
verification and grants no term.

Recovery: a pending order is offered again only when a proof-free recheck (never
a request carrying a browser claim) reads the original order as `created` or
`attempted` with explicit `amount_paid` 0, `amount_due` equal to the amount and
only failed payments. That appends `ResumeConfirmed` (migration
`20261117140000_PaymentResumeConfirmation`, payment-event insertion sequence);
later uncertainty is judged after the latest confirmation. A paid order with no
captured payment, an authorization, an in-flight payment or absent totals keep
the attempt pending without a launcher. The web draft reopens a saved pending
attempt only for such a recheck sent after that pending state was saved.

Methods: `Payments:Razorpay:Methods` must be listed explicitly (no default; a
default array would merge with a narrower configured list), without duplicates,
from `card`, `netbanking`, `wallet`, `upi`. The launcher receives the pinned list
as `methods` and opens Razorpay's documented display configuration with one
block of exactly those methods and `show_default_blocks: false`; the server still
refuses to fulfil any other method. Provider requests use only
`https://api.razorpay.com/` with redirects disabled; `MaxAmountPaise` is the
account's per-order maximum and must be configured. Dashboard-side method
enablement and the saved checkout configuration remain a human gate.
