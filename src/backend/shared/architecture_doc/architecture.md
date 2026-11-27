# Shared Backend Libraries — Architecture

Scope: `src/backend/shared/` only. The consumers are described in their own documents:
[merchant backend](../../intelibill/architecture_doc/architecture.md) and
[licensing service](../../licensing/architecture_doc/architecture.md).

## Dependency rules

```
Intelibill.Shared.Contracts   (no project references, no packages)
        ^
Intelibill.Shared.Security    (Contracts + BCrypt.Net-Next; no FrameworkReference)

Intelibill.Shared.Web         (no project references; Microsoft.AspNetCore.App, EF Core,
                               FluentValidation, Serilog, Serilog.Sinks.Async, OpenTelemetry)
```

- No shared project references a merchant or licensing project. `Shared.Web` references no
  `Intelibill.*` assembly at all, including the other two shared libraries.
- `Contracts` and `Security` stay framework-neutral: no reference to `Microsoft.AspNetCore*` or
  `Microsoft.EntityFrameworkCore*` assemblies.
- These rules are enforced by tests, not convention (see [Testing](#testing)).

Consumer project references:

| Consumer project | Contracts | Security | Web |
|---|---|---|---|
| `Intelibill.Application` (merchant) | yes | yes | |
| `Intelibill.Infrastructure` (merchant) | | yes | yes |
| `Intelibill.Api` (merchant) | | | yes |
| `Intelibill.Licensing.Application` | yes | | |
| `Intelibill.Licensing.Infrastructure` | | yes | yes |
| `Intelibill.Licensing.Api` | | | yes |

Neither consumer's Domain project references a shared library.

## Intelibill.Shared.Contracts

Namespace `Intelibill.Shared.Contracts.V1`. One file per integration area (entitlement snapshots,
merchant billing delegation, checkout, capacity changes, coverage transfers/replacements, refunds,
shop trials/closures, billing documents, and so on). Each area typically has:

- a static `*V1` class with the constants both sides must agree on: internal paths, body limits,
  action names, schema/policy versions, and pure validation helpers
  (for example `EntitlementSnapshotsV1.SnapshotPath`, `BodyLimit`, `IsInstant`, `IsDigest`;
  `MerchantBillingDelegationV1.IsKeyId`, `NormalizeAccountName`);
- sealed `record` DTOs suffixed `V1`, some with `IsValid()` self-checks.

`EntitlementSnapshotsV1.Json` is the strict serializer configuration for the entitlement transport:
web defaults, case-sensitive names, duplicate and unmapped members disallowed, required constructor
parameters and nullable annotations respected, `MaxDepth = 8`.

Records that carry credentials or personal details override `ToString()` so the default record
rendering cannot leak them (`OpaqueTokenV1` returns its type name; several billing records return
`[REDACTED]`).

## Intelibill.Shared.Security

| Type | Responsibility |
|---|---|
| `IPasswordHashing` / `BcryptPasswordHashing` | BCrypt hash (work factor 12) and verify. |
| `OpaqueTokenPrimitives` | `NewToken()` = 64 random bytes, Base64Url; `Hash()` = upper-case hex SHA-256 of the token's UTF-8 bytes; `Create()` returns both as `OpaqueTokenV1`. |
| `SignedServiceAssertion` | Signs and verifies the RS256 assertion used on service-to-service entitlement calls. Two fixed purposes: `LicensingDelivery` (licensing to merchant) and `MerchantEvents` (merchant to licensing). |
| `ServiceAssertionPurpose`, `VerifiedServiceAssertion` | Type/issuer/audience triple; the verified result (replay id, key id, public key, times, body digest). |
| `EntitlementTransportOptions` | Bound from configuration section `EntitlementDelivery` (`EntitlementSnapshotsV1.Section`): peer origin, local key, public and retired key sets; helpers `Origin`, `VerificationKey`, `CanSignLocally`, `TrySignLocalAsync`, `IsDistinct`. |

## Intelibill.Shared.Web

| Folder | Types | Responsibility |
|---|---|---|
| `Logging/` | `LogEventRedactor`, `RedactedSinkExtensions` (`Redacted`, `WriteToRedactedSinks`), internal `RedactingSink`, internal `SafeExceptionDescription` | Rewrite every Serilog event before any sink sees it. |
| `Logging/` | `ISafeDiagnosticWriter` / `SafeDiagnosticWriter`, `SafeDiagnosticContext` | Record a request failure from allowlisted facts only. |
| `PublicErrors/` | `ISafeErrorFactory` / `SafeErrorFactory`, `PublicError`, `PublicFieldError`, `SafeTraceId` | Map an exception to the public error; resolve a log-safe trace id. |
| `Middleware/` | `ExceptionHandlingMiddleware`, `CallerDisconnect` | Catch unhandled exceptions and answer with the problem envelope. |
| `Observability/` | `SafeSpanProcessor` | Strip exception text from spans before export. |
| `Http/` | `PrivateIntegrationRequest` | Strict reader for internal service-to-service POSTs. |
| `Persistence/` | `DatabaseLockOptions`, `LockWait`, `DatabaseLockTimeouts` | PostgreSQL `lock_timeout` budgets. |
| `Delivery/` | `LeasedHttpOutbox`, `LeasedHttpMessage`, `ILeasedHttpDestination`, `LeasedHttpDeliveryPump`, `LeasedHttpDeliveryWorker`, `LeasedHttpOutboxSchema` | Leased outbox for entitlement messages between the services. |
| root | `DependencyInjection.AddSafeErrorHandling` | Registers `ISafeErrorFactory` and `ISafeDiagnosticWriter` as singletons. |

### Error path

`ExceptionHandlingMiddleware` handles three cases:

1. Caller disconnected (`CallerDisconnect.Explains`: request aborted and the exception is a
   cancellation or `IOException`): status 499 if the response has not started; no diagnostic, no body.
2. Response already started: the diagnostic is written and the connection is aborted, so a partial
   body is never followed by a problem document.
3. Otherwise: `application/problem+json` with `{type,title,status,code,traceId,errors?}`.

`SafeErrorFactory` mapping (the only codes this library emits):

| Exception | Status | Code |
|---|---|---|
| FluentValidation `ValidationException` | 400 | `Validation.Failed` (with field errors) |
| `BadHttpRequestException` | its own status | `Request.Invalid` |
| `ArgumentException` | 400 | `Request.Invalid` |
| `DbUpdateConcurrencyException` | 409 | `Concurrency.Conflict` |
| `UnauthorizedAccessException` | 401 | `Auth.Unauthorized` |
| anything else | 500 | `Internal.Unexpected` |

Status 500 and above is logged through `ISafeDiagnosticWriter.Unexpected` (Error), everything else
through `Rejected` (Warning).

### Delivery

`LeasedHttpDeliveryPump.Register` adds the scoped pump, the hosted `LeasedHttpDeliveryWorker`
(2-second tick, up to 10 deliveries per tick) and the named `HttpClient` `EntitlementDelivery`
(no redirects, cookies, proxy, client logging or trace-header propagation). The consumer supplies the
scoped `LeasedHttpOutbox` over its own `DbContext` and an `ILeasedHttpDestination`.

`LeasedHttpOutbox` writes `public.entitlement_messages` / `public.entitlement_outbox` and calls the
SQL functions `public.claim_entitlement_delivery` and `public.finish_entitlement_delivery`; no
database transaction spans the HTTP call (claim with a lease, send with a 10-second timeout, finish).
Those tables and functions are owned by each consumer's migrations. `LeasedHttpOutboxSchema` holds
DDL text for them but is not referenced by any consumer code at present; the live definitions are
in each consumer's `Baseline` migration script.

## Consumer wiring

Both hosts wire the same pieces; file locations differ.

| Piece | Merchant backend | Licensing service |
|---|---|---|
| `AddSafeErrorHandling()` + `UseMiddleware<ExceptionHandlingMiddleware>()` | `Intelibill.Api/Program.cs` | `Intelibill.Licensing.Api/Program.cs` |
| `new LogEventRedactor(maskPersonalData: logging.EnablePiiMasking)` + `WriteToRedactedSinks` | `Intelibill.Api/Extensions/SerilogExtensions.cs` | `Intelibill.Licensing.Api/Observability/LicensingObservabilityExtensions.cs` |
| `AddProcessor(new SafeSpanProcessor())` | `Intelibill.Infrastructure/Extensions/OpenTelemetryServiceCollectionExtensions.cs` | `LicensingObservabilityExtensions.cs` |
| `AddDatabaseLockTimeouts`, `IPasswordHashing` -> `BcryptPasswordHashing`, `OpaqueTokenPrimitives` (singletons) | `Intelibill.Infrastructure/DependencyInjection.cs` | `Intelibill.Licensing.Infrastructure/DependencyInjection.cs` |
| `EntitlementTransportOptions` binding, `LeasedHttpOutbox`, `ILeasedHttpDestination`, `LeasedHttpDeliveryPump.Register` | `Intelibill.Infrastructure/Billing/MerchantBillingRegistration.cs` (`MerchantEntitlementDelivery`) | `Intelibill.Licensing.Infrastructure/Entitlements/EntitlementDeliveryRegistration.cs` (`LicensingEntitlementDelivery`) |
| `SignedServiceAssertion.SignAsync` / `Verify` | `Intelibill.Infrastructure/Licensing/MerchantEntitlementTransport.cs` (signs `MerchantEvents`, verifies `LicensingDelivery`) | `Intelibill.Licensing.Infrastructure/Entitlements/LicensingEntitlementTransport.cs` (signs `LicensingDelivery`, verifies `MerchantEvents`) |
| `PrivateIntegrationRequest.ReadAsync` | `Intelibill.Api/Internal/EntitlementEndpoints.cs` | `Intelibill.Licensing.Api/Entitlements/EntitlementIntegrationEndpoints.cs` |

Paths are relative to `src/backend/intelibill/` and `src/backend/licensing/`.

## Security invariants

Logging and diagnostics:

- `LogEventRedactor` is a backstop, not the boundary: callers must still not log secrets. It
  replaces the event's exception with `ExceptionTypes` / `ExceptionFrames` (plus `SqlState`,
  `ProviderStatus` when present), masks credential-named properties at every depth
  (`***MASKED***`), scrubs `name=value` secrets, `Bearer`/`Basic` credentials and exception
  `ToString()` text from strings and templates, and bounds depth (8), elements (100) and string
  length (4096).
- Credential masking is unconditional. Personal data (email, phone and similar names, and email
  addresses inside strings) is masked only when the host passes `maskPersonalData: true`.
- `WriteToRedactedSinks` puts both the host's exporters and any container-registered
  `ILogEventSink` behind the redactor; nothing is written beside them.
- `SafeExceptionDescription` never reads an exception's message, `Data` or provider payload: only
  type names, `Type.Method` frames without file or line, a five-character SQLSTATE, and an HTTP
  status. `SafeDiagnosticWriter` never passes the exception object to the logger.
- `SafeDiagnosticContext` records the route template (or `<unmatched>`), never the raw path.
- `SafeTraceId` uses the W3C trace id, else the request identifier only if it matches
  `^[A-Za-z0-9:._-]{1,64}$`, else a generated id, so a caller cannot inject text into a log line.
- `SafeSpanProcessor` clears an error span's status description and the `exception.message` /
  `exception.stacktrace` tags; error status is kept.

Public errors:

- `PublicError` never carries exception text, type or stack. Validation messages are the one
  exception, and a field message that echoes the attempted value (4 or more characters) is
  replaced by a generic message.

Service-to-service transport:

- `SignedServiceAssertion.Verify` returns `null` for anything unexpected: token over 4096
  characters, not three parts, non-canonical Base64Url, algorithm other than `RS256`, wrong type,
  issuer or audience, method other than `POST`, path or body SHA-256 mismatch, lifetime outside 60
  seconds, unknown key id, or an RSA key outside 2048 to 4096 bits. Replay protection is the
  consumer's job, using the returned `ReplayId`.
- `EntitlementTransportOptions.Origin` accepts only an HTTPS origin with no path, query, fragment
  or user info (loopback HTTP only when the caller passes `local: true`). Retired keys never verify.
  A key reused for another purpose, or as the local private key, is rejected (`IsDistinct`). Local
  PEM signing is allowed only when `local` is true and no Key Vault key is configured. A malformed
  key disables the trust path instead of throwing.
- `PrivateIntegrationRequest.ReadAsync` sets `Cache-Control: no-store` and rejects the request
  unless the path matches exactly (no path base, query string or differing raw target), the content
  type is JSON, exactly one `Authorization: Bearer` header is present, and the body is non-empty and
  within the limit.

Credentials and locks:

- `OpaqueTokenPrimitives.Hash` is the stored form of an opaque credential; the hash format
  (upper-case hex SHA-256 of the UTF-8 token) and the BCrypt format must stay compatible with
  values already stored by either service.
- `DatabaseLockOptions` (section `DatabaseLocks`): `Brief` 3 s, `Standard` 5 s by default,
  validated at startup to lie between 1 ms and 1 minute. `SetLockTimeoutAsync` sets `lock_timeout`
  for the current transaction only; a context built outside a host uses the defaults.

## Testing

Compatibility is tested through the consumers, in `tests/backend/shared/` (both projects start the
real merchant and licensing hosts on Testcontainers PostgreSQL; the host-based classes share one
non-parallel collection, `SharedHostsGroup`):

- `P0102ConsumerRegressionTests` (`Shared.Web`): both APIs answer the identical safe problem
  document, sinks never receive exception text or secrets (including background-worker failures),
  PII masking follows host configuration while credential masking does not depend on it, exported
  spans drop exception text, both hosts resolve the shared error and diagnostic types, and
  `Shared.Web` has no `Intelibill.*` assembly or project reference.
- `P0103ConsumerRegressionTests` (`Shared.Security`, `Contracts`): password and refresh-credential
  compatibility for the merchant, BCrypt edge cases, both hosts resolving `OpaqueTokenPrimitives`,
  the `OpaqueTokenV1` wire shape and redacted `ToString()`, authority isolation between the two
  services, and `BuildBoundaryTests`.

`BuildBoundaryTests` reads the project files and asserts: `Security` references exactly
`Contracts` and exactly one package (`BCrypt.Net-Next`); `Contracts` has no references or packages;
neither has a `FrameworkReference` or an ASP.NET Core / EF Core assembly reference; both
Infrastructure projects reference `Security`; and the merchant `Dockerfile` and
`Dockerfile.migrate` copy the `Security` and `Contracts` project files before `dotnet restore`.

How to run them is in the [README](../README.md).
