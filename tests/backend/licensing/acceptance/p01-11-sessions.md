# P01-11 administrator device sessions

The Provider Console links every signed-in administrator to **Device sessions**. The page lists the administrator’s active sessions and marks the current one. Recorded sign-in, last activity and expiry times are displayed in UTC. Revoking another device requires a reason and authenticator verification less than five minutes old. A normal sign-in supplies fresh verification. The existing owner capabilities confer no authority over another administrator’s sessions.

Revocation and its versioned audit event commit together while holding the same administrator and session locks used by refresh rotation. The session tombstone denies every credential in its refresh chain. The audit history identifies the affected session and its before and after PostgreSQL row versions. The protected list returns five device fields and a paging cursor, and records an inspection before returning results.

Signing out clears the initiating tab and same-browser tabs as soon as the user acts. The browser retains a generation-bound sign-out intent across reloads and serializes cookie writes using the existing Web Lock. If delivery is uncertain, the sign-in page offers **Retry sign-out**. A delayed response cannot restore hidden grants. The API can end the family with a refresh cookie after access expires, and can end a legacy session using a still-valid access cookie. A remote device receives the denial on its next protected request or refresh.

## Verification

Run from the repository root unless the command changes directories. All API and browser checks used disposable local fixtures.

| Command | Result |
| --- | --- |
| `dotnet test tests/backend/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj --filter FullyQualifiedName~P0111Tests` on baseline with the three regression tests | Expected exit 1. Real PostgreSQL setup, owner/invitee authenticator sign-in and prerequisite refresh succeeded. List expected 200 and received 404, revoke and sign-out expected 204 and received 404. |
| `dotnet test tests/backend/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj --filter FullyQualifiedName~P0111Tests` | 10 passed. Includes fixed MFA/idle/absolute boundaries, access and refresh survival/denial, scope and CSRF, and rollback after an audit insert failure. A PostgreSQL row lock held both API replicas at the same time. Either rotation outcome left the revoked target unusable and the sibling live. |
| `dotnet test tests/backend/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj` | 80 passed. Includes the original refresh and audit suites and EF snapshot check. |
| `dotnet test tests/backend/shared/P0103ConsumerRegressionTests/Intelibill.Shared.P0103ConsumerRegressionTests.csproj` | 12 passed. |
| `dotnet build Intelibill.slnx -warnaserror` | Built with zero warnings and errors. |
| `cd src/frontend/licensing && bun run test --watch=false` | 40 passed across eight files. Includes delayed 200 and 401 refreshes, stale protected results, abandoned sign-out intent, queued sign-in and unavailable Web Locks. |
| `cd src/frontend/licensing && bun run audit:release` | Production build and release Chromium check passed. |
| `cd src/frontend/intelibill && bun run build` | Merchant production build and worker packaging passed. The build emitted existing style-budget warnings. |
| `bash tests/backend/licensing/acceptance/run-p0111-browser.sh` | Chromium scenario and explicit migration job checks passed. |
| `git diff --check cbda75bd0bdf57b17fa9c704d8307b821b8a4194 HEAD` | Exit 0. |

## Chromium demo

1. The runner built the licensing source and production admin web. It created disposable PostgreSQL and Mailpit instances, bootstrapped a fixture owner and enrolled an invited administrator with no business capabilities.
2. The invited administrator signed in on three separate Chromium contexts. The 1280 px list and 390 px confirmation showed the current marker and UTC timestamps. Invalid CSRF and origin requests returned 403. Foreign and missing revoke targets returned the same 404, including when the bootstrap owner tried to revoke the invited administrator’s device. The response was `no-store`. A confirmed revocation gave the target 401 on access and refresh. The unrelated context refreshed successfully.
3. Two existing same-browser tabs cleared their protected views as soon as **Sign out this device** was clicked, while a third tab held a source refresh response. The delayed list response did not restore content. The sign-out POST waited for the refresh writer, then committed while the browser was still showing **Signing out…**. The test withheld that response and confirmed 401 from saved access and refresh cookies. After reload, a separate tab retried the sign-out and both tabs showed **Signed out**. A fresh sign-in created a different session that could access the API.
4. The surviving device signed out with its access cookie removed to exercise the refresh-only browser path. Its saved access and refresh cookies both returned 401. The fixed-clock integration test separately advanced access to its exact expiry. The owner inspected the target’s versioned revocation evidence.

The runner built and executed `src/backend/licensing/Dockerfile.migrate` on a fresh database and on a database at `20261114221354_AddProviderAuditInspection` with seeded identity, session, refresh and audit rows. Source API readiness left the predecessor schema unchanged. The job applied `20261114225609_AddAdminSessionRevocationAudit`, preserved the seed digest and reused PostgreSQL’s existing system `xmin` column. A staged runtime audit INSERT held the table fence until commit. Down then refused with SQLSTATE 55000 and preserved the audit row and migration history. Runtime INSERT succeeded and UPDATE, DELETE and TRUNCATE were refused. Reapply changed neither the migration history nor the audit digest.

Apply the licensing migration job before new API writers. Keep the migrated schema and audit history when rolling back application binaries.
