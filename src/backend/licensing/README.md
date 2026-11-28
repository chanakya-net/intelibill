# InteliBill Licensing API

Build the licensing solution from the repository root with
`dotnet build src/backend/licensing/Intelibill.Licensing.slnx -warnaserror`.
The root `Intelibill.slnx` builds merchant and licensing projects together.

## Documentation

- [Architecture](architecture_doc/architecture.md): layers, request flow, functional areas, persistence,
  merchant integration, security invariants and test structure.
- [Licensing rollout](../../../docs/licensing-rollout.md): local stack, ports, databases and identities,
  configuration keys, integration test notes and per-slice rollout notes. Further slice notes are
  `docs/licensing-*.md`.
- Related projects: [merchant backend](../intelibill/architecture_doc/architecture.md),
  [shared libraries](../shared/architecture_doc/architecture.md),
  [admin web](../../frontend/licensing/architecture_doc/architecture.md),
  [infrastructure](../../../.tofu/architecture_doc/architecture.md).

## Runtime configuration

Provision a separate PostgreSQL 17 database and a dedicated login for the API.
Supply `LicensingDatabase__Host`, `LicensingDatabase__Database`,
`LicensingDatabase__Username` and `LicensingDatabase__Password`. Optional
settings under the same prefix include `Port` (5432), `MaxPoolSize` (12)
and `TimeoutSeconds` (5 for connection acquisition and 5 for commands by
default). Run the API with `dotnet run --project src/backend/licensing/Intelibill.Licensing.Api`.

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
`src/backend/shared/Intelibill.Shared.Web`. Every log sink sits behind the
shared redactor and spans lose exception text before export. Optional
`Observability__Logging__MinimumLevel` (Information),
`Observability__Logging__EnablePiiMasking` (true) and
`Observability__Logging__AsyncBufferSize` (10000) tune logging.

## Migrations

The API never migrates. The `licensing-migrate` image
(`src/backend/licensing/Dockerfile.migrate`) is an EF migration bundle for
`LicensingDbContext`; it reads `LicensingMigrationDatabase__Host`, `__Port`,
`__Database`, `__Username` and `__Password` (all but `Port` required) and runs
as the identity that owns the licensing database. Provision the database and
roles before it runs and grant the runtime login afterwards.

The schema starts from a single migration, `20261127074702_Baseline`, which
runs the embedded `Migrations/20261127074702_Baseline.sql` script. Never edit
that script; add new migrations after it. Its `Down` is unsupported: drop and
recreate the database instead. With the `LicensingMigrationDatabase__*`
environment variables set, run the explicit migration from the repository root
with:

```sh
dotnet ef database update \
  --project src/backend/licensing/Intelibill.Licensing.Infrastructure \
  --startup-project src/backend/licensing/Intelibill.Licensing.Api \
  --context LicensingDbContext
```

Locally, `docker compose --profile full up -d --build` does all of this:
`db-bootstrap` prepares the database and roles, `licensing-migrate` runs the
bundle, `licensing-runtime-role` grants the runtime login and checks it holds
no schema or role authority, and only then does `licensing-api` start. See
[docs/licensing-rollout.md](../../../docs/licensing-rollout.md).

The baseline includes the provider-only draft catalogue (plans, plan versions,
trial policy versions and their change evidence) under forced row-level
security. See "Draft plans and trial policy (P02-06)" in the rollout guide.

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
dotnet run --project src/backend/licensing/Intelibill.Licensing.Api -- \
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

The owner setup and invitation
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

The provider origin is the exact browser-facing origin recorded
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
sign-ins without credential material. Device session revocation
(`/api/admin/sessions`), account recovery (`/api/admin/auth/recovery`) and
capability management (`/api/admin/users`) are served by the same API.

`tests/backend/licensing/acceptance/run-p0109-browser.sh` is the disposable
PostgreSQL, migration job and Chromium acceptance runner for this slice. The
`run-p01NN-browser.sh` runners from P01-08 to P01-14 still name migration IDs
from before the baseline squash, so update them before relying on a run.

## Provider session refresh

Sign-in also sets an HttpOnly, SameSite=Strict refresh cookie (`__Host-` and
Secure over HTTPS, `/api/admin` on the loopback HTTP origin) that lasts until the
session's 12-hour absolute end. `POST /api/admin/auth/refresh` needs the exact
Origin and `X-Admin-CSRF` proof from `GET /api/admin/auth/sign-in/context`; a
refused proof never reads the credential. A refresh consumes the presented
credential and issues one successor in the same session (`parent_id` chain,
unique per parent and one root per session) plus a new 15-minute access token.
`admin_refresh_credentials` stores only the SHA-256 digest. Rotation locks the
administrator, the session and then the credential, so presentations on any
replica serialize.

Refresh never changes the session's creation or MFA time: the 12-hour absolute
limit and the five-minute recent-MFA window still count from sign-in. It
honours the 30-minute inactivity limit and counts as activity. A deactivated
administrator, a revoked or expired session and an unknown credential get 401
`Admin.SessionEnded`, and both session cookies are cleared. A consumed
credential presented again revokes the whole session and records audit action
16 (`SessionReuseRevoked`), so neither holder keeps a live successor and the
next protected request answers 401.

The admin web renews when a protected request returns 401. Requests in one tab
share a renewal. Refresh and sign-in take turns under the same Web Lock, and
protected retries verify that the browser is still using the session generation
that started the request. Browsers where coordination fails show a recovery
notice and send zero refresh POSTs.

The console stores only a browser-generated generation and renewal state in
local storage. It records a pending rotation before sending the POST. If the
outcome stays unconfirmed, waiting tabs can check the live access session or
offer an explicit retry. A retry of a consumed credential ends the session. A
sign-in whose tab closed with its POST in flight leaves its marker behind:
automatic checks stay unconfirmed, and an explicit retry first waits for the
Web Lock, then checks the live session before any rotation. A
definitive rejection clears grants and discards authorized invitation forms
and their request proofs. Invitation recovery checks the session and requests
a fresh proof before showing the form.

`tests/backend/licensing/acceptance/run-p0110-browser.sh` is the acceptance
runner for this slice. It was written as an upgrade from the P01-09 migration
and is affected by the baseline note above.

## Recovering a checkout

The local Compose role preparation script grants the
runtime execute permission on the payment scheduler's private routines. Refresh
those grants after manually migrating a local database.

The original payment attempt is queued in the same transaction that saves its
operation key and provider pin. The local simulator worker claims due attempts
with 30-second leases, performs provider reads outside SQL transactions, and
validates the live claim when it records the result. Twelve observations with
bounded backoff leave unresolved attempts available for an owner to check
again. Use `Payments:ReconcileInBackground=false` to control the worker manually
in a fixture. The simulator and its controls require Development or Testing
with `Payments:Mode=LocalSimulator`.

The merchant web app keeps the frozen start command in owner, account and order
scoped browser storage. The existing payment status and verification routes
recover that attempt. The simulator offers timeout, delayed full capture and
definitive failure on an existing session. See
[P03-03 checkout recovery](../../../tests/backend/licensing/acceptance/p03-03-checkout-recovery.md)
for the PostgreSQL and browser fixtures.

### Fresh paid periods and service restoration

First purchase and trial conversion start on a new anchor when licensing can publish the paid entitlement. A paid package bought after grace starts a fresh period at restoration readiness. Verified funds held by a restriction can use the same attempt or manual confirmation after an independent release. An accepted on-time renewal keeps its original anchor even when confirmation arrives late.

The payment status distinguishes recorded funds, paid term fulfilment and merchant acknowledgement. A Licensing Manager can link a separately approved complimentary grant to a fulfilled payment order for covered shops. The adjustment preserves the original price and paid dates.

## Role-correct licence notices (P07-01)

`NotificationPlanner` and `NotificationPlanningWorker` (configured under `Notifications:*`) schedule durable semantic notice intents into `notification_jobs` (`PlanLicenseNotices` migration) with forced RLS, append-only history triggers, semantic uniqueness `(account_id, scope_kind, scope_id, recipient_key, purpose, source_kind, source_id, source_revision, threshold)`, and verified email alias uniqueness (`ux_notification_jobs_email_destination`).

- A member and a verified contact sharing an identity or destination have one scheduled job per threshold. An additional authorized channel is recorded in immutable `notification_channel_additions`; provider preview and merchant reads project the effective channels.
- Discovery uses worker-only `SECURITY DEFINER` routines (`public.due_notification_planning(timestamptz, uuid, integer)` excluding actor/account/shop/admin GUCs and filtering expired/converted/closed/complimentary work, plus `public.account_notification_billing_members`), granted explicitly in `scripts/db/local-licensing-role.sql`.
- Merchant identity and exact `(shop, account, affiliation)` proof are fetched outside database transactions via signed RS256 requests to `/internal/merchant/v1/licensing/notice-recipients`; licensing acquires a shared account lock, re-checks live term/roster/restriction preconditions and `FOR SHARE`-locks active billing memberships and verified opted-in `billing_notice_contacts` before inserting.
- Contact approval omits verification proof tokens from all HTTP responses, mutations bind `operation_key` to normalized payloads in `billing_notice_contact_operations` with append-only `OptedOut` history, and masked destination hints never expose plaintext addresses (including one-character local parts).
- Separate read contracts serve current operational shop guidance (`GET /api/licensing/shops/{shopId}/notices`), current per-source commercial account notices with bounded shop samples (`GET /api/billing/accounts/{accountId}/notices`), paged billing contacts (`GET /api/billing/accounts/{accountId}/notice-contacts?after=`), and full-history live capability-checked provider previews (`GET /api/admin/accounts/{accountId}/notifications`, `Cache-Control: no-store`, Support redacted). Email transport delivery (`#925`) and send-time invalidation (`#926`) remain separate slices.

## Localized email delivery

Apply the `DeliverBillingNoticeEmails` migration and runtime grants before starting workers.
The planner queues one delivery for the first effective Email channel on a notice.
Standalone contact approval queues an encrypted, single-use proof in the approval transaction.
The holder types their mailbox and chooses whether to opt in at `/billing-contact/verify`.

The full local Compose stack uses Mailpit, synthetic From and Reply-To addresses and a project-owned licensing key volume.
`BillingEmail` configures the sender, support mailbox, merchant and verification origins and Mailpit UI origin.
Enablement requires Development or Testing, local SMTP capture without credentials and loopback browser origins.
Operators can inspect the SMTP state on the account notification preview.
The [isolated acceptance runner](../../../tests/backend/licensing/acceptance/p07-02-email-delivery.md) exercises signed recipient checks and real mail.
