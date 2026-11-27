# P01-09 provider sign-in evidence

The red regression was run on `ab90178f67c24efa61b62b4f0629f8492313f404`
(merged P01-08) with only `P0109Tests.cs` added. The test created an isolated
PostgreSQL database, completed owner password and authenticator enrollment, and
requested `GET /api/admin/auth/sign-in/context`. The assertion reported
expected 200, actual 404, and the test process exited 1. Reproduce that red
check with the original dashboard test and the targeted command below.

| Command | Observed result |
| --- | --- |
| `dotnet test tests/backend/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj --filter FullyQualifiedName~P0109Tests` | 10 passed. Fixed-clock sign-in and no-store, merchant JWT and admin key/issuer/audience isolation, live revocation and deactivation, access/idle/absolute expiry, origin and CSRF denial, account rate window, and concurrent TOTP consumption across two hosts against PostgreSQL. |
| `dotnet test tests/backend/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj` | 55 passed. Full licensing integration suite, including P01-06 bootstrap, P01-07 invitations and P01-08 enrollment. |
| `dotnet test tests/backend/shared/P0103ConsumerRegressionTests/Intelibill.Shared.P0103ConsumerRegressionTests.csproj` | 12 passed. |
| `dotnet build Intelibill.slnx -warnaserror` | Passed with zero warnings and errors. |
| `cd src/admin-frontend && bun run test --watch=false` | 8 passed. |
| `cd src/admin-frontend && bun test tests/server.test.mjs` | 13 passed. |
| `cd src/admin-frontend && bun run audit:release` | Production build and Chromium release test passed. |
| `cd src/frontend && bun run build` | Passed, including protected worker and offline pack packaging. Merchant size-budget warnings were emitted. |
| `bash tests/backend/licensing/acceptance/run-p0109-browser.sh` | One real Chromium scenario passed. The explicit migration job upgraded the P01-08 database and ran again without changing data. |
| `git diff --check` | Clean. |

The browser runner built the licensing API from source and the admin site in
production mode. A fresh Chromium context redirected from the dashboard to
sign-in. The bootstrapped owner enrolled, signed in with an unused TOTP step,
and reloaded the dashboard with the invitation capability. The invited and
enrolled second operator saw an empty grant list and received 403 when opening
the invitation route directly at a 390 px viewport. Both requests missing
CSRF proof and requests with a foreign origin returned 403. Chromium exposed no
credential to script or browser storage. The loopback flow exercised the
documented HTTP development mode. The HTTPS API integration test checked
Secure, HttpOnly and SameSite=Strict response cookies.

The migration job started from `20261114185900_AddAdminMfaEnrollment` with
an existing identity and pending credential. The source API showed readiness
while the database remained at P01-08 and had no session table. The migration
image built from `src/backend/licensing/Dockerfile.migrate` applied
`20261114194123_AddAdminSignInSessions` using the migrator role. It preserved
the prior rows and, on reapplication, both browser-created sessions. The
runner used local PostgreSQL and Mailpit with disposable credentials.

Policy choices in this slice include the global and normalized account budgets,
a dedicated signing key, and a 15-minute access cookie with server-side session
limits. See the licensing README for local key configuration and the later
Phase 1 session and capability slices.
