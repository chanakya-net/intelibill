# P01-10 provider session refresh evidence

The red regression ran before implementation and again during review fixes on `b9e466e5482b456c8947e08fc0379e76b4384e49` (merged
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
| `cd src/admin-frontend && bun run test --watch=false` | 29 passed. The invitation and session regressions cover stale responses after same-tab and cross-tab refusals, confirmed grants after fresh sign-in, hidden grants, discarded form/proof, explicit recovery, retried 401, coordination failures and recovery from an abandoned sign-in marker. |
| `cd src/admin-frontend && bun test tests/server.test.mjs` | 13 passed. |
| `cd src/admin-frontend && bun run audit:release` | Production build and Chromium release test passed. The static release server answers the API with 404, so no renewal is attempted. |
| `cd src/frontend && bun run build` | Passed. |
| `bash tests/licensing/acceptance/run-p0110-browser.sh` | One Chromium scenario passed. The explicit migration job upgraded P01-09, refused Down with a concurrent staged insert, and reapplied without changes. |
| `git diff --check` | Clean. |

The browser runner built the licensing API from source and served the
production admin site against disposable PostgreSQL. The owner bootstrapped,
enrolled and signed in. The loopback refresh cookie was HttpOnly and
SameSite=Strict. Three tabs lost the access cookie and reloaded together.
All returned to the dashboard and successful refreshes never overlapped.

An aborted refresh hid dashboard grants and offered Try again. An explicit
retry renewed the session. When the API committed a rotation and the browser
lost its response, a second tab had already received 401 and was waiting for
the lock. Both tabs showed the retry notice and sent exactly one POST between
them before explicit recovery. The explicit retry got 401, both tabs hid their
grants, and the session API refused access with `Cache-Control: no-store`.
Browser storage held only a generated coordination ID and its state.
A third tab received a session 200 that the API had authorized before the
refusal. It redirected to sign-in without showing grants or sending another
refresh POST.
Two concurrent tabs with Web Locks absent, and two with lock acquisition
rejected, hid grants, offered Try again and sent zero refresh POSTs.

Fresh sign-in waited behind both a held refresh 200 and a held refresh 401.
The new cookie pair survived both interleavings and a protected session check
succeeded. A third tab received its old protected 401 after the sign-in and
returned to sign-in without a refresh POST under the new cookie pair.

A fourth tab started a sign-in whose POST was held and then closed with the
coordination record still `replacing`. Another tab reloaded the dashboard and
stayed on the unconfirmed notice without sending a request. Try again waited
for the lock, confirmed the live session and restored the dashboard with zero
refresh POSTs, leaving the record `uncertain` so automatic rotation stays paused.

The database started at `20261114194123_AddAdminSignInSessions` with a prior
enrolled identity, session and sign-in audit row. The source API was ready
before the explicit migration job applied
`20261114202808_AddAdminRefreshCredentials`. The predecessor rows kept the
same digest. The credential table had four unique indexes, three check
constraints, two foreign keys and runtime INSERT/UPDATE/SELECT privileges.
The audit constraint accepted actions 1 through 16 and the upgrade created no
credential rows.

The runner held a staged credential insert open while the predecessor
migration job requested an exclusive lock. After the insert committed, Down
refused with SQLSTATE 55000 and preserved the row, schema and migration history.
With the old Down implementation, the same check reproduced table removal.
After the browser scenario, the explicit migration job kept the five history
rows, predecessor facts, schema and credential chain digest unchanged.

Verification used local fixtures. Live provider behavior remains unverified.
