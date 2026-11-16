# P03-02 simulator purchase

Use the shipping merchant build, the loopback merchant and licensing APIs (each on its own PostgreSQL database), the local payment simulator (`Payments:Mode = LocalSimulator`, Testing host) and the signed entitlement transport pumped by the fixture. Set `P0302_EVIDENCE_DIR` to an absolute path outside the product worktree.

```sh
(cd src/frontend && bun run build)
P0302_RUN_BROWSER=1 P0302_EVIDENCE_DIR=/absolute/path/to/evidence dotnet test tests/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj --filter 'FullyQualifiedName~P0302BrowserTests.BillingOwnerCompletesSimulatorPurchaseAndSeesAccessApplied' --logger 'console;verbosity=normal'
```

The fixture affiliates three shops through the real consent flow, publishes a local plan, and creates and accepts the full-payment quote through the merchant API. Chromium then runs three phases, each of which must report one pass and zero skips:

1. `@pending`: the accepted quote shows the full amount and all named shops; "Continue to payment" starts one attempt (201) and shows the development-only simulator. "Return without capturing" sends only the browser's success claim; the page reads pending verification, also after reload. PostgreSQL has no verified payment and no paid term.
2. `@capture`: the simulated provider captures out of band. "Check again" asks the server, which verifies the captured funds and fulfils the order: payment confirmed and "Applying access to 0 of 3 shops" at 1440 × 900 and 390 × 844. A repeated server verification leaves exactly one payment, fulfilment, paid term and three shop links.
3. `@applied`: after the fixture delivers the snapshots and the merchant's signed acknowledgements, the page reads "Access applied to all 3 shops".

Evidence holds per-phase logs, observations, screenshots and `build-and-postgres.json` (committed SHA, merchant build ID, row counts). This proves simulator behaviour only; no real payment provider, credential or webhook is exercised, and production gating is covered by the integration tests, not by this run.
