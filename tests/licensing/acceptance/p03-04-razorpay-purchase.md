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
Those need the human-supplied test account described in the P03 PRD. Razorpay
payments expose only `created_at`; the adapter uses it as the effective payment
time, which equals the capture time for auto-captured payments.
