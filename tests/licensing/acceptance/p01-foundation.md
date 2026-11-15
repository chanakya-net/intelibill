# Phase 1 foundation acceptance

Issue [#845](https://github.com/chanakya-net/intelibill/issues/845) closes the local foundation gate defined by [#823](https://github.com/chanakya-net/intelibill/issues/823).

**Local gate:** Full Compose acceptance in progress. **Live provider gate:** Unverified and unauthorized.

The implementation run started from `966ba4a33f18c52ad0999157bed7dd22293b6e9f`. It uses synthetic identities, a local PostgreSQL instance and Mailpit. The two web image builds use Bun 1.3.11. Host web verification was repeated with Bun 1.3.11 and the frozen lockfile. The host SDK was .NET 10.0.403, selected by the repository's `latestMinor` roll-forward.

## Reproduce the local gate

Use the repository's Bun and Playwright versions, Chromium installed for the admin workspace, and a local Docker Unix socket. The Compose runner also uses Bash, `jq`, `lsof` and OpenSSL. Run from the repository root.

```bash
bun install --frozen-lockfile
(cd src/admin-frontend && bun x playwright install chromium)

export P0115_PROJECT="intelibill-p01-05-845-$(date -u +%Y%m%d%H%M%S)"
export P0115_ARTIFACTS="$(mktemp -d /tmp/intelibill-p0115.XXXXXX)"
chmod 700 "$P0115_ARTIFACTS"
bash scripts/licensing/test-p01-foundation.sh --case all
bash scripts/licensing/test-p01-foundation.sh --case cleanup
```

Use a fresh project and evidence directory each run. The primary fixture uses merchant web 14000, admin web 14001, the APIs on 14277 and 14278, PostgreSQL on 14432 and Mailpit on 14025 and 14825, all on loopback. The P01-05 migration cases use distinct loopback ports. The cleanup command checks the evidence directory's project record and the Compose resource labels before removing the fixture volumes.

The runner first calls `scripts/dev/test-licensing-stack.sh --case all`. That calls guarded `docker compose --env-file /dev/null -f docker-compose.yml -p "$P0115_PROJECT" --profile full up -d --build`, plus the existing reused-volume, failure and merchant browser cases. The provider stages add two random roots through a private Compose override and issue the bootstrap proof through the one-shot CLI under the migration identity. The running API uses the licensing runtime role. The browser uses no fixture session. The provider stages launch Chromium from `src/admin-frontend` with the following command and private fixture inputs.

```bash
NODE_PATH="$PWD/node_modules" bun x playwright test \
  --config ../../tests/licensing/browser/playwright.p0115.config.ts
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
| `L03.log` | 01:54:44Z | 1 | One existing P0107 test timed out waiting for the async invitation-accepted log. The isolated retry passed and both final full runs passed. The new cross-host tests use the existing serial licensing collection. |

## Child evidence

The command IDs below identify current verification. The release browser matches the configured sign-in route. The anonymous shell described in [P01-04's historical browser scenarios](../../contracts/licensing/p01-04-browser-scenarios.md) predates the current session guard.

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
| P01-15 / #845 | Combined rerun, fixed-clock authority tests and credential-safe evidence | L01, S01, G01, G02 | Two new integration tests and shell guards. Combined run pending. | Live gate unverified |

## Command log

All entries run in the issue worktree unless a subshell names a consumer. Times are command start in UTC on 2026-11-15. The attempt's `verification/` directory retains command, exit, applicability, time and logs. Listed checks are applicable. The expected red is an intentional nonzero assertion, and failed retries are recorded separately.

<!-- Command results generated from the verification ledger. -->

## Live prerequisites

- Human verification of the initial owner's identity and verified email attestation remains outstanding.
- Custody, backup and restore, rotation, and access to the MFA encryption, session-signing and Data Protection keys remain unverified.
- Recovery ownership and the out-of-band proof procedure require human verification.
- Production admin hostname, exact origin, TLS, Secure-cookie behavior, deployment secrets, service identities, grants and reviewed rollout remain unverified.

Production access and every commercial flow remain outside this local acceptance gate.
