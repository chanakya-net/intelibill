# P01-08 authenticator enrollment acceptance

All observations use disposable local fixtures. The browser run uses the
production admin bundle and its Node proxy, the licensing API with the
restricted runtime login, PostgreSQL 17 and local Mailpit. Trace, screenshots
and video are off. The runner generates credentials at runtime and removes the
temporary bootstrap proof, invitation fixture session and CSRF key ring.

## Behavior-first baseline

On `355468b1688e037d5d45ed20f043644c0a53134c`, with only the first P0108
test added, run from the issue worktree:

```sh
dotnet test tests/backend/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj --filter FullyQualifiedName~P0108Tests
```

Exit 1, one assertion failed. The fixed-clock, PostgreSQL-backed
`PasswordProof_OpensAuthenticatorSetupButCannotEnterBusinessApis` test
completed password setup and proved that the pending cookie received 403 from
provider access and 401 from the invitation API. It then received HTTP 404 from
the required MFA context, so a verified password holder could not start
authenticator setup. The implementation makes the same test pass. Additional
regressions exercised concurrent completion, one-use proof consumption in
`IAdminMfaVerifier`, the five-attempt limit across independently hosted
replicas and replacement credentials, the reset boundary and key rotation.

## Browser and migration

```sh
bun install --frozen-lockfile
tests/backend/licensing/acceptance/run-p0108-browser.sh
```

Exit 0, one Chromium scenario passed. The initial owner followed
`/auth/bootstrap` → `/auth/bootstrap/enrollment` →
`/auth/mfa-enrollment`. The invited operator followed
`/auth/invitation` → `/auth/mfa-enrollment`; the invitation fragment was
cleared. The runner used the existing guarded Development session fixture to
send the invitation after the owner had completed real MFA. The invitee used a
fresh browser context.

- Both operators saw the manual setup key. Pending reload kept the same key.
  `GET /api/admin/access` answered 403 while enrollment was pending.
  An incorrect authenticator code displayed an error. A valid authenticator
  code replaced the setup key with exactly ten recovery codes.
- After completion, the enrollment cookie was retired and the verification
  replay, enrollment context and business probe answered 401. Reload and a
  direct return to `/auth/mfa-enrollment` displayed neither setup key nor
  recovery codes. Local storage, session storage, IndexedDB, Cache Storage
  and script-readable cookies were empty. The API diagnostic scan found zero
  fixture credential matches.
- The fixture ran the image built from `src/backend/licensing/Dockerfile.migrate`
  first at `20261114181011_AddAdminInvitations`, then at
  `20261114185900_AddAdminMfaEnrollment`. The API answered readiness 200 on
  the predecessor without producing migration history or MFA tables. The
  migration job preserved the predecessor's pending grant, and a repeated job
  preserved the encrypted records, recovery digests, enrollment evidence and
  audit rows. Fresh installs ran in the PostgreSQL integration suite.
- PostgreSQL counted two MFA-enrolled admins, two encrypted TOTP records
  carrying consumed counters, 20 recovery digests, two retired enrollment
  grants, two completion audits, two rejection audits and one accepted
  invitation. The integration suite independently authenticated the ciphertext
  under its TOTP purpose, verified that the recovery key could not decrypt it,
  and matched the recovery digest with its separate HMAC key. A restarted host
  lacking the stored key returned 503; retaining the old key worked after
  changing the active ID.

Sign-in and business session issuance belong to #839. Refresh and audited
recovery routes belong to #840 and #842.

## Verification

| Command | Result |
| --- | --- |
| `dotnet test tests/backend/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj --filter FullyQualifiedName~P0108Tests` | 5 passed |
| `dotnet test tests/backend/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj` | 45 passed |
| `dotnet test tests/backend/shared/P0102ConsumerRegressionTests/Intelibill.Shared.P0102ConsumerRegressionTests.csproj` | 14 passed |
| `dotnet test tests/backend/shared/P0103ConsumerRegressionTests/Intelibill.Shared.P0103ConsumerRegressionTests.csproj` | 12 passed |
| `dotnet build Intelibill.slnx -warnaserror` | 0 warnings, 0 errors |
| `cd src/admin-frontend && bun run test --watch=false` | 7 passed |
| `cd src/admin-frontend && bun test tests/server.test.mjs` | 13 passed |
| `cd src/admin-frontend && bun run audit:release` | Build and Chromium smoke test passed |
| `cd src/frontend && bun run build` | Build and worker packaging passed with existing size-budget warnings |
| `git diff --check` | Passed |

TOTP uses the [RFC 6238](https://www.rfc-editor.org/rfc/rfc6238.html)
counter and the [RFC 4226](https://www.rfc-editor.org/rfc/rfc4226.html)
authenticator-compatible truncation.
