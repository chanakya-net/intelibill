# P01-07 local fixture acceptance

Issue #837. Every identity in this document is synthetic: `fixture.owner@example.test`
and random `invitee.*@example.test` addresses. Mail went only to a local Mailpit.
Nothing in this document verifies a live provider.

No production provider session exists on this base, because TOTP sign-in belongs to
P01-08 and P01-09. Issuance therefore fails closed with 401 by default. The inviting
owner below is a **fixture**. Its session is the `AdminFixtures` cookie, which only
Development and Testing accept. Its completed MFA is one labeled `UPDATE` of
`mfa_enrolled_at`. Neither fixture bypasses the persisted owner and recent-MFA checks.
The invited administrator's MFA is never synthesized.

## Reproduce

From the repository root after `bun install --frozen-lockfile` and installing the
repository's Playwright Chromium:

```sh
export P0105_PROJECT=intelibill-p01-05-837-a
export P0105_ARTIFACTS="$(mktemp -d /tmp/p0107-837.XXXXXX)"
bash scripts/dev/test-licensing-stack.sh --case fresh
bash tests/backend/licensing/acceptance/run-p0106-browser.sh
bash tests/backend/licensing/acceptance/run-p0107-browser.sh
bash scripts/dev/test-licensing-stack.sh --case cleanup
```

The P01-07 runner first requires the P01-06 owner and an empty invitation table.
It then synthesizes the owner's MFA and writes a random fixture secret to a
mode-0600 file. It passes only that secret's SHA-256 to Compose and recreates
`licensing-api`. After Playwright, it checks PostgreSQL and the service logs.
Finally, it recreates the API without the fixture and proves that issuance is
closed again. Recording is disabled. Passwords live only in browser memory.

## Behavior-first evidence

`dotnet test tests/backend/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj --filter "FullyQualifiedName~P0107Tests"`

- Immutable baseline `b158ba58c73a6e5b4b537b8711c46578c43c1b70` with the
  behavior-first tests applied, against real PostgreSQL and Mailpit containers:
  exit 1, `Failed: 12, Passed: 0`. Every invitation and acceptance route answered
  `404` where the tests expected `200`, `201` or `403`. A Production host
  configured with fixture sessions started anyway, so
  `Assert.NotNull() Failure: Value is null`.
- First implementation: exit 1, `Failed: 12`. Every fixture request answered
  401. The host read `AdminFixtures` before the test host added its
  configuration. Fixture options now bind lazily and are checked by
  `ValidateOnStart`.
- Next run: exit 1, `Failed: 2, Passed: 10`. Both failures were test errors. The
  first counted the owner's own bootstrap credential. The second expected a
  context read after MFA loss, which correctly answered 403. After fixing the
  tests, exit 0 and `Passed: 12`.
- The log assertion was initially vacuous because nothing logged. Adding
  identifier-only invitation logs exposed that the shared redactor masks a
  property named `Code`. That property was renamed `Outcome`, and the test
  now requires logged events and still finds no token, password or invitee.
- `BootstrapCommand_StillPreparesAfterInvitationServicesAreRegistered` came from
  the stack run below. The P01-06 runner's `bootstrap-admin prepare` exited 1
  with `Licensing host failed (AggregateException)`, because the CLI host
  validates every registered handler. The new test runs the real CLI process
  against isolated PostgreSQL. It failed the same way, exit 1, then passed
  after the invitation store and mailer joined bootstrap persistence.
- Admin web specs (`src/admin-frontend/src/app/features/admin-users/admin-users.spec.ts`)
  were written after the components. As a mutation check, removing the
  fragment-stripping `Location.replaceState` failed 5 of 7 tests
  (`expected '/auth/invitation#token=i1_AAAA…' to be '/auth/invitation'`).

## Release evidence (integration, real PostgreSQL + Mailpit, fixed clock)

- A recent-MFA owner gets 201 with an identifier and an expiry exactly 72 hours
  after the fixed clock, with `no-store` and no token. Mailpit receives exactly
  one message whose link is `https://localhost/auth/invitation#token=i1_…`.
  `admin_invitations` stores only the matching SHA-256 digest. The token does
  not appear in any serialized row.
- A fresh client on a restarted host accepts with the invited email and gets
  200 `enrollmentRequired` plus an HttpOnly, SameSite=Strict enrollment cookie.
  With that cookie, `GET /api/admin/auth/bootstrap/enrollment` answers 200,
  `/api/admin/access` answers 403 and `/api/admin/invitations/context` answers
  401. Replaying the link answers 409 with no cookie. PostgreSQL holds one
  verified administrator with `mfa_enrolled_at IS NULL` and
  `verification_reference = 'invitation:<id>'`, plus issued (7) and accepted (8)
  audit events. Captured Verbose logs include the acceptance and the
  `Invitation.Unavailable` refusal, but no token, token body, password or
  invitee address.
- Wrong identity: a forged token, a truncated token and a short password each
  answer 400. Five wrong emails answer 400 with no cookie. The link then has
  `failed_attempts = 5` and is retired, so the right email answers 409. There are
  5 refusal audits (9), no pending administrator and no invitee enrollment
  credential. Runtime `UPDATE` of the settled row fails with SQLSTATE 55000.
- Expiry at 72 hours with a fixed clock: 1 s before gives 200 and one
  administrator. Exactly at expiry and 1 s after both give 409 and none.
- Two hosts accept the same link concurrently. Exactly one gets 200 and one
  gets 409, only one response sets a cookie, and PostgreSQL holds one
  administrator and one invitee credential.
- A foreign `Origin` or a forged request token is refused with 403 before any
  budget or write. Acceptance still succeeds afterwards.
- Unauthorized issuance: a missing capability, an unknown actor, MFA exactly 5
  minutes old and MFA dated in the future all answer 403 with no invitation or
  mail. MFA 4:59 old answers 201; at 5:00 the POST and the context answer 403.
  Without a session, or with a wrong fixture cookie, the answer is 401. A
  foreign origin or forged CSRF gets 403. An MFA-pending invitee's cookie
  gets 401. A bearer token with forwarded-user and capability headers gets
  401. The owner without persisted MFA gets 403.
- A Production host with a configured fixture fails to start. A Production host
  with empty fixture values starts and answers 401.
- With an unreachable SMTP port, the API answers 503
  `Invitation.DeliveryUnavailable`, retires the invitation and records the
  undelivered audit (11).
- Re-inviting retires the previous link (409) and the current link enrolls.
  Re-inviting an enrolled address or the owner's address answers 409.

## Release evidence (isolated Compose stack, `intelibill-p01-05-837-a`)

- `test-licensing-stack.sh --case fresh` exited 0. All one-shot jobs exited 0 on
  first and repeated `up`. Merchant and licensing data survived `down` and `up`.
  Runtime DDL across restarts and readiness probes was 0.
- Explicit migration job:
  - On a scratch database inside the stack, the job applied
    `20261114170140_InitialAdminIdentityAndAudit` (the #836 schema). A second
    run upgraded it with `20261114181011_AddAdminInvitations`. A third run
    reported "No migrations were applied. The database is already up to date."
  - On `intelibill_licensing_dev`, both entries are present. Two further
    explicit `compose run licensing-migrate` runs exited 0 and were up to date.
  - `admin_invitations` is owned by `intelibill_licensing_migrator` and has 2
    triggers. The checks are `action BETWEEN 1 AND 11` and `id BETWEEN 1 AND 5`.
    The runtime login gets `must be owner of table admin_invitations` for
    `ALTER` and can read the table.
- The P01-06 runner passed after the command fix, with facts `1|1|1|1|1|1`.
- In the P01-07 runner, Chromium passed 1 test:
  - The owner fixture page `/admin-users/invite` sent the invitation (201,
    "Invitation sent").
  - Mailpit captured "Your InteliBill Provider Console invitation" from
    `provider-console@intelibill.local` to the synthetic invitee.
  - In a fresh browser context, the link opened at `/auth/invitation` with the
    fragment already removed. The invited email and a password were accepted.
  - The page showed "MFA setup required" and "No provider permissions" after a
    reload, at 390 px. `/api/admin/access` answered 403 `no-store`, the
    invitation context answered 401, and localStorage and sessionStorage were
    empty.
  - Replaying the link in another fresh context showed "expired or was already
    used".
- PostgreSQL facts were `1|1|1|1|1`: accepted invitation, pending invited
  administrator, enrollment credential, issued audit and accepted audit. API
  and admin-web logs had 0 matches for an invitation token or the invitee
  domain. After the fixture was removed, the invitation context answered 401.
  `--case cleanup` exited 0 and removed only the fixture projects.
