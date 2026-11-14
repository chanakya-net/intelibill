# InteliBill Licensing API

Build the licensing solution from the repository root with
`dotnet build src/licensing/Intelibill.Licensing.slnx -warnaserror`.
The root `Intelibill.slnx` builds merchant and licensing projects together.

## Runtime configuration

Provision a separate PostgreSQL 17 database and a dedicated login for the API.
Supply `LicensingDatabase__Host`, `LicensingDatabase__Database`,
`LicensingDatabase__Username` and `LicensingDatabase__Password`. Optional
settings under the same prefix include `Port` (5432), `MaxPoolSize` (12)
and `TimeoutSeconds` (5 for connection acquisition and 5 for commands by
default). Run the API with `dotnet run --project src/licensing/Intelibill.Licensing.Api`.

Revoke public connection and temporary-object privileges on the licensing
database and grant `CONNECT` to its own roles. The runtime login needs schema
`USAGE` and grants for the data it uses. Keep ownership and DDL privileges
with the migrator. Keep merchant access and licensing access in separate
roles and restrict each role to its own service database. Use explicit data
grants for the runtime login and reserve PostgreSQL predefined roles for
administrative connections.

| Anonymous probe | Response |
| --- | --- |
| `GET /health/live` | HTTP 200 with `{"service":"intelibill-licensing","status":"live"}` |
| `GET /health/ready` | HTTP 200 with `{"service":"intelibill-licensing","status":"ready"}` when the licensing database is reachable under a restricted role |
| `GET /health/ready` when the check fails | HTTP 503 with `{"service":"intelibill-licensing","status":"unavailable"}` |

Both probes send `Cache-Control: no-store`. Readiness checks the runtime
connection and its role privileges. API startup works during a licensing
database outage when settings are valid.

## Errors and logging

Unhandled failures answer the same `application/problem+json`
`{type,title,status,code,traceId}` envelope as the merchant API
(`Internal.Unexpected` for anything unplanned), from
`src/shared/backend/Intelibill.Shared.Web`. Every log sink sits behind the
shared redactor and spans lose exception text before export. Optional
`Observability__Logging__MinimumLevel` (Information),
`Observability__Logging__EnablePiiMasking` (true) and
`Observability__Logging__AsyncBufferSize` (10000) tune logging.

## Migrations

The API never migrates. The `licensing-migrate` image
(`src/licensing/Dockerfile.migrate`) is an EF migration bundle for
`LicensingDbContext`; it reads `LicensingMigrationDatabase__Host`, `__Port`,
`__Database`, `__Username` and `__Password` (all but `Port` required) and runs
as the identity that owns the licensing database. Provision the database and
roles before it runs and grant the runtime login afterwards.

The first migration is `InitialAdminIdentityAndAudit`. Run the explicit
migration from the repository root with:

```sh
dotnet ef database update \
  --project src/licensing/Intelibill.Licensing.Infrastructure \
  --startup-project src/licensing/Intelibill.Licensing.Api \
  --context LicensingDbContext
```

Locally, `docker compose --profile full up -d --build` does all of this:
`db-bootstrap` prepares the database and roles, `licensing-migrate` runs the
bundle, `licensing-runtime-role` grants the runtime login and checks it holds
no schema or role authority, and only then does `licensing-api` start. See
[docs/licensing-rollout.md](../../docs/licensing-rollout.md).

## Security primitives

Both APIs register the bcrypt password hasher and opaque token primitives from
`Intelibill.Shared.Security`. Opaque token creation returns the V1 carrier
from `Intelibill.Shared.Contracts`. Each application owns credential storage,
token issuance policy, sessions and authorization. Fixture-based regression tests verify
merchant and licensing authority independently.

## First provider owner

After the explicit migration, an operator with the separate migrator login can
attest an independently verified email and prepare one 30-minute setup proof.
Use a non-secret attestation reference. The command emits the proof once on
stdout, so capture it in a protected file and deliver it out of band.

```sh
umask 077
dotnet run --project src/licensing/Intelibill.Licensing.Api -- \
  bootstrap-admin prepare --verified-email "$VERIFIED_EMAIL" \
  --verification-reference "$ATTESTATION_REFERENCE" --admin-origin "$ADMIN_ORIGIN" \
  > "$PROTECTED_PROOF_FILE"
```

The admin origin must be HTTPS. The local fixture permits HTTP loopback in
Development or Testing. Run the command with `DOTNET_ENVIRONMENT=Development`
for a loopback fixture. Terminate HTTPS at a trusted ingress, preserve the
external Host header through the admin proxy, and keep the API upstream private.
For multiple API replicas, configure a shared, access-controlled ASP.NET Core
Data Protection key ring for the bootstrap CSRF tokens. Set
`AdminBootstrap__DataProtectionKeyDirectory` to its protected mount path when
using a shared file-system key ring.

Open `/auth/bootstrap` on the prepared admin origin and enter the proof and a
chosen password. A successful request permanently consumes the proof and creates
one verified owner awaiting MFA. Setup uses a separate 15-minute enrollment
cookie. If it is lost or expires, the initial owner can resume at
`/auth/bootstrap/enrollment` using the verified email and chosen password while
MFA enrollment remains pending. Resuming revokes the preceding enrollment
credential. Both credentials are stored only as digests and the password is
stored as a bcrypt hash.

The enrollment cookie grants pending enrollment access through
`BootstrapEnrollment`. Provider business endpoints use
`ProviderBusinessAccess` and require independent business authentication.
P01-06 ends at pending enrollment.

## Administrator invitations

A platform owner invites further administrators; there is no public signup.
Both routes need the `AddAdminInvitations` migration, applied by the explicit
migration job before the API is updated.

- `GET /api/admin/invitations/context` and `POST /api/admin/invitations`
  (`{ email }`) run behind the `ProviderInvitations` policy: a provider session
  carrying the `admin.invitations.manage` capability and an MFA time less than
  five minutes old. The handler then requires that the actor is the persisted
  bootstrapped owner with completed MFA. The POST also requires the exact
  provider origin, the request token from the context read
  (`X-Bootstrap-CSRF`) and a durable issuance budget (20 per hour). It answers
  201 with the invitation identifier and expiry, never the token.
- The invitation is stored as a SHA-256 digest, bound to one normalized email,
  and expires after 72 hours. Re-inviting an address retires its open link. The
  email carries `<origin>/auth/invitation#token=…`: the token stays in the URL
  fragment, which browsers never send to a server, and the page moves it into
  memory and out of the address bar at once.
- `GET /api/admin/auth/invitation/context` and
  `POST /api/admin/auth/invitation/accept`
  (`{ invitationToken, email, password }`) need the provider origin, the
  request token and a durable acceptance budget (30 per 15 minutes). Only the
  invited email is accepted. Five wrong identities retire the link. An
  expired, retired or used link answers 409 `Invitation.Unavailable`.
  Concurrent acceptances serialize on the invitation row; exactly one succeeds.
- Acceptance creates a verified administrator awaiting MFA and sets the same
  15-minute enrollment cookie as the first owner. It grants no capability and
  no business session: `/api/admin/access` answers 403 and the invitation
  routes answer 401.
- Issued, accepted, refused, superseded and undeliverable invitations append
  provider audit events. Accepted or retired invitations cannot be changed or
  deleted. Logs carry invitation identifiers and outcome codes only.

Delivery uses the `AdminMail` section (`Host`, `Port`, `FromEmail`, `FromName`,
`Username`, `Password`, `UseStartTls`). A real transport always uses TLS and
authentication. `LocalCapture=true` (Mailpit, no TLS, no login) is honored only
in Development or Testing. When delivery is unconfigured or fails, the
invitation is retired and the API answers 503
`Invitation.DeliveryUnavailable`.

The local invitation fixture uses `AdminFixtures` (`SessionDigest`, the SHA-256
of a fixture cookie, `AdminId`, `Capabilities`, and optional `MfaVerifiedAt`).
Startup refuses a configured fixture outside Development or Testing. The
fixture only reaches invitation authority checks and still requires the
persisted MFA-enrolled owner.

## Authenticator enrollment

Apply `20261114185900_AddAdminMfaEnrollment` through the explicit licensing
migration job before serving enrollment. The owner setup and invitation
acceptance pages both link to `/auth/mfa-enrollment`. That page requires the
existing pending enrollment cookie, exact origin and CSRF token. It shows a
manual setup key for a six-digit, 30-second TOTP. Verification records the used
step, completes enrollment, retires the pending credential and returns ten
one-use recovery codes in the successful response. Save the codes before
leaving the page. A lost response or refresh cannot redisplay them.

Configure `AdminMfa__ActiveKeyId` and `AdminMfa__Keys__<key-id>` with an
independently generated, base64-encoded 32-byte root from the licensing API's
secret store. Each key ID derives separate TOTP encryption and recovery HMAC
keys. Keep every referenced key ID in the ring across replicas, restarts and
rotation. Changing the active ID selects the key for new setups. Keep this ring
separate from ASP.NET Data Protection, provider session keys and merchant keys.
Without the required key, enrollment and proof verification fail closed. The
migration job needs only its database credentials.

For local Compose, set `INTELIBILL_ADMIN_MFA_ACTIVE_KEY_ID=local` and provide
`INTELIBILL_ADMIN_MFA_LOCAL_KEY` from a stable, private local fixture value.
The reproducible acceptance runner provisions and destroys its own isolated
database and generates ephemeral fixture keys.

Every attempt against a started enrollment shares the administrator's
database budget of five tries in 15 minutes. TOTP accepts the current counter
and one step of drift in either direction, with a persisted high-water mark.
Sign-in consumes an authenticator step and commits the new session in the same
transaction. The verifier is also available to later credential workflows.

## Provider sign-in

Apply `20261114194123_AddAdminSignInSessions` through the explicit licensing
migration job. The provider origin is the exact browser-facing origin recorded
by first-owner bootstrap. Configure `AdminSessions__SigningKey` on the source
API with a dedicated base64-encoded 32-byte random key shared by the licensing
replicas. Share the existing ASP.NET Data Protection key ring across those
replicas for request tokens. For local Compose sign-in, supply
`AdminSessions__SigningKey` to the `licensing-api` service through a local
Compose override alongside the MFA environment settings. With no signing key
the API leaves the sign-in endpoints unavailable and still serves bootstrap,
invitations and enrollment according to their own authorization.

At `/auth/sign-in`, enter the enrolled email, password and a fresh authenticator
code. The API issues a host-only, HttpOnly, SameSite=Strict cookie with a
15-minute access lifetime. HTTPS uses the `__Host-` prefix and Secure.
Development and Testing allow the existing loopback HTTP origin. Each authorized
request checks the device session, admin status, 30-minute inactivity and
12-hour absolute limit in PostgreSQL. The dashboard reads its capabilities
from the session API and current platform ownership provides the invitation
capability. An enrolled invitee can sign in and sees an empty capability list.

Sign-in requires an exact Origin and the `X-Admin-CSRF` request proof. PostgreSQL
enforces 5 sign-in attempts per normalized account per 15 minutes and 60
overall per 5 minutes, across replicas. The authenticator keeps its existing
separate proof budget. Provider audit events record accepted and refused
sign-ins without credential material. Access expiry currently returns the
operator to sign-in. Refresh rotation, operator revocation, recovery and grant
management are the subsequent P01 slices.

Run `bash tests/licensing/acceptance/run-p0109-browser.sh` for the disposable
PostgreSQL, migration job and Chromium acceptance flow.
