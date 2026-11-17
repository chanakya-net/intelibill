# Licensing foundation rollout

One Docker Compose command starts the merchant and licensing systems for local
development. CI validates both consumers of shared code. Production provider
access and commercial flows require separate rollout gates.

## Start, restart, stop

```bash
docker compose --profile full up -d --build   # build and start everything
docker compose --profile full up -d           # start again; reruns migrations and grants
docker compose --profile full down            # stop; both databases are kept
```

`down -v` deletes the `postgres_data` volume and with it **both** databases. Use
it only when you mean to start from nothing.

`docker compose up -d` (no profile) still starts PostgreSQL alone for host-side
development; nothing about that workflow changed.

## What starts, in order

| Step | Service | Runs as | Exits / serves |
| --- | --- | --- | --- |
| 1 | `postgres` | — | PostgreSQL 17, volume `postgres_data` |
| 2 | `db-bootstrap` | `intelibill` (superuser) | creates the licensing roles and database once, re-states role attributes, confines `CONNECT` on both databases; exit 0 |
| 3a | `merchant-migrate` | `intelibill` (merchant schema owner) | merchant EF migration bundle; exit 0 |
| 4a | `db-runtime-role` | `intelibill` | merchant runtime grants (`scripts/db/local-runtime-role.sql`); exit 0 |
| 5a | `backend` | `intelibill_app` | merchant API, healthy on `/health/live` |
| 6a | `frontend` | — | merchant web |
| 3b | `licensing-migrate` | `intelibill_licensing_migrator` (licensing database owner) | licensing EF migration bundle; exit 0 |
| 4b | `licensing-runtime-role` | `intelibill` | licensing runtime grants, then refuses to exit 0 if the runtime login could change schema, own anything or assume a role |
| 5b | `licensing-api` | `intelibill_licensing_app` | licensing API, healthy on `/health/ready` |
| 6b | `admin-frontend` | — | admin web |
| — | `mailpit` | — | local mail capture, started before `backend` |

Each step waits for the previous one to **exit 0** (`service_completed_successfully`)
or be healthy. The `a` and `b` branches share only steps 1–2, so a failing
migration can only ever block its own API and web. `docker compose up` reports
such a failure and stops starting anything else it had not started yet; start
the healthy branch alone with `docker compose --profile full up -d frontend`
(merchant) or `... up -d admin-frontend` (licensing). A branch that is already
running keeps serving when a later `up` fails in the other one. One-shot jobs never restart on their own; every `up` runs them again,
which is how a newly added migration is applied and granted. An API restart never
reruns a migration, and neither API issues DDL at startup.

Run the licensing bundle as the migrator before starting API writers. The
bundle applies these migrations in order:

- `20261114170140_InitialAdminIdentityAndAudit`
- `20261114181011_AddAdminInvitations`
- `20261114185900_AddAdminMfaEnrollment`
- `20261114194123_AddAdminSignInSessions`
- `20261114202808_AddAdminRefreshCredentials`
- `20261114221354_AddProviderAuditInspection`
- `20261114225609_AddAdminSessionRevocationAudit`
- `20261115000319_AddAdminRecovery`
- `20261115002745_ProtectAdminRecovery`
- `20261115010116_AddAdminCapabilities`

The initial migration creates an unarmed bootstrap without an owner or proof.
Keep the identity, session and audit history on rollback. Migrations that
protect recorded evidence can refuse a downgrade.

## Ports (loopback only)

| Service | Default | Override |
| --- | --- | --- |
| Merchant API | `127.0.0.1:5277` | `INTELIBILL_MERCHANT_API_PORT` |
| Merchant web | `127.0.0.1:4000` | `INTELIBILL_WEB_PORT` (also moves `App__BaseUrl`) |
| Licensing API | `127.0.0.1:5278` | `INTELIBILL_LICENSING_API_PORT` |
| Admin web | `127.0.0.1:4001` | `INTELIBILL_ADMIN_WEB_PORT` |
| Mailpit UI / SMTP | `127.0.0.1:8025` / `127.0.0.1:1025` | `INTELIBILL_MAILPIT_UI_PORT` / `INTELIBILL_MAILPIT_SMTP_PORT` |
| PostgreSQL | `5432` | `INTELIBILL_POSTGRES_PORT` |

`INTELIBILL_CONTAINER_PREFIX` (default `intelibill`) prefixes every container
name. Together with `docker compose -p <name>` (own volume and network) these
let a second, fully isolated stack run beside your normal one, which is what the
acceptance harness does.

## Databases and identities (local only)

| Database | Owner / migrator | Runtime login |
| --- | --- | --- |
| `intelibill_dev` | `intelibill` (unchanged) | `intelibill_app` |
| `intelibill_licensing_dev` | `intelibill_licensing_migrator` | `intelibill_licensing_app` |

The passwords are fixed, public development values written in
`docker-compose.yml` and `scripts/db/local-licensing-role.sql`. They exist only
in this local stack. Both runtime logins are `NOSUPERUSER NOBYPASSRLS
NOCREATEDB NOCREATEROLE NOREPLICATION NOINHERIT`, belong to no role, own
nothing, hold schema `USAGE` without `CREATE`, cannot create temporary tables
and can connect only to their own database (PUBLIC `CONNECT`/`TEMPORARY` is
revoked on both). Data grants cover existing tables and, through default
privileges `FOR ROLE` the actual migrator, tables it creates later.

`scripts/db/local-licensing-role.sql` refuses to run unless given
`-v environment=Development` (or `Testing`), is serialized by an advisory lock
and never drops, recreates or reassigns anything: an unexpected owner or role
membership stops it with an error instead.

### Existing developer volumes

The first full `up` after pulling this change, on a volume that already holds
merchant data:

- `merchant-migrate` applies any merchant migrations you had not yet applied
  (previously you ran `dotnet ef database update` by hand);
- `db-bootstrap` revokes PUBLIC `CONNECT` and `TEMPORARY` on `intelibill_dev`.
  `intelibill_app` keeps its explicit `CONNECT`; the superuser `intelibill`
  is unaffected. Any other login you created yourself needs its own
  `GRANT CONNECT`;
- the licensing database and roles are created beside it. Merchant data,
  ownership and migration history are left as they were.

## Configuration keys

| Service | Prefix / keys | Notes |
| --- | --- | --- |
| `merchant-migrate` | `Database__Host/Port/Database/Username/Password` | schema owner |
| `backend` | as before, plus `DistributedCache__CreateIfNotExists=false` | unchanged otherwise; still `ASPNETCORE_ENVIRONMENT=Development` for Mailpit and relaxed rate limits |
| `licensing-migrate` | `LicensingMigrationDatabase__Host/Port/Database/Username/Password` | migrator only; never given to the API |
| `licensing-api` | `ASPNETCORE_ENVIRONMENT=Development`, `LicensingDatabase__Host/Port/Database/Username/Password` | image default is Production |
| `frontend` | `API_ORIGIN=http://backend:8080` | forwards `/api` and `/hubs` |
| `admin-frontend` | `API_ORIGIN=http://licensing-api:8080`, `PORT` | forwards only `/api/admin/**`; every other `/api` or `/hubs` path answers 404 `no-store` |

## What you can do locally today

- Enrol a merchant through the merchant web with any address (for example
  `you@example.test`): the verification email lands in Mailpit at
  `http://127.0.0.1:8025`, and nothing leaves the machine.
- Open the admin web at `http://localhost:4001`. Before provider setup, the
  Console reports an unconfirmed session and hides capabilities. Once setup
  and signing are configured, an unauthenticated browser is sent to sign-in.
  Merchant credentials carry no provider authority.
- Probe `GET http://127.0.0.1:5278/health/live` and `/health/ready`.

For a disposable fixture, run [the P01 foundation smoke check](../tests/licensing/acceptance/p01-foundation.md).
It supplies separate local MFA and session-signing roots, prepares a one-use
owner proof and exercises invitation delivery through Mailpit. The owner
chooses a password, enrolls an authenticator, acknowledges recovery codes and
signs in with password and TOTP. The console shows the session's explicit
capabilities. Invited operators complete the same MFA setup and start with
zero grants. Device session and audit views apply their own authorization.
Without configured roots, MFA setup and sign-in fail closed.

Live access still requires verified initial owner identity, approved key custody
and recovery ownership, and a verified production hostname, TLS, exact origin,
secret delivery and database roles. The local fixture uses only loopback
services and synthetic identities.

## Troubleshooting

- `docker compose ps -a` shows each one-shot's exit code. A nonzero
  `merchant-migrate` or `licensing-migrate` leaves its own API and web
  unstarted (`Created`); read it with `docker compose logs <service>` and start
  the other branch alone as described above.
- `licensing-runtime-role` exits nonzero when the runtime login has gained an
  attribute, membership, ownership or schema privilege it must not have. Fix the
  role (as the superuser) rather than editing the check.
- `db-bootstrap` exits nonzero if `intelibill_licensing_dev` exists with another
  owner or a service login belongs to a role.
- A port already in use: stop the other process or set the matching
  `INTELIBILL_*_PORT`.

## CI

`Application Release` (`.github/workflows/deploy.yml`) validates pull requests
that touch merchant, licensing, admin, shared, workspace, Compose or local
database script paths. Its single validation job builds the root
`Intelibill.slnx` (both backends and the shared projects), runs every test
project under `tests/backend`, `tests/licensing` and `tests/shared` once,
installs the root Bun workspace once, builds and tests the merchant and admin
web apps, checks the Compose contract and the admin runtime server, and builds
and scans all six images. Licensing and admin images are built for validation
only; the publish and dev/prod release jobs, and the main-push trigger set that
starts them, are unchanged.

## Acceptance harness

`scripts/dev/test-licensing-stack.sh` runs the real workflow under isolated
Compose projects (never your default stack, containers or volume) and records
evidence. The harness uses Docker Compose and `jq` to check resolved resources
and existing ownership before changing or removing a fixture. It reads the
checked-in Compose file with fixture-specific ports and skips local Compose
overrides and `.env`. See `tests/licensing/acceptance/p01-05-infrastructure.md`.

```bash
P0105_PROJECT=intelibill-p01-05-local P0105_ARTIFACTS=/tmp/p0105 \
  bash scripts/dev/test-licensing-stack.sh --case all
bash scripts/dev/test-licensing-stack.sh --case cleanup   # same P0105_PROJECT
```

## Merchant billing delegation

The merchant serves `GET /api/billing/accounts/{accountId}/access` under its
existing origin. It checks the current session and verified identity before
sending a one-use RS256 assertion. The licensing service binds the assertion to
`GET /internal/licensing/v1/customer/accounts/{accountId}/access` and independently
checks account membership before recording its use. The response contains only
the account ID and permitted action. The wire contract allows a verified-identity
flag and identifiers for the actor, operation and request. It carries no contact
details, shop data, consent or capacity. Both responses use `Cache-Control: no-store`.
The merchant and admin web proxies expose only their existing API paths.

Apply the licensing migration `20261115124718_AddMerchantBillingDelegationUses`
through the migration job before running the new licensing API. Preserve the
replay table on application rollback. The migration refuses to drop recorded uses.
The runtime may append and read within the verified actor and account scope.
Scope is set inside the membership and replay transaction and clears when it
ends. PostgreSQL arbitrates simultaneous uses across replicas.

The merchant uses `MerchantBillingDelegation__LicensingOrigin` for an HTTPS
origin and `MerchantBillingDelegation__KeyVaultKeyId` for a separate vault key.
`MerchantBillingDelegation__ManagedIdentityClientId` can select the signing
identity. Licensing receives the corresponding RSA public key in
SubjectPublicKeyInfo PEM at `MerchantBillingVerification__PublicKeys__<kid>`,
where the key ID is the vault key version.

For a staged rotation, configure the merchant with
`https://<vault>/keys/<name>/<version>` and use this order.

1. If the merchant uses a versionless reference, pin the currently accepted
   version on every merchant replica before creating a new version.
2. Create the new version. Add its public key to the verification ring on
   every licensing replica and confirm that each has loaded the updated ring.
3. Change the merchant pin to the new version on every merchant replica.
   The signer picks up the pin on the next request after configuration reload,
   or after restart for environment variable changes.
4. Wait at least 60 seconds after the last old signer stops issuing assertions
   before removing its public key from the accepted ring. Retain public
   verification material as required for historical evidence. Remove a revoked
   key from the accepted ring on every replica immediately.

A versionless `https://<vault>/keys/<name>` reference follows the current
version on first use, then on the first request at or after the five-minute
refresh interval. A restart can pick up a new version immediately. If an
unstaged rotation selects a key that licensing has yet to accept, the merchant
returns 503 `Billing.DelegationUnavailable` and records
`UpstreamAuthentication`.

Keep the clocks on both hosts synchronized. Licensing gives no tolerance to
a future whole-second `iat` or `nbf` and rejects an assertion at its expiry.
A licensing clock trailing the merchant can cause intermittent 503
`Billing.DelegationUnavailable` responses at the merchant.

Local PEM signing (`LocalKeyId`, `LocalPrivateKeyPem` under the merchant section)
and loopback HTTP are restricted to Development and Testing.

### Billing accounts (P02-02)

A verified merchant creates a billing account explicitly. Nothing creates one
from a shop, email address, phone number, GSTIN or login, and no table holds
any of those. The creating merchant user becomes the account's only active
owner. Billing membership grants billing actions only and never grants access
to a merchant shop.

Apply the licensing migration `20261115142000_BillingAccountsAndPlanLifecycle`
through the migration job before deploying the licensing API, and deploy the
licensing API before the merchant API. The migration adds `billing_accounts`
and `billing_account_members` with forced row-level security and makes
`merchant_billing_delegation_uses.account_id` nullable for actor-scoped uses.
Its `Down` refuses (`55000`) while any account, member or actor-scoped use
exists. A runtime `DELETE` or `TRUNCATE` on either table, or an `UPDATE` of an
account, is refused (`55000`).

The V1 delegation contract gains two actor-scoped actions,
`billing.account.list` (`GET /internal/licensing/v1/customer/accounts`) and
`billing.account.create` (`POST` on the same path). Their assertions carry the
same fields as the account read without `account_id`; the read keeps its exact
field set. Licensing rejects a token whose scope does not match its action. A
new merchant against an older licensing API gets 401 upstream and answers 503,
which fails closed. See `tests/contracts/licensing/billing-accounts-v1.json`.

The create body is `{"operationKey","name"}`. The merchant serializes it from
validated values, signs the SHA-256 of the exact bytes and sends those bytes.
Licensing reads at most 1024 bytes, checks the digest, and then deserializes
strictly. The account ID is derived from the verified actor and the operation
key, so the primary key is the idempotency fence. A retry with the same key and
name answers 200 with the same account. The same key with another name answers
409 `Billing.OperationPayloadMismatch`. The account, its owner and the replay
use commit in one transaction.

Account context comes only from the delegation transaction
(`set_config(..., true)`), never from a client value. Without an account
context no account row is visible; with only the actor set, the actor sees its
own membership index. The production membership adapter
`BillingAccountMembershipAuthority` replaces the unavailable default. It locks
the actor's active membership `FOR SHARE` and allows `billing.account.read` to
Owners and Managers. `GET /api/billing/accounts/{id}/access` therefore answers
200 for a member and 403 otherwise.

The merchant exposes `GET /api/billing/accounts` and `POST /api/billing/accounts`
for any live verified session, without a shop or role policy. Creation has its
own budget of 10 per caller each hour. It checks no credential and sends no
message, so it uses no subject budget and does not fail closed. The merchant
web page `/billing` (profile menu, "Billing accounts") lists accounts and
creates one only on explicit submit. It freezes the operation key and name in
`localStorage` before sending and reuses them on every retry until a
definitive answer.

Listing returns `{ accounts, nextCursor }` with at most 100 accounts per response,
ordered by account ID. The merchant accepts a licensing page of at most 88,256 bytes:
100 accounts whose 120-character names are fully JSON-escaped (six bytes per UTF-16
unit, as for emoji), so no valid name can make a page unreadable. Continue using `GET /api/billing/accounts?after={nextCursor}`
until the cursor is null. The merchant signs the canonical internal path
`/internal/licensing/v1/customer/accounts/after/{cursor}`; licensing rejects changed
paths and extra query parameters. A cursor is only a position, never account authority.
Every page authorizes the live caller and reads its own active memberships under RLS.
The web collects the traversal before replacing its list, preserving the current rows
if any page fails. Creation triggers a fresh traversal, as does reload. Concurrent
membership changes are reflected by a fresh traversal; pages are not a frozen snapshot.
Deploy licensing, then merchant, then web so all consumers understand continuation.

Replay-row retention: keep every `merchant_billing_delegation_uses` row through
observe-only Phase 2. The page traverses the list once per visit and once per
create (one delegated call per page), with no polling. Before paid traffic (P03), schedule a migrator-only
purge of rows past `expires_at` plus a retention window. Keep global replay
uniqueness for every assertion that can still be accepted.

Run the local browser check after `bun run build` from each of `src/frontend`
and `src/admin-frontend`.

```sh
P0201_RUN_BROWSER=1 dotnet test tests/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj --filter 'FullyQualifiedName~P0201BrowserTests'
P0202_RUN_BROWSER=1 dotnet test tests/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj --filter 'FullyQualifiedName~P0202BrowserTests'
```

The P0202 check needs only the merchant web build. It runs the production
membership adapter and exercises explicit creation, retry, reload, the listing
and another merchant's refusal at desktop and narrow mobile widths.

The harness starts both APIs on loopback and both shipping web servers. It applies
migrations to disposable PostgreSQL databases and supplies account membership
from a test-only schema. Chromium exercises merchant sign-in, allowed and denied
billing access, an existing merchant read and the proxy boundaries. The harness
prints the location of its sanitized observations.

### Billing manager invitations (P02-03)

Rollout order is licensing migration `20261115152000_AddBillingManagerInvitations`,
licensing API, merchant API, then merchant web. The migration preserves existing
billing accounts, members and delegation-use records. Its `Down` refuses while
invitation, receipt or audit evidence exists. Application rollback retains those
facts and the existing licensing Data Protection key ring. All licensing API
replicas issuing or exchanging invitations need the same retained key ring.

An active Billing Owner requests an invitation using an existing user's verified
email address or phone number. After an owner check, the merchant resolves the
credential privately and signs only the resolved identity and request digest.
An eligible recipient sees the invitation in their signed-in Billing inbox for
seven days. Issuance acknowledges the keyed request uniformly. A recipient
explicitly accepts one invitation revision. A purpose-bound proof travels only
between the APIs. The invitation retains its hash and clears its protected copy
on acceptance. Licensing commits one active Manager membership and a versioned
audit event in the same transaction that consumes the invitation.

Verify through the local PostgreSQL and Chromium fixtures. This slice adds
commercial membership under the observe-only configuration. Shop authorization
continues through merchant sessions and memberships.

### Draft plans and trial policy (P02-06)

Rollout order is licensing migration `20261116090000_AddCatalogueDrafts`, then the
licensing API, then the admin web. The migration adds `plans`, `plan_versions`,
`trial_policy_versions` and the append-only `catalogue_draft_changes`, all with
forced row-level security: a transaction sees or writes them only after naming,
transaction-locally (`app.catalogue_admin_id`), an active, MFA-enrolled
administrator who holds the Licensing Manager grant. Without that context the
runtime role sees no rows and cannot insert, so drafts never reach merchant or
customer surfaces. `Down` refuses (55000) while any catalogue row exists.

Only the persisted `licensing-manager` grant may read or save drafts; Platform
Owner alone is refused (403 `Catalogue.AccessDenied`). Saves need a live provider
session, the admin origin and `X-Admin-CSRF`, and an explicit `expectedVersion`
(`0` creates; a stale revision answers 409 `Catalogue.DraftVersionConflict`).

- `GET /api/admin/plans?cursor=`, `PUT /api/admin/plans/{planId}/draft`
- `GET /api/admin/trial-policy/draft`, `PUT /api/admin/trial-policy/draft`

Every fact is optional while drafting and stays visibly missing (`missing[]`,
`complete: false`); there is no unlimited value or default. Prices are whole INR
paise from 0 to 1,000,000,000,000 (zero is an explicit fact), capacities and
trial caps 1 to 100,000, the trial lasts a fixed 14 days. Single-shop covers one
shop with no extra shops; Multi-shop needs at least two shops and prices extra
shops only when offered; Custom needs a recorded negotiation reason. No invoice
quotas, feature tiers, metered seats or extra fees exist. Every draft stays
inactive: publication, retirement and customer catalogue reads are later slices.
The admin web exposes the editor at `/catalogue`. Verify with `P0206Tests` and,
after building the admin web, `P0206_RUN_BROWSER=1` `P0206BrowserTests`; all
amounts in those runs are local fixture values.

### Billing ownership transfer (P02-04)

Apply licensing migration `20261116120000_AddBillingOwnershipTransfers` before
deploying the licensing API, merchant API and merchant web. All licensing
writers must use the account fence before taking membership locks. Coordinate
the writer cutover before serving transfer requests. The migration validates
existing accounts and enforces one active owner at commit. Its downgrade refuses
while transfer evidence exists. An application rollback retains that evidence.

The owner confirms a request for a verified existing merchant identity. The
named recipient has seven days to review the responsibility disclosure and
accept in the merchant app. Acceptance promotes the recipient to Owner and
makes the former Owner an active Manager in one licensing transaction. Existing
financial obligations, coverage and shop memberships continue under their
original recorded terms. The versioned proposal and acceptance jointly retain
both participants' authority and request evidence.

Local verification, after building the merchant web from `src/frontend`:

```bash
dotnet test tests/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj --filter 'FullyQualifiedName~P0204Tests'
P0204_RUN_BROWSER=1 dotnet test tests/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj --filter 'FullyQualifiedName~P0204BrowserTests' --logger 'console;verbosity=normal'
```

### Plan retirement (P02-08)

Apply licensing migration `20261116140000_AddCatalogueRetirement` before the
licensing API, merchant API and both web builds. Upgrade all licensing catalogue
readers before accepting retirement requests. Older readers cannot see the
withdrawal markers. Keep the migration and retained evidence during an application
rollback. Its downgrade refuses with PostgreSQL error `55000` after a retirement.

The Licensing Manager selects a published plan version under **Published versions**
in `/catalogue`, gives a reason and confirms retirement with recent MFA. Recovery
uses the original operation key and the same administrator's live authority.
A public withdrawal and its provider-only reason commit together. The next
catalogue read omits the withdrawn offer. Publishing a successor makes the
new version available for selection.

A saved `/billing/plans?version=<versionId>` link shows the frozen terms and
current availability of a previously public version. Existing coverage and
shop affiliations continue under their recorded terms. The version read works
within its original environment even when new publication is disabled.

Verify locally with `P0208Tests` and `P0208_RUN_BROWSER=1` `P0208BrowserTests`
after building both apps. The Chromium fixture drops the retirement response
after it commits, reloads the console to recover it, and checks the fresh
merchant catalogue and the saved terms link. The fixtures use local prices and
preserve the observe-only licensing configuration.

### Access status and exact deadlines (P02-09)

Apply licensing migration `20261116150000_AddSubscriptionStatusFacts` before the
licensing API, merchant API and both web builds. It adds the append-only
`subscription_term_facts` supplement and narrow shop-scoped read policies; it
backfills nothing and seeds nothing. Its downgrade refuses with PostgreSQL error
`55000` once a fact exists. Keep the migration and facts during an application
rollback.

Status is observe-only. No route, guard or merchant gate consumes it, and no
scheduler moves a term: each read evaluates the stored facts at one instant.
Coverage without a supplement reads as incomplete evidence, never as paid
access with grace. Licensing storage or transport failure answers 503
`Billing.DelegationUnavailable`, which the web apps show as an outage, never as
an expiry. Payment verification reads `NotAvailable`. See
[access status and deadlines](licensing-p02-09-status.md).

### Explicit shop trials (P02-10)

Apply both trial migrations before deploying the writers. The trial flow uses
separate service-delivery credentials, a complete published policy and live
owner consent. Trial status remains observe-only. See
[Shop trial activation](licensing-p02-10-trials.md) for the migration identifiers,
fixture configuration and Chromium checks.

### P02-13 entitlement delivery

Apply both entitlement migrations and runtime grants before enabling local transport. New projections stay observe-only. See [entitlement snapshot delivery](licensing-p02-13-entitlement-delivery.md) and [P02-14 synchronization status](licensing-p02-14-synchronization-status.md) for the UI and API rollout order.

Apply `20261116210000_AddScheduledSubscriptionCancellations` before starting the cancellation-aware licensing API. Upgrade all licensing coverage writers and status readers before accepting cancellations. See [scheduled subscription cancellation](licensing-p02-15-cancellation.md) for deployment order, retained evidence and the local verification entry points.

Apply `20261116220000_AddSubscriptionCancellationWithdrawals` after the cancellation migration, then upgrade every licensing API replica before accepting a withdrawal. See [withdrawing a scheduled cancellation](licensing-p02-16-cancellation-withdrawal.md) for the revision model, deployment order and local verification.

### P02 lifecycle acceptance

Use the [P02 acceptance guide](../tests/licensing/acceptance/p02-lifecycle.md) for the PostgreSQL and shipping Chromium scenarios, P03 synchronization contracts and commercial launch inputs. Keep this phase observe-only.

### Full-payment quote review

Apply `20261116230000_CheckoutPayments` to the licensing database, apply the existing runtime grants, then roll out licensing API, merchant API and merchant web in that order. The merchant Billing page opens the account's checkout review. A live Billing Owner or Manager may request a 15-minute INR quote from a published plan and the account's applied shop affiliations. The review shows the final tax-inclusive amount, named coverage, capacity, intended dates and policy versions.

Acceptance appends a receipt to the frozen quote. The merchant can reload or recover the original operation when a response is lost. Quote acceptance creates no payment attempt and confers no entitlement. Roll back by retiring the quote reviewers and writers while retaining the tables and their facts. The migration refuses a downgrade after the first quote.

See [P03-01 fixture verification](../tests/licensing/acceptance/p03-01-checkout.md) for the browser run.

### Simulator full-payment purchase

Apply `20261117090000_PaymentFulfilment` to the licensing database after `20261116230000_CheckoutPayments`, apply the existing runtime grants, then roll out licensing API, merchant API and merchant web in that order. The migration adds SQL-owned payment attempts, recorded checkout sessions, payment audit events, verified payments, one fulfilment per order with its paid shop links and the entitlement revision each link published, plus the local simulator's own provider-side tables. Every row is retained; `Down` refuses once an attempt exists.

An accepted first-purchase quote (no renewal term, start at fulfilment, no trialled shop) can start one payment attempt inside its 15-minute window. The attempt pins provider, receiving account, environment and configuration before any gateway call; no SQL transaction spans a gateway call. Only a server-side observation of the exact full captured amount, currency, account and order fulfils the order: one paid term for the complete named package, its frozen facts, shop links, an audit event and the entitlement snapshots commit together. A browser success report is recorded as a claim and leaves the payment pending verification. Customer responses carry neutral `payment`, `fulfilment` and `access` states only; provider identifiers and statuses stay inside adapters. Access reads `Applying` until the merchant's signed acknowledgement confirms each shop's fulfilment snapshot.

The only adapter in this slice is the development simulator. It is registered only when `Payments:Mode = LocalSimulator` in a Development or Testing host; otherwise `canStart` is false, starting answers `Billing.PaymentUnavailable`, and the simulator capture route is not mapped (the merchant relay also answers 404 outside Development/Testing). A started simulator attempt keeps its status when it is later served by a host that no longer permits the simulator, but licensing offers no launcher for it, and a merchant host outside Development/Testing strips any simulator launcher before it reaches the browser. A trial delivery re-reads shop coverage after taking the billing-account lock, and `trial_history` refuses a shop with paid coverage, so a trial that loses the race to a paid fulfilment answers `NeedsReview`/`CoverageExists`; a fulfilment that loses to a committed trial keeps the verified payment as `PaidNeedsReview`. No production payment, real provider or enforcement is enabled by this slice. Renewals, trial conversion, recovery of unknown dispatch, webhooks, manual payments and unmatched funds belong to later P03 slices.

See [P03-02 simulator purchase](../tests/licensing/acceptance/p03-02-simulator-purchase.md) for the browser run.

### Early and grace renewals (P03-08)

Apply `20261117120000_PaidRenewals` to the licensing database after `20261117110000_ManualPaymentReceipts`, then roll out licensing API and merchant web. The migration adds nullable renewal provenance to `payment_fulfilments` (`predecessor_term_id`, `payment_effective_at`, `grace_days_included`, `renewal_policy_version`) with one successor per predecessor (`payment_fulfilment_one_successor`), replaces lifetime shop uniqueness of paid links with per-term uniqueness, and extends the quote, attempt, manual receipt, shop link and completeness guards. Existing rows keep null provenance. `Down` refuses (55000) once a renewal has been paid or a renewal attempt or manual receipt exists; rollback otherwise restores the #869 guards and retains every fact.

A paid package (imported or bought through checkout) renews as a whole from its latest paid period only: the quote's `previewStartsAt` is the previous paid-through and `previewEndsAt` the next boundary of the original anchor (31 January → 28 February → 31 March; 29 February clips only in common years). A renewal may be accepted or started only while `now < previous paid-through + 7 days`, independently of the 15-minute quote lifetime; proof of a payment made inside that window may arrive later and is judged against the accepted quote and its verified effective time (online capture time, manual `receivedAt`), never the observation time. The fulfilment records the grace days actually included. All renewals run under the billing-account lock; a superseded predecessor can never be quoted or extended again, and a losing paid order is kept as `PaidNeedsReview`/manual review evidence. Shop status projects a contiguous renewed chain as one continuous paid interval with grace only after its last end. Restored-access starts and trial conversion stay unsupported (#872). Simulator evidence only; no real provider is enabled.
