# P03-07 payment review acceptance

Sign in to the Provider Console with Billing and recent MFA. Open **Review payment funds**, enter the billing account ID, and inspect the original accepted order, named shops, expected and observed money, receiving pin, and linked evidence. Record a no-access decision, refer the funds for refund review, or recover the accepted terms when the case shows an eligible term. Supply a reason and evidence reference. Refund referral records an operator disposition for later review.

If a decision response is lost, reload the case and use **Check original decision** or **Retry original decision**. Both actions use the saved operation key. A lookup returning 404 leaves the saved decision available to retry.

The fixture captures a renewal after a scheduled cancellation changed its source revision. The merchant sees the original payment in review and can check its status. After the cancellation is withdrawn, finance can recover the original payment against the complete accepted package. The merchant then sees Applying until the signed merchant acknowledgements arrive, followed by Applied.

## Verification

Run from the repository root with Docker available and install frontend dependencies using Bun.

```sh
dotnet test tests/backend/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj --filter 'FullyQualifiedName~P0307Tests'
(cd src/admin-frontend && bun run build)
(cd src/frontend && bun run build)
P0307_RUN_BROWSER=1 P0307_EVIDENCE_DIR=/absolute/local/evidence dotnet test tests/backend/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj --filter 'FullyQualifiedName~P0307BrowserTests' --logger 'console;verbosity=normal'
```

The Chromium harness uses the built admin and merchant apps, local API processes, separate PostgreSQL databases, and the development simulator. It drops one successful browser response and simulates an unresolved operation lookup. The attempted recovery and replay reach the real application and database. Razorpay adapter tests use HTTP fixtures.

## Rollback

Once review evidence exists, roll back the readers and writers while retaining the financial tables and recorded decisions.
