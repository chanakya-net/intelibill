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
