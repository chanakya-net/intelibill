# P01-06 local fixture acceptance

Issue #836. The fixture email and attestation are synthetic. Evidence ends
at pending enrollment. The test runs neither TOTP nor a business sign-in.

## Reproduce

From the repository root after `bun install --frozen-lockfile` and installing
the repository's Playwright Chromium. Choose an unused project name and private
scratch directory. The fixture scripts check Compose model and resource labels
and use the dedicated local port range 14000–14825.

```sh
export P0105_PROJECT=intelibill-p01-05-836-a
export P0105_ARTIFACTS="$(mktemp -d /tmp/p0106-836.XXXXXX)"
bash scripts/dev/test-licensing-stack.sh --case fresh
bash scripts/dev/test-licensing-stack.sh --case browser
bash tests/licensing/acceptance/run-p0106-browser.sh
bash scripts/dev/test-licensing-stack.sh --case cleanup
```

The fresh case runs the explicit migration job twice and exercises restricted
runtime DML and denied DDL. The P01-05 browser case uses the stack's Mailpit.
The P01-06 runner creates a one-shot CLI container using the fixture migrator.
It redirects the proof to a temporary mode-0600 file and removes it on exit.
The browser creates its password in memory, disables recordings, and captures
only the pending enrollment screen after clearing secrets.

## Behavior-first evidence

`dotnet test tests/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj --filter FullyQualifiedName~P0106Tests`

- Immutable baseline `3e7fe83e1d6c1335fa04dd635eeeb85749c9f913` with the
  behavior-first test applied. Exit 1, 1 failed. Production-mode setup context
  returned 404 where the test expected 200 and setup unavailable.
- The same scenario passed with the explicit migration and context. At the
  next slice boundary the same command exited 1 with 4 failed and 1 passed.
  Both racing POSTs and the exact-expiry cases returned 404. With consumption
  implemented, exit 0 and 5 passed.
- Enrollment first returned 404 after setup. Exit 1 with 1 failed and 5 passed,
  then exit 0 with 6 passed. The durable budget first allowed a sixth attempt
  after restarting the API. The stage command returned exit 1, then exit 0
  with 7 passing tests after the PostgreSQL budget.
- `cd src/admin-frontend && bun run test --watch=false` first exited 1
  with 1 failed and 1 passed. The setup URL redirected to the landing page.

## Release evidence

The P01-06 PostgreSQL suite passed 10 tests. The race ends with one 200
and one 409 across independent hosts, one pending owner and one success
audit event. Proof expiry was checked one second before, exactly at,
and one second after the boundary with a fixed clock. A separate test
observed the transaction waiting on the singleton in `pg_stat_activity`
and advanced the clock before releasing the row. An injected audit constraint
rolled back the owner, credential and proof consumption while the rate count
survived. A restarted API enforced the durable budget with 429 and
`Retry-After: 900`. The suite verified runtime audit and tombstone mutation
refusals. The same valid credential received 200 from the enrollment endpoint
and 403 from the production business policy at `/api/admin/access`.
Submitting the proof as a cookie or the enrollment credential as a bearer
received 401. No MFA state was synthesized.

The initial current-source P01-05 fresh and browser runs ended with exit 0.
All five jobs exited 0 on first and repeated startup. The only licensing
migration history entry was `20261114170140_InitialAdminIdentityAndAudit`.
The explicit job reported applying it on a fresh volume and subsequently
reported the schema up to date. The bootstrap table belonged to
`intelibill_licensing_migrator`. The restricted runtime could read and
write its own data, was denied schema changes and had zero logged DDL during
restarts and readiness probes. Both Chromium phases passed two tests each.
The merchant retained one user, shop and membership and its shop-only RLS.
The admin page issued no protected requests on landing and denied merchant
API forwarding.

The P01-06 Chromium scenario passed on a separately isolated PostgreSQL
project with the built admin and licensing images. It observed setup 200,
replay 409, business 403, pending enrollment after reload and API/web
restart, and recovery after clearing the enrollment cookie. The CLI returned
exit 2 when preparing again and emitted no proof. PostgreSQL evidence was
`1|1|1|1|1|1` for verified pending owner, consumed proof, active credential,
revoked credential, owner-created audit and resume audit, respectively.
The pending screen was checked at 390 pixels. The test observed empty
localStorage/sessionStorage and used no credential-bearing recording.

The first proxy check showed that the upstream Host differed from the prepared
browser authority. The test now verifies Host forwarding. The first Chromium
bootstrap received a duplicate `no-store` directive from the proxy and API.
The proxy now emits one directive. The server test sends literal request
targets through a socket because Bun's HTTP client normalized the existing
mixed-separator regression cases.
