# P03-01 checkout review fixture

Use the shipping merchant build, the loopback merchant and licensing APIs, PostgreSQL, and the opt-in `P0301BrowserTests.BillingOwnerReviewsAndAcceptsFrozenFullPaymentQuote` test. The test publishes a local fixture plan and names four affiliated shops. It records sanitized Chromium observations at 1440 × 900 and 390 × 844 with the browser timezone set to America/Los_Angeles. Set `P0301_EVIDENCE_DIR` to an absolute path outside the product worktree.

Run the merchant build and the fixture from the repository root.

```sh
(cd src/frontend/intelibill && bun run build)
P0301_RUN_BROWSER=1 P0301_EVIDENCE_DIR=/absolute/path/to/evidence dotnet test tests/backend/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj --filter 'FullyQualifiedName~P0301BrowserTests.BillingOwnerReviewsAndAcceptsFrozenFullPaymentQuote' --logger 'console;verbosity=normal'
```

The review shows ₹270.00 for the four named shops and accepts only after explicit confirmation. After an unsent acceptance and a lookup that returns 404, the browser retries the exact request. It drops the committed response, switches billing accounts, then opens the original quote and recovers with the original operation key. The expiry run uses the exact server deadline and checks both the retained accepted quote and refusal of a separate unaccepted quote. Each Playwright phase must report one pass and zero skips. The evidence includes the merchant build ID, committed SHA, screenshots, operation and snapshot hashes, and PostgreSQL counts before and after checkout. Payment providers and fulfilment are outside this fixture.
