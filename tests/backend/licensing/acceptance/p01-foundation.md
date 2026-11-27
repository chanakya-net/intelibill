# Phase 1 foundation acceptance

Issue [#845](https://github.com/chanakya-net/intelibill/issues/845) closes the local foundation gate defined by [#823](https://github.com/chanakya-net/intelibill/issues/823).

**Local gate:** Verified with a clean Compose run and local consumer checks. **Live provider gate:** Unverified and unauthorized.

The implementation run started from `966ba4a33f18c52ad0999157bed7dd22293b6e9f`. It uses synthetic identities, a local PostgreSQL instance and Mailpit. The two web image builds use Bun 1.3.11. Host web verification was repeated with Bun 1.3.11 and the frozen lockfile. The host SDK was .NET 10.0.403, selected by the repository's `latestMinor` roll-forward.

## Reproduce the local gate

Use the repository's Bun and Playwright versions, Chromium installed for the admin workspace, and a local Docker Unix socket. The Compose runner also uses Bash, `jq`, `lsof` and OpenSSL. Run from the root of a clean checkout, with no untracked build inputs.

```bash
bun install --frozen-lockfile
(cd src/frontend/licensing && bun x playwright install chromium)

export P0115_PROJECT="intelibill-p01-05-845-$(date -u +%Y%m%d%H%M%S)"
export P0115_ARTIFACTS="$(mktemp -d /tmp/intelibill-p0115.XXXXXX)"
chmod 700 "$P0115_ARTIFACTS"
bash scripts/licensing/test-p01-foundation.sh --case all
bash scripts/licensing/test-p01-foundation.sh --case cleanup
```

Use a fresh project and evidence directory each run. The primary fixture uses merchant web 14000, admin web 14001, the APIs on 14277 and 14278, PostgreSQL on 14432 and Mailpit on 14025 and 14825, all on loopback. The P01-05 migration cases use distinct loopback ports. The cleanup command checks the evidence directory's project record and the Compose resource labels before removing the fixture volumes.

The runner first calls `scripts/dev/test-licensing-stack.sh --case all`. That calls guarded `docker compose --env-file /dev/null -f docker-compose.yml -p "$P0115_PROJECT" --profile full up -d --build`, plus the existing reused-volume, failure and merchant browser cases. The provider stages add two random roots through a private Compose override and issue the bootstrap proof through the one-shot CLI under the migration identity. The running API uses the licensing runtime role. The browser uses no fixture session. The provider stages launch Chromium from `src/frontend/licensing` with the following command and private fixture inputs.

```bash
NODE_PATH="$PWD/node_modules" bun x playwright test \
  --config ../../../tests/backend/licensing/browser/playwright.p0115.config.ts
```

The runner requires merchant Chromium results of two passes in each phase and one passing provider journey for each of `enroll` and `restart`. The provider restart uses `compose restart backend licensing-api`, then `compose down` and `compose up -d` with the original fixture volume and roots. It compares the identities, grants, accepted invitation, revoked sessions, audit count and ordered migration IDs across both restarts. The runner publishes `summary.txt` after all checks and a fixture credential scan. Proofs, cookies, tokens, private logs and browser traces stay in temporary scratch deleted when the runner exits.

## Observed red

The new owner UI test ran on the unconfigured P01-05 fixture on 2026-11-15 at 01:46:13 UTC. The fresh case and one-shot `bootstrap-admin prepare` succeeded. The test submitted the proof and password, reached the pending-MFA screen and clicked **Set up authenticator**. The first MFA setup request returned HTTP 503 `Mfa.KeyUnavailable` where the assertion required HTTP 200. Chromium reported one failure and exited 1.

The fixture project was `intelibill-p01-05-845-red`. The pre-change command was `P0105_PROJECT=intelibill-p01-05-845-red P0105_ARTIFACTS=<private-directory> bash scripts/dev/test-licensing-stack.sh --case fresh`, followed by the guarded one-shot prepare with the local migration login and the same Playwright command above with `--grep 'owner completes bootstrap'`. Evidence is in `verification/red/setup.log`, `verification/red/repro.sh`, `verification/RED.log` and `verification/RED-cleanup.log` under the implementation attempt directory. Red cleanup exited 0.

The first combined run also reproduced a stale P01-05 browser expectation. The unconfigured admin home stayed at `/` after its session read returned 401 and the refresh context returned 503. The P01-05 browser check now asserts the unconfirmed session, zero capability headings, the 503 no-store response and unchanged storage and request boundaries. P01-15 exercises the sign-in redirect after provider setup.

### Earlier verification attempts

| Log | UTC | Exit | Observation |
| --- | --- | --- | --- |
| `GREEN1.log`, `STACK-BROWSER-DEBUG.log` | 01:51:10Z, 01:56:37Z | 1, 1 | The old browser assertion expected a redirect on the unconfigured stack. The merchant enrollment test passed. |
| `PROVIDER-DEBUG.log` | 01:58:05Z | 1 | The exploratory Chromium check waited for the compatibility login route. The current merchant UI posts to `/api/auth/login`. |
| `GREEN.log` | 02:00:49Z | 1 | Merchant and provider enrollment completed. The restart snapshot then queried the wrong EF history column. The checked query uses `migration_id` in both databases. |
| First rebased Compose run | 06:13:03Z | 1 | The reused merchant image imported host `dist` with mode 0700. The root Docker context now excludes generated `dist` directories. |
| `L03.log` | 01:54:44Z | 1 | One existing P0107 test timed out waiting for the async invitation-accepted log. An isolated retry passed. A later full run hit the same timeout and its immediate full rerun passed. The new cross-host tests use the existing serial licensing collection. |

## Child evidence

The command IDs below identify current verification. The release browser matches the configured sign-in route. The anonymous shell described in [P01-04's historical browser scenarios](../../../contracts/licensing/p01-04-browser-scenarios.md) predates the current session guard.

| Child | Behavior checked | Commands | Current evidence | Limit |
| --- | --- | --- | --- | --- |
| P01-01 / #831 | Independent hosts, model and migration boundaries, restricted persistence | B01–B03, L02, G01 | `P0101Tests*` (17 tests) and `ProjectBoundaryAssertions`. Both solution and aggregate builds. | Local databases |
| P01-02 / #832 | Both APIs consume shared safe errors and redaction | B04 | 14 diagnostic and consumer tests | Local sinks and test canaries |
| P01-03 / #833 | Shared security consumption, credential/session authority isolation | B05, B09, L01, G01 | 12 shared consumer and 49 merchant auth, session and realtime integration tests | Fixture credentials |
| P01-04 / #834 | Shared Angular, dependency boundaries, independent production builds, login and worker behavior | W01–W08, I06 | 3 workspace assertions, both web suites and release audits, duplicate styles 27/27 | Local Chromium |
| P01-05 / #835 | Fresh and reused full stack, job and role ordering, cross-database denial, merchant RLS, restart and failing migration isolation | I01–I05, G01 | Existing guarded P01-05 Compose cases and merchant Playwright suite | Local Compose |
| P01-06 / #836 | One-use initial owner proof, enrollment-only identity, restart and preparation refusal | L02, G01 | `P0106Tests*` (10 tests) and real browser bootstrap plus CLI retry | Synthetic attestation |
| P01-07 / #837 | Authorized invitation, recipient-specific delivery, one-use acceptance and pending MFA | L02, G01 | `P0107Tests*` (13 tests) and the real Mailpit invitation path | Local mail |
| P01-08 / #838 | Authenticator and one-time recovery-code enrollment, encrypted/hashed storage and race | L02, G01 | `P0108Tests*` (5 tests) and browser verification and acknowledgement for both principals | Fixture authenticator |
| P01-09 / #839 | Password plus TOTP opens a capability-scoped session, wrong authority denied | L01, L02, G01 | `P0109Tests*` (10 tests), `P0115Tests` and the protected Console | Production TLS unverified |
| P01-10 / #840 | Refresh rotates a live browser cookie; PostgreSQL serializes reuse and concurrent refresh | L02, G01 | `P0110Tests*` (10 tests) and real-cookie rotation with CSRF/origin checks | Local origin |
| P01-11 / #841 | Device sign-out and revoked credentials, fixed session boundaries and races | L02, G01 | `P0111Tests*` (10 tests) and browser device sign-out across restart | Local device sessions |
| P01-12 / #842 | Reviewed and continuation recovery, retained evidence and revocation without MFA bypass | L02 | `P0112Tests*` (10 tests) with real PostgreSQL and injected time | Server proof. Recovery ownership requires human input. |
| P01-13 / #843 | Exact five-minute recent-MFA cutoff, last recoverable owner races and explicit grant editing | L01, L02, G01 | `P0113Tests*` (10 tests), `P0115Tests` and Chromium capability edit, denial and re-login | Local owner |
| P01-14 / #844 | Immutable audit, constrained role, UTC and cursor boundaries, allowed and denied access | L02, G01 | `P0114Tests*` (5 tests), TCP runtime mutation denial and real audit page | Provider audit fixture only |
| P01-15 / #845 | Combined rerun, fixed-clock authority tests and credential-safe evidence | L01, S01, G01, G02 | Two PostgreSQL regressions, shell guards and the full Chromium and Compose run passed. | Live gate unverified |

## Command log

Local checks ran from clean source `8f4b2e50ac77b83befd7fb2d6587798733942a18` in an isolated worktree. Times are UTC on 2026-11-15. The Compose commands, shell acceptance tests and web image contract ran from clean source `fc681bba76ce96ff0ca309f6da6781f4d3bca54e`, which adds the Docker context correction and isolates the shell test Git environment. The two Compose commands used the same unique project and private evidence directory. Private command logs and exit files stay outside the repository.

| ID | Started (UTC) | Command | Exit | Result |
| --- | --- | --- | --- | --- |
| B01 | 06:12:34 | `dotnet build src/backend/intelibill/Intelibill.slnx --configuration Release` | 0 | Merchant solution build |
| B02 | 06:12:43 | `dotnet build src/backend/licensing/Intelibill.Licensing.slnx --configuration Release` | 0 | Licensing solution build |
| B03 | 06:12:49 | `dotnet build Intelibill.slnx --configuration Release` | 0 | Aggregate solution build |
| B04 | 06:14:19 | `dotnet test tests/backend/shared/P0102ConsumerRegressionTests/Intelibill.Shared.P0102ConsumerRegressionTests.csproj --configuration Release` | 0 | Shared diagnostics consumer tests (14 passed) |
| B05 | 06:14:34 | `dotnet test tests/backend/shared/P0103ConsumerRegressionTests/Intelibill.Shared.P0103ConsumerRegressionTests.csproj --configuration Release` | 0 | Shared security consumer tests (12 passed) |
| B06 | 06:14:56 | `dotnet test tests/backend/intelibill/unit/Intelibill.Domain.Unit.Tests --configuration Release` | 0 | Merchant domain unit tests (468 passed) |
| B07 | 06:14:58 | `dotnet test tests/backend/intelibill/unit/Intelibill.Application.Unit.Tests --configuration Release` | 0 | Merchant application unit tests (1802 passed) |
| B08 | 06:15:01 | `dotnet test tests/backend/intelibill/unit/Intelibill.Api.Unit.Tests --configuration Release` | 0 | Merchant API unit tests (588 passed) |
| B09 | 06:15:07 | `dotnet test tests/backend/intelibill/integration/Intelibill.Integration.Tests --configuration Release --filter 'FullyQualifiedName~AuthControllerTests\|FullyQualifiedName~SessionLifecycleIntegrationTests\|FullyQualifiedName~RealtimeAuthorizationIntegrationTests'` | 0 | Merchant PostgreSQL auth, session and realtime tests (49 passed) |
| L01 | 06:12:52 | `dotnet test tests/backend/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj --configuration Release --filter FullyQualifiedName~P0115Tests` | 0 | P0115 PostgreSQL authority and fixed-clock regressions (2 passed) |
| L02 | 06:13:14 | `dotnet test tests/backend/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj --configuration Release` | 0 | Full licensing PostgreSQL integration suite (102 passed) |
| I01 | 06:12:35 | `bash .github/scripts/tests/licensing-stack-contract.test.sh` | 0 | 11-service Compose contract |
| I02 | 06:12:35 | `bash .github/scripts/tests/licensing-stack-harness.test.sh` | 0 | Compose lifecycle and ownership guards |
| I03 | 06:22:20 | `bash .github/scripts/tests/frontend-image-contract.test.sh` | 0 | Merchant and admin image contracts |
| I04 | 06:17:41 | `bash .github/scripts/tests/migration-image-contract.test.sh` | 0 | Merchant and licensing migration image contracts |
| I05 | 06:12:38 | `bash .github/scripts/tests/validate-phase-11-workflows.test.sh` | 0 | Release workflow contracts |
| I06 | 06:12:38 | `node --test src/frontend/licensing/tests/server.test.mjs` | 0 | Admin runtime server tests |
| S01 | 06:22:20 | `bash tests/backend/licensing/acceptance/p01-foundation-harness.test.sh` | 0 | Exit propagation, safe evidence, scope and source provenance |
| W00 | 06:12:34 | `bun install --frozen-lockfile` | 0 | Frozen workspace install with Bun 1.3.11 |
| W01 | 06:12:35 | `(cd src/frontend/intelibill && bun run test --run scripts/tests/web-workspace.spec.ts)` | 0 | Web workspace boundary tests (3 passed) |
| W02 | 06:12:35 | `(cd src/frontend/intelibill && bun run test --watch=false)` | 0 | Merchant test suites (2998 Angular and 107 Bun tests passed) |
| W03 | 06:13:09 | `(cd src/frontend/intelibill && bun run build)` | 0 | Merchant production build and worker packaging |
| W04 | 06:13:21 | `(cd src/frontend/intelibill && bun run audit:styles:duplicates)` | 0 | 27 duplicate style blocks against a ceiling of 27 |
| W05 | 06:13:57 | `(cd src/frontend/intelibill && bun run audit:ui:release)` | 0 | Merchant production Chromium release audit (78 passed) |
| W06 | 06:13:21 | `(cd src/frontend/licensing && bun run test --watch=false)` | 0 | Admin Angular unit tests (48 passed) |
| W07 | 06:13:25 | `(cd src/frontend/licensing && bun run build)` | 0 | Admin production build |
| W08 | 06:13:28 | `(cd src/frontend/licensing && bun run audit:release)` | 0 | Admin production Chromium release audit (1 passed) |
| G01 | 06:22:00 | `bash scripts/licensing/test-p01-foundation.sh --case all` | 0 | 4 merchant and 2 provider Chromium passes, database isolation, startup jobs, persistence and credential scan |
| G02 | 06:29:28 | `bash scripts/licensing/test-p01-foundation.sh --case cleanup` | 0 | Label-checked fixture cleanup |

In Chromium, the merchant enrolled through Mailpit and signed back into the same shop after Compose recreation. The owner completed provider bootstrap, TOTP and recovery-code acknowledgement, then invited a second administrator at 390px. A new sign-in reflected the delegated Support grant and revoked access and refresh credentials stayed invalid across restart. The provider audit UI showed the grant change and the audit API returned denied reads. Both runtime database roles remained restricted.

## Live prerequisites

- Human verification of the initial owner's identity and verified email attestation remains outstanding.
- Custody, backup and restore, rotation, and access to the MFA encryption, session-signing and Data Protection keys remain unverified.
- Recovery ownership and the out-of-band proof procedure require human verification.
- Production admin hostname, exact origin, TLS, Secure-cookie behavior, deployment secrets, service identities, grants and reviewed rollout remain unverified.

Production access and every commercial flow remain outside this local acceptance gate.
