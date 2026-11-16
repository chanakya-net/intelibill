# P03-03 checkout recovery

Run from the repository root with Docker and the repository's Playwright
Chromium installed. Build the shipping merchant application before running the
browser fixture.

```sh
(cd src/frontend && bun run build)
export P0303_EVIDENCE_DIR=/absolute/path/to/issue-local/evidence/p0303
mkdir -p "$P0303_EVIDENCE_DIR"
dotnet test tests/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj --filter 'FullyQualifiedName~P0303Tests'
P0303_RUN_BROWSER=1 dotnet test tests/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj --filter 'FullyQualifiedName~P0303BrowserTests.BillingOwnerRecoversUncertainCheckoutWithoutNewCharge' --logger 'console;verbosity=normal'
```

The browser fixture uses the shipping merchant bundle, two loopback Kestrel
APIs and separate PostgreSQL databases. The first POST commits before the
browser drops its response. A subsequent GET gets one injected 404. The test
opens a second tab in the same browser context, exercises the real simulator
timeout and checks the pending operation while navigating between two billing
accounts. The simulator then schedules capture on the original session. The
leased worker observes it at the fixed-clock boundary. Applying becomes
Applied after signed merchant acknowledgement. The second order demonstrates
a terminal simulator failure.

The issue-local evidence directory contains `red.log`, the PostgreSQL
`worker-restart-and-races.json`, the per-phase Playwright logs and screenshots,
and `build-and-postgres.json` with the build ID and fixture counts. This
scenario uses local simulator funds.
