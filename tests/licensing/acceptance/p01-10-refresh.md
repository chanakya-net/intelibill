# P01-10 provider session refresh evidence

The red regression ran on `b9e466e5482b456c8947e08fc0379e76b4384e49` (merged
P01-09) with only the `P0110Tests*.cs` files and their cookie-jar helper added.
Each test created an isolated PostgreSQL database through Testcontainers and
completed owner bootstrap, authenticator enrollment and a 200 sign-in, so the
prerequisites passed. All 10 tests then failed on missing refresh behavior:
sign-in set no refresh cookie (`Assert.NotNull() Failure: Value is null`) and
`POST /api/admin/auth/refresh` answered 404 where 200, 401 or 403 was expected.
The concurrent tests saw no 200 among the presentations.

| Command | Observed result |
| --- | --- |
| `dotnet test tests/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj --filter FullyQualifiedName~P0110Tests` | 10 passed. Rotation and digest-only storage, replay revoking the session for both holders, concurrent presentation across two hosts (one 200, one 401, no live session, two credentials) and six concurrent presentations (exactly one successor), unchanged MFA time with a fixed clock, a refresh at 12 h − 1 s accepted and the session refused at 12 h, idle refresh accepted at 29:59 and refused 30:00 later, revoked and deactivated sessions refused, origin/CSRF refusal before the credential is read, unknown/absent/oversized credentials refused without ending the session. |
| `dotnet test tests/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj` | 65 passed. |
| `dotnet test tests/shared/P0103ConsumerRegressionTests/Intelibill.Shared.P0103ConsumerRegressionTests.csproj` | 12 passed. |
| `dotnet build Intelibill.slnx -warnaserror` | Passed with zero warnings and errors. |
| `cd src/admin-frontend && bun run test --watch=false` | 15 passed. |
| `cd src/admin-frontend && bun test tests/server.test.mjs` | 13 passed. |
| `cd src/admin-frontend && bun run audit:release` | Production build and Chromium release test passed. The static release server answers the API with 404, so no renewal is attempted. |
| `cd src/frontend && bun run build` | Passed. |
| `bash tests/licensing/acceptance/run-p0110-browser.sh` | One Chromium scenario passed; the migration job upgraded P01-09 and a second run changed nothing. |
| `git diff --check` | Clean. |

The browser runner built the licensing API from source and the admin site in
production mode, against disposable PostgreSQL. The owner bootstrapped,
enrolled and signed in; the refresh cookie was HttpOnly and SameSite=Strict on
the documented loopback path. With three tabs open, the access cookie was
removed and all tabs reloaded together. Each tab renewed under the Web Lock:
three refreshes, all 200, never more than one in flight, and every tab showed
its dashboard. A refresh aborted before it reached the API left the tab on the
dashboard route with grants hidden and a retry notice, without another refresh
attempt; the operator's retry renewed it. A refresh the API completed while its
answer was lost (cookies restored to their earlier state) also showed the
retry notice. The retry presented the consumed credential, got 401, and showed
"Your provider session ended." The other two tabs then went to sign-in and the
session API answered 401 with `no-store`. Script-visible cookies, Web Storage,
IndexedDB and Cache Storage stayed empty.

Migration facts checked by the runner: the database started at
`20261114194123_AddAdminSignInSessions` with a prior enrolled identity, its
P01-09 session and a sign-in audit row. The source API was ready while
`admin_refresh_credentials` did not exist. The image built from
`src/licensing/Dockerfile.migrate` applied
`20261114202808_AddAdminRefreshCredentials` with the migrator role. The prior
identity, session and audit rows were unchanged (md5). The new table had four
unique indexes (primary key, digest, parent, session root), three check and two
foreign-key constraints, and runtime INSERT/UPDATE/SELECT privilege. The audit
check became `action >= 1 AND action <= 16` and no credential was backfilled.
After the browser run there were 2 sessions (1 revoked by reuse), 6 chained
credentials (1 unconsumed, 1 root) and 1 reuse audit event. Re-running the job
left the history at 5 migrations, with predecessor facts, schema facts and the
refresh chain digest unchanged.

These are local fixtures only. No live provider or deployed environment was
verified.
