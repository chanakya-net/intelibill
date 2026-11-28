# Licensing Service Architecture

Scope: the licensing backend in `src/backend/licensing/` only. Other projects are linked, not described:
[merchant backend](../../intelibill/architecture_doc/architecture.md),
[shared libraries](../../shared/architecture_doc/architecture.md),
[admin web](../../../frontend/licensing/architecture_doc/architecture.md),
[infrastructure](../../../../.tofu/architecture_doc/architecture.md).
Setup, configuration keys and operator procedures are in the [README](../README.md).

## Purpose

The licensing service is a separately deployed ASP.NET Core API with its own PostgreSQL database. It owns:

- provider (InteliBill staff) administrator identities, MFA, sessions, capabilities and audit;
- billing accounts, their members, and the shops affiliated to them;
- the plan catalogue and trial policy (draft, publish, retire);
- subscription terms: trials, complimentary grants, paid periods, capacity changes, cancellation,
  suspension, closure and reopening;
- checkout orders, payments (gateway and manual), refunds and billing documents;
- the entitlement snapshots delivered to the merchant API, and the merchant's signed replies.

It has three callers: the admin web (browser, `/api/admin/**`), the merchant API (service to service,
`/internal/licensing/v1/**`), and payment gateways (`/api/payments/webhooks/{provider}`). Merchant end users
never call it directly.

## Stack

| Concern | Choice |
| --- | --- |
| Runtime | .NET 10 (`net10.0`), ASP.NET Core minimal APIs (no MVC controllers) |
| Persistence | PostgreSQL via Npgsql, EF Core with `EFCore.NamingConventions` (snake_case) |
| Results and validation | `ErrorOr`, `FluentValidation` |
| Logging and tracing | Serilog (compact JSON console behind the shared redactor), OpenTelemetry ASP.NET Core tracing with `SafeSpanProcessor`; no exporter is configured |
| Tokens and keys | `System.IdentityModel.Tokens.Jwt`, ASP.NET Data Protection, `Azure.Identity` + `Azure.Security.KeyVault.Keys` |
| Mail | MailKit (administrator invitations) |
| Packages | Central Package Management from the root `Directory.Packages.props` |

Wolverine is not used here: handlers are plain classes registered in DI and invoked from endpoint delegates.

## Solution Layout

`Intelibill.Licensing.slnx` contains the four projects below, the three shared libraries and
`tests/backend/licensing/integration`. The repository-root `Intelibill.slnx` also includes them.

| Project | References | Responsibility |
| --- | --- | --- |
| `Intelibill.Licensing.Domain` | none | Entities (`Entities/`), pure policies (`Policies/`: billing calendar, proration, grace, renewal, trial extension, checkout and capacity quotes, complimentary access, subscription access), `ValueObjects/SubscriptionAccessFacts` |
| `Intelibill.Licensing.Application` | Domain, `Shared.Contracts` | Handlers, validators, store and gateway ports (`I*Store`, `IPaymentGateway`, `IEntitlementPublisher`), error definitions, one folder per functional area |
| `Intelibill.Licensing.Infrastructure` | Domain, Application, `Shared.Web`, `Shared.Security` | `LicensingDbContext`, migrations, PostgreSQL stores, payment gateway adapters, entitlement transport and delivery, MFA key ring, SMTP mailer, background workers |
| `Intelibill.Licensing.Api` | Application, Infrastructure, `Shared.Web` | `Program.cs`, endpoint mapping, authentication schemes and policies, request gates, the `bootstrap-admin` CLI command, observability setup |

Registration entry points: `AddLicensingApplication` (`Application/DependencyInjection.cs`) and
`AddLicensingInfrastructure` (`Infrastructure/DependencyInjection.cs`), which chains the per-area
`*Registration.cs` files (`BillingDelegationRegistration`, `SubscriptionStatusRegistration`,
`CheckoutRegistration`, `TrialRegistration`, `EntitlementDeliveryRegistration`,
`ComplimentaryGrantRegistration`). API-side handlers and policies are registered in
`Api/AdminIdentities/ProviderSessions.cs` and `Api/Bootstrap/BootstrapAuthorization.cs`.

Api folders mirror the functional areas: `AdminIdentities`, `Audit`, `Billing`, `Bootstrap`, `Catalogue`,
`Coverage`, `Documents`, `Entitlements`, `Grants`, `Payments`, `Refunds`, `Restrictions`, `Status`,
`Subscriptions`, `Trials`, `Observability`.

## Request Flow

`Program.Main` runs either the `bootstrap-admin` CLI command (a plain generic host, no web server) or the
web host. A host failure prints only the exception type and exits 1.

Pipeline order in `Program.BuildApplication`:

1. `ExceptionHandlingMiddleware` (shared safe error envelope), registered first so probes are covered.
2. `/health/live` and `/health/ready` (anonymous, `Cache-Control: no-store`).
3. `UseRouting`, then `AdminRequestBoundary`: for `/api/admin/**` it sets `no-store`, answers 403
   `Admin.AccessDenied` for any endpoint that carries neither authorization metadata nor `AllowAnonymous`,
   and turns database failures into 503 `Bootstrap.StorageUnavailable`.
4. `UseAuthentication`, `UseAuthorization`, then the endpoint groups.

An endpoint delegate reads the authenticated caller, calls an Application handler, and maps `ErrorOr`
errors to `application/problem+json`. Handlers call store ports; Infrastructure stores open the
transaction, set transaction-local `app.*` settings that row-level security policies read, and run SQL.

### Surfaces and authentication

| Surface | Prefix | Authentication |
| --- | --- | --- |
| Provider console | `/api/admin/**` | Scheme `ProviderSession`: an HS256 access JWT (issuer `InteliBill.Licensing.Admin`, carries `sub` and `sid`) in a host-only cookie, re-checked against the persisted session on each request. `BootstrapEnrollment` (enrollment cookie) covers pending MFA enrollment. `ProviderLocalFixture` exists for local invitation fixtures and is refused at startup outside Development/Testing |
| Merchant delegation | `/internal/licensing/v1/customer/**` | Scheme `MerchantBillingDelegation`: an RS256 bearer assertion signed by the merchant API (see Merchant Integration) |
| Merchant events | `/internal/licensing/v1/merchant-events/**` and the other receipt paths in `EntitlementIntegrationEndpoints` | Endpoints are `AllowAnonymous`; each verifies a signed service assertion over path and body before any work |
| Gateway webhooks | `/api/payments/webhooks/{provider}` | `AllowAnonymous`; the provider adapter validates the signature. Body limit 64 KiB (413 above it); a disabled receipt answers 404, a rejected signature 401 |
| Probes | `/health/live`, `/health/ready` | Anonymous |

Admin authorization policies (`ProviderSessions`) all require the `provider_business_session` claim; some
add a capability claim. The default policy equals `ProviderBusinessAccess`, so a bare `[Authorize]` cannot
be weaker than the business routes. Policy claims are necessary but not sufficient: handlers re-check the
persisted administrator, grant and (where required) recent MFA inside the transaction (`ProviderActor`).

## Functional Areas

### Administrator identity (`AdminIdentities`, `Bootstrap`)

- **First owner**: `bootstrap-admin prepare` (CLI, run with the migrator login) attests a verified email
  and emits a one-time setup proof; `/api/admin/auth/bootstrap` consumes it and creates the owner.
- **Invitations**: a platform owner invites further administrators by email; there is no public sign-up.
- **MFA**: TOTP enrollment with one-use recovery codes. Secrets are protected by the `AdminMfa` key ring
  (`AdminMfaKeys`); an invalid ring fails startup.
- **Sign-in and sessions**: password plus TOTP; `AdminSession` lifetimes are 15 minutes access, 30 minutes
  idle, 12 hours absolute. Refresh credentials rotate per use; reuse revokes the session.
  `/api/admin/sessions` lists and revokes device sessions; `/api/admin/auth/sign-out` ends the current one.
- **Recovery**: anonymous `/api/admin/auth/recovery/{code,reset,continue}` plus a reviewed reset at
  `/api/admin/recovery/resets` behind the `ProviderRecoveryReview` policy
  ([licensing-admin-recovery](../../../../docs/licensing-admin-recovery.md)).
- **Capabilities**: `AdminCapability` flags `Support`, `Billing`, `LicensingManager`,
  `SecurityAdministrator`, `PlatformOwner`, managed through `/api/admin/users`. `PlatformOwner` additionally
  yields the `admin.access.manage`, `admin.invitations.manage`, `admin.audit.identity.read` and
  `admin.recovery.review` claims. Sensitive writes require MFA verified within five minutes.
- **Request gates**: state-changing auth routes require the exact provider origin and a CSRF proof
  (`X-Bootstrap-CSRF` or `X-Admin-CSRF`), plus PostgreSQL-backed rate budgets shared across replicas.

### Audit (`Audit`)

`provider_audit_events` records identity and commercial actions (`AdminAuditActions`,
`AuditEvidenceFactory`). A database trigger (`provider_audit_immutable`) refuses update, delete and
truncate. `GET /api/admin/audit` needs the `admin.audit.identity.read` capability.
`AdminAuditAccessDeniedHandler` records a denial event when a signed-in administrator is forbidden on an
endpoint marked with `AuditDenial` metadata.

### Billing accounts and coverage (`Billing`, `BillingAccounts`, `Coverage`)

Billing accounts with `Owner` and `Manager` members, manager invitations, manager removal and ownership
transfer. Shops are affiliated to an account after the shop owner's decision arrives from the merchant
API. Coverage operations assign unused slots, schedule replacements, request and review replacement
exceptions, and transfer coverage between accounts (customer request, shop consents, provider approval).

### Catalogue (`Catalogue`)

Plans, plan versions and trial policy versions are drafted, published and retired by a Licensing Manager.
Operations are keyed by an operation key and can be re-read (`/api/admin/catalogue/operations/{key}`,
`/catalogue/history`). The merchant side reads the published catalogue through the delegated surface.

### Trials, grants and status (`Trials`, `Grants`, `Status`)

- Shop trials start from the merchant side (owner consent delivery); provider trial extensions go through
  `/api/admin/accounts/{account}/…` with preview and operation-key recovery.
- Complimentary grants are provider-issued access (preview, create, recover by operation key).
- Status: `SubscriptionAccessPolicy` projects `SubscriptionAccessFacts` (basis `Paid`, `Trial`,
  `Complimentary`, `Temporary`, `Transition`) to a `SubscriptionAccessState` (`PendingActivation`,
  `Scheduled`, `Active`, `Grace`, `Expired`, `Suspended`, `EvidenceIncomplete`, `NeedsReview`). Status
  evaluation reads a keyed clock (`subscription-status`) once per response; only test hosts replace it.

### Subscriptions and restrictions (`Subscriptions`, `Changes`, `Restrictions`)

Capacity change quotes and paid capacity increases, scheduled capacity reductions, scheduled cancellation
and its withdrawal, provider suspensions and releases, shop and account closure proposals with owner
consent, and reviewed reopening.

### Payments, refunds and documents (`Payments`, `Refunds`, `Documents`)

- **Checkout**: quote-based orders (`CheckoutQuotePolicy`, 15-minute quote lifetime), acceptance, then a
  payment attempt pinned to one provider configuration.
- **Gateways**: `IPaymentGateway` is the provider-neutral port. Adapters: `RazorpayGateway`
  (`Payments/Razorpay`) and `DevelopmentPaymentGateway` (`Payments/Development`, Development/Testing
  only). No SQL transaction is held across a gateway call.
- **Reconciliation**: webhooks are stored in a gateway inbox and processed by `GatewayInboxWorker`;
  `PendingPaymentAttemptWorker` claims due attempts under leases and reads the provider;
  `RefundAttemptWorker` pumps refund attempts. All three are hosted services on a 2-second timer and do
  nothing when `Payments:ReconcileInBackground=false`; `RefundAttemptWorker` additionally runs only in
  Development/Testing.
- **Manual payments**: provider-confirmed receipts with protected evidence; unmatched payments go to a
  review queue; immutable payment confirmations are readable by both sides.
- **Refunds**: customer refund requests and provider approval, sent once through the pinned gateway.
- **Billing documents**: PDF upload, validation (`Application/Documents/Pdf*.cs`), supersession and
  download.

### Offline device observations (`Entitlements`, `Coverage/DevicePermitStatus.cs`)

Licensing does not issue offline permits. It receives signed `OfflineDeviceObservationV1` events from the
merchant API, stores them in `offline_device_observations`, and exposes a provider read of device contact
and outstanding permits (`GET /api/admin/accounts/{account}/device-permits`). Coverage replacement and
transfer logic also reads these observations.

## Payment Provider Resolution

The licensing resolver's synchronous `ForPin` port calls `PaymentProviderBindings.Find` for
immutable credential-reference metadata. Its first lookup per pin per dependency-injection
scope performs a synchronous PostgreSQL `SingleOrDefault()` and blocks the calling thread,
including request and worker threads. A scoped cache includes missing results, so repeated
lookups of that pin in the same scope do not repeat the read; retained profile configuration
is bounded to 32 entries. Binding writes remain asynchronous in the authorized transaction.

Follow-up: when retiring the legacy synchronous `ForNewAttempts` bootstrap seam, make the
resolver and binding reads asynchronous end to end, preserving exact historical pins and
fail-closed routing. This slice retains the existing port and adds no refund workflow.

Supporting facts from `PaymentGatewayResolver`, `PaymentProviderConfiguration` and `PaymentProviderBindings`:

- A `PaymentProviderPin` is (provider id, account id, environment, configuration version). An attempt is
  served only by the adapter matching its exact pin.
- Profiles come from `Payments:Profiles` and hold references to credential configuration, never secret
  values. `payment_provider_bindings` stores pin, profile id and references; a profile whose references
  differ from the stored binding fails with `ConfigurationReused`.
- `ForNewAttempts` selects Razorpay when `Payments:Mode=Razorpay`, the simulator when
  `Payments:Mode=LocalSimulator` in Development/Testing, otherwise none.
- The simulator is refused outside Development/Testing on every path.
- `/api/admin/payment-providers` lists profile status and records the provider selection for new attempts.

## Persistence

- **Context**: `LicensingDbContext` (`Infrastructure/Data`), snake_case naming, one pooled
  `NpgsqlDataSource` built from `LicensingDatabaseOptions` (section `LicensingDatabase`).
- **Mappings**: `IEntityTypeConfiguration<T>` classes in `Data/Configurations` and beside their stores
  (`*Mapping(s).cs`). The EF model covers identity, audit, billing accounts and catalogue tables. Most
  other stores use interpolated SQL through `db.Database` against tables and SQL functions that exist only
  in the baseline script.
- **Migrations**: one migration, `20261127074702_Baseline`, runs the embedded `pg_dump` script
  `Migrations/20261127074702_Baseline.sql` (tables, functions, triggers, row-level security policies,
  privileges, seed rows). Never edit the script; add new migrations after it. `Down` throws: recreate the
  database instead of reverting.
- **The API never migrates.** `LicensingDbContextFactory` (design time, bundle and the `bootstrap-admin`
  CLI) reads `LicensingMigrationDatabase__*` from environment variables. `Dockerfile.migrate` builds a
  self-contained EF migration bundle image.
- **Roles**: the migrator login owns the database and schema; the API uses a separate restricted runtime
  login. `/health/ready` runs `LicensingRuntimeRolePolicy.Query` and reports ready only when the session
  role is not superuser, has no `BYPASSRLS`, `CREATEDB`, `CREATEROLE` or `REPLICATION`, holds no predefined
  `pg_*` role, does not own the database or objects in it, has no `CREATE` on the database or any schema,
  cannot create temporary tables, and has no admin option on any role. Local role setup is `scripts/db/local-licensing-role.sql`.
- **Row-level security**: policies read transaction-local settings such as `app.billing_account_id`,
  `app.billing_actor_id` and `app.entitlement_account_id`, set by the stores with `set_config(…, true)` so
  commit or rollback clears them before the pooled connection is reused.
- **Lock timeouts**: `AddDatabaseLockTimeouts` from `Shared.Web` (section `DatabaseLocks`).
- **Outbox**: outbound merchant deliveries use the shared leased HTTP outbox (`LeasedHttpOutbox`,
  `LeasedHttpDeliveryWorker`) on the licensing database.

## Merchant Integration

All contracts (paths, limits, DTOs, JSON options) are the `V1` types in `Intelibill.Shared.Contracts`.
The two services share no database. Three signed channels exist:

| Direction | Mechanism | Licensing side |
| --- | --- | --- |
| Merchant to licensing, on behalf of a user | Billing delegation assertion (`MerchantBillingDelegationV1`): RS256 JWT, issuer `InteliBill.Merchant.Billing`, audience `InteliBill.Licensing.Customer`, lifetime at most 60 seconds | `MerchantBillingDelegationVerifier` checks exact header and payload fields against `MerchantBillingVerification:PublicKeys`. The endpoint requires the assertion's action, method, path, `X-Billing-Request-Id` and body SHA-256 to match the request. `PostgresBillingDelegationAccess` then authorizes membership and records the `jti` in `merchant_billing_delegation_uses` inside the operation's transaction, so an assertion is used once |
| Merchant to licensing, service events | `SignedServiceAssertion.MerchantEvents` (RS256, 60 seconds) over path and body | `EntitlementIntegrationEndpoints` + `EntitlementMerchantEvents`: snapshot acknowledgement and reconciliation, capacity and reduction receipts, coverage slot, replacement, exception and transfer consents and receipts, offline device observations |
| Licensing to merchant | `SignedServiceAssertion.LicensingDelivery`, signed with an Azure Key Vault key through managed identity, or a local key in Development/Testing | `EntitlementPublisher` writes snapshots and commands to the outbox; `LicensingEntitlementDelivery` posts them to the configured merchant origin (section `EntitlementDelivery`) and validates the acknowledgement digest. `QueueDueAsync` also publishes entitlements due for re-verification and due capacity reductions |

Additional points:

- Shop affiliation decisions and shop trial consents arrive on their own delivery paths with separate key
  sections (`ShopAffiliationDelivery`, `ShopTrialDelivery`). `LicensingEntitlementTransport` requires the
  entitlement keys to be distinct from the keys of the other purposes.
- Synchronous outbound checks (for example `MerchantCoverageTransferAuthorityClient`) use a signed request
  and a signed reply with a short timeout, hold no SQL transaction across the call, and treat any fault as
  unavailable.
- When no merchant origin or signing key is configured, delivery is disabled rather than attempted.

Merchant-side enforcement of entitlements is described in the
[merchant backend doc](../../intelibill/architecture_doc/architecture.md).

## Shared Libraries

Details: [shared libraries doc](../../shared/architecture_doc/architecture.md).

| Library | Used here for |
| --- | --- |
| `Intelibill.Shared.Contracts` | `V1` wire contracts with the merchant API, `OpaqueTokenV1` |
| `Intelibill.Shared.Security` | `BcryptPasswordHashing`, `OpaqueTokenPrimitives`, `SignedServiceAssertion`, `EntitlementTransportOptions` |
| `Intelibill.Shared.Web` | `ExceptionHandlingMiddleware`, `AddSafeErrorHandling`, `LogEventRedactor` and redacted sinks, `SafeDiagnosticWriter`, `SafeSpanProcessor`, `PrivateIntegrationRequest`, leased HTTP outbox and worker, database lock timeouts |

## Security Invariants

- Separate database, logins and signing keys from the merchant API; the runtime login holds no schema or
  role authority and readiness enforces it.
- No public sign-up. Administrators exist only through the bootstrap proof or an owner invitation, and
  hold no business session until MFA enrollment completes.
- Credentials at rest: passwords as bcrypt hashes; setup proofs, enrollment, invitation and refresh
  credentials as digests.
- Session and capability claims are re-validated against PostgreSQL on every request and again inside
  write transactions.
- Every `/api/admin` endpoint must declare authorization or `AllowAnonymous`; otherwise the boundary
  answers 403. Admin and delegated responses are `no-store`.
- Service-to-service requests are bound to path and body digest, short-lived, and single-use where they
  authorize a user action. Verification failures answer 401.
- Payment secrets never enter the database or the routing API; only configuration references do. The
  payment simulator, local mail capture, admin fixtures and loopback HTTP are limited to Development and
  Testing.
- Dependency failures fail closed: storage faults answer 503, missing keys disable the affected feature
  (sign-in without `AdminSessions:SigningKey`, MFA without its key ring, delivery without a signing route).
- Logs pass through the shared redactor with PII masking on by default; spans drop exception text. Do not
  log exception messages, tokens, links or DTOs by hand.
- `provider_audit_events` is append-only at the database level.

## Tests

| Location | Content |
| --- | --- |
| `tests/backend/licensing/integration/` | xUnit project `Intelibill.Licensing.Integration.Tests`. Classes are named by slice (`P0106Tests`, `P0212Tests`, …) and split into partials by concern. It references both the licensing and merchant APIs and runs real hosts (`LicensingApiFactory`, `MerchantStartupProbeFactory`) against Testcontainers PostgreSQL (`LicensingPostgreSqlFixture`), with `MailpitFixture` and `FakeRazorpay` as stand-ins. `*BrowserTests.cs` classes launch the Playwright specs with `bun x playwright test` |
| `tests/backend/licensing/browser/` | Playwright specs and configs per slice (`pNN-NN-*.spec.ts`, `playwright.pNNNN.config.ts`) |
| `tests/backend/licensing/acceptance/` | Acceptance criteria per slice (`*.md`), `run-p01NN-browser.sh` runners, `p01-foundation-harness.test.sh`, `p04-enforcement-inventory.tsv` |
| `tests/contracts/licensing/` | Versioned JSON contract fixtures shared with the merchant side (`*-v1.json`) and scenario notes |

Run with `scripts/dev/test-integration.sh --licensing [--filter "..."]` from the repository root (Docker
required). Run targeted filters for the touched area first; run the whole suite only when shared database,
entitlement or delegation seams change. Parallelism and test-host differences are in
[licensing-rollout](../../../../docs/licensing-rollout.md#backend-integration-tests).

## Related Notes

| Document | Topic |
| --- | --- |
| [licensing-rollout](../../../../docs/licensing-rollout.md) | Local stack, ports, databases and identities, configuration keys, CI, per-slice rollout notes |
| [licensing-admin-recovery](../../../../docs/licensing-admin-recovery.md) | Administrator recovery |
| [licensing-p02-05-affiliation](../../../../docs/licensing-p02-05-affiliation.md) | Shop affiliation |
| [licensing-p02-09-status](../../../../docs/licensing-p02-09-status.md) | Access status and deadlines |
| [licensing-p02-10-trials](../../../../docs/licensing-p02-10-trials.md) | Shop trials |
| [licensing-p02-11-trial-extensions](../../../../docs/licensing-p02-11-trial-extensions.md) | Trial extensions |
| [licensing-p02-12-grants](../../../../docs/licensing-p02-12-grants.md) | Complimentary grants |
| [licensing-p02-13-entitlement-delivery](../../../../docs/licensing-p02-13-entitlement-delivery.md) | Entitlement delivery |
| [licensing-p02-14-synchronization-status](../../../../docs/licensing-p02-14-synchronization-status.md) | Synchronization status |
| [licensing-p02-15-cancellation](../../../../docs/licensing-p02-15-cancellation.md) | Scheduled cancellation |
| [licensing-p02-16-cancellation-withdrawal](../../../../docs/licensing-p02-16-cancellation-withdrawal.md) | Cancellation withdrawal |

## Licence notice planning (P07-01)

Trial reminders fall three days and one day before expiry. Paid reminders fall seven days and one day before expiry. Eligible grace periods add start, end-minus-one-day and expiry notices. Shop scheduling and reads use the intersection of the term and the shop’s coverage interval, including mid-term joins and handoffs.

The worker scans eligible term IDs before loading facts or requesting rosters. After signed merchant HTTP observations, it locks the billing account and recipient authorities, serializes notice additions per account, and checks the observed source IDs at the current decision time. A deferred account is counted and retried on a later pass while other accounts advance.

Forced RLS, immutable job rows and recipient identity and destination aliases preserve one notice per recipient and threshold. A later verified channel is appended to `notification_channel_additions`. The security-invoker view `effective_notification_jobs` projects both channels through the underlying table policies. Worker discovery requires the dedicated transaction-local planner scope.

Merchant reads return a current notice per live source. The next refresh includes end-exclusive deadlines, and live restriction guidance changes on release. Provider preview retains the scheduled facts and job identifiers across those boundaries.

### Sending an approved notice

The licensing database holds the protected mailbox on the original job or its immutable channel addition.
The email ledger references that job or an independently issued contact proof. Both inserts share their source transaction.
The worker claims due work using a restricted database function with row locking and `SKIP LOCKED`.
Before submission it verifies the bound mailbox against current role, contact or shop authority.
A contact on the same account can still receive a contact-safe version after losing a billing role.
The signed merchant observation and the licensing transaction have separate boundaries.

A reminder worker pins a PostgreSQL connection and acquires the shared billing-account advisory lock after connecting to SMTP.
Local billing mutations take the exclusive side of the same lock. Inside a short transaction the worker re-reads recipient
authority, scope, semantic term revision and the threshold window. It renders the eligible audience's current guidance
and commits either suppression or `Sending` against the lease token. At SMTP handoff it checks the signed observation,
business window and sending lease again. The shared lock spans the bounded submission and is released before outcome recording.
External mailbox authority comes from the latest signed merchant observation on each attempt.
A positive final reply records `SmtpAccepted`. An ambiguous reply or expired sending lease records `Uncertain` and stops automatic retry.
Pre-submission failures and explicit transient rejections can retry after 30, 60, 120 and 240 seconds.
The fifth failure exhausts the budget. Append-only events preserve attempts and timestamps.
The suppression event preserves the decision reason even if the agreement changes again.
The provider projection joins that event by account, delivery and attempt and exposes only the reason codes in the V1 contract.

Use the same protected licensing key ring across worker replicas and restarts.
Local capture has no SMTP credentials. A future live rollout requires independently verified sender and support contacts,
authenticated TLS, external secret custody and explicit authorization.
