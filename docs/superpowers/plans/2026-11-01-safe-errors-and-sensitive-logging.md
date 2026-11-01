# Safe errors and sensitive logging — implementation plan

**Goal:** Return useful, stable errors without exposing exception text, credentials or
recovery URLs through HTTP responses, NDJSON streams or exported telemetry.

**Spec:** [API security evidence §9](../../reviews/2026-10-21-market-readiness-evidence/api-security.md)
— *"Public stable error codes/correlation ids for unexpected errors; full detail only in
access-controlled, scrubbed logs. Tests inject a synthetic exception containing a sentinel
secret/database detail and assert it never reaches JSON/stream output in production."*
§10 (recovery URL logging) is the source of the reset-link checks.

**Base:** `842a4cb11` on `main`, 1 November 2026. Plan 09 of the market-readiness series.

## Verified current behaviour (at base)

- [ExceptionHandlingMiddleware.cs](../../../src/backend/Intelibill.Api/Middleware/ExceptionHandlingMiddleware.cs)
  answers every caught exception with `detail = exception.Message` (500s included), logs
  `ex.Message` as a template property plus the exception object, and tries to set a status
  on a response that may already have started. Request cancellation is logged as an error.
  `ExceptionHandlingMiddlewareTests` asserts that `Unexpected` and `Invalid argument` appear
  in the body.
- [SalesController.cs:86](../../../src/backend/Intelibill.Api/Controllers/SalesController.cs)
  writes `ex.Message` into the offline-snapshot NDJSON `error` record, including when the
  failure happens before any byte was written.
- [PiiScrubbingEnricher.cs](../../../src/backend/Intelibill.Api/Logging/PiiScrubbingEnricher.cs)
  masks six top-level property names, and only when `Observability:Logging:EnablePiiMasking`
  is on. Nothing masks credentials in nested structures, sequences, dictionaries, URL query
  strings, message text or exception objects. Every Serilog event carrying an exception
  (Serilog request logging, EF Core, Wolverine, workers) exports `Exception.ToString()` to
  the console and to the OTLP sink.
- [RuntimeRoleGuard.cs](../../../src/backend/Intelibill.Infrastructure/Data/RuntimeRoleGuard.cs)
  logs a connection exception's message on each retry.
- **Already fixed by earlier plans:** `NoOpEmailService` no longer logs the reset URL (plan
  10); Google sign-in and `GoogleAuthProvider` no longer exist (external identity removed).
  The plan's `OAuthInvalidToken_ReturnsGenericProviderCode` therefore has no subject.
- OpenTelemetry ASP.NET Core 1.11.1 and HttpClient 1.11.1 instrumentation redact URL query
  values by default; HttpClient spans whose query names an identity secret are already
  filtered. Hub `access_token` is accepted only on `/hubs`; request logging records the path
  only. `Microsoft.AspNetCore` logs below Warning (which carry the query string) are
  filtered out.

## Design

**Public contract.** Exception-derived responses are `application/problem+json`:
`{ type, title, status, code, traceId, errors? }`. No `detail`, no exception text, type
name, stack or inner exception. Mapping (`SafeErrorFactory`):

| Exception | Status | Code | Field errors |
|---|---|---|---|
| `FluentValidation.ValidationException` | 400 | `Validation.Failed` | `{ code: property, description }`, a message that echoes the attempted value replaced with a generic one |
| `BadHttpRequestException` | its own | `Request.Invalid` | — |
| `ArgumentException` | 400 | `Request.Invalid` | — (text is not assumed safe) |
| `UnauthorizedAccessException` | 401 | `Auth.Unauthorized` | — |
| anything else | 500 | `Internal.Unexpected` | — |

ErrorOr results (`ErrorOrExtensions.ToProblemResult`) are authored, approved text and are
unchanged. The web already falls back to its own translated key when `detail` is absent.

**Trace id.** `SafeTraceId.Resolve` uses the request `Activity` trace id (W3C, 32 hex, parsed
by the runtime), else `HttpContext.TraceIdentifier` only if it matches
`^[A-Za-z0-9:._-]{1,64}$`, else a fresh random id. The same value goes into the response and
the diagnostic, so support can find an incident from the trace id alone.

**Streams.** The snapshot keeps the `error` discriminator and `OfflineSnapshot.StreamFailed`
code; `message` becomes stable text and `traceId` is added. A failure before any record is
written is rethrown and answered by the middleware as a normal 500. A response that has
already started is never given a second JSON document: the middleware writes the
diagnostic and aborts the connection. Cancellation by the caller is not an incident.

**Diagnostics.** `SafeDiagnosticWriter` writes one event per failure with the error code,
classification, status, method, route template and trace id, the exception type chain and
sanitized stack frames (`Type.Method`, no file paths), plus `SqlState` from a `DbException`
and `ProviderStatus` from an `HttpRequestException` — the two reviewed adapters. It never
passes the exception object or its message/`Data` to the logger.

**Backstop.** `RedactingSink` wraps every configured sink (console, OTLP, and any
`ILogEventSink` registered in DI). For each event it: drops the exception object and
replaces it with the same safe type/frame description; recursively masks properties whose
normalized names are credentials (authorization, tokens, password*, secret, api key, code,
otp, *link, cookie, connection string, query string…) — and PII (email, phone, card,
Aadhaar, PAN) when `EnablePiiMasking` is on — through structures, sequences and
dictionaries, bounded in depth, width and string length; and scrubs `name=value` secrets
and `Bearer` tokens from string values and message text. Redaction complements source
removal; it cannot find a secret in arbitrary prose. `PiiScrubbingEnricher` is replaced by
this sink.

## Tasks

### Task 1: Safe exception contract in the middleware

Files: `Api/Errors/SafeErrorFactory.cs`, `Api/Errors/SafeTraceId.cs`,
`Api/Logging/SafeDiagnosticWriter.cs`, `Api/Logging/SafeExceptionDescription.cs`,
`Api/Middleware/ExceptionHandlingMiddleware.cs`, `Program.cs`;
tests `Api.Unit.Tests/Middleware/ExceptionHandlingMiddlewareTests.cs`.

- [ ] Replace the raw-message assertions with `UnexpectedException_ReturnsCodeAndTraceWithoutSentinel`
  (synthetic bearer token, database password, email, inner exception, `Data`). Run; expect
  FAIL on disclosure.
- [ ] Add `ValidationError_DoesNotEchoPasswordValue`, `ArgumentException_UnexpectedTextIsNotPublic`,
  `RequestCancellation_DoesNotLogError`, `StartedNonFramedResponse_AbortsWithoutSecondJson`,
  `HostileTraceId_IsReplacedAndCannotInjectLogLines`. Run; expect FAIL.
- [ ] Implement factory, trace id, diagnostic writer and middleware. Run
  `dotnet test tests/backend/unit/Intelibill.Api.Unit.Tests --filter ExceptionHandlingMiddlewareTests`;
  expect PASS.

### Task 2: Snapshot stream failure

Files: `SalesController.cs`, `OfflineSalesSnapshotStreamRecords.cs`;
tests `Api.Unit.Tests/Controllers/SalesControllerTests.cs`.

- [ ] Add `SnapshotFailure_AfterFirstRecord_EmitsSafeErrorWithoutComplete` and
  `SnapshotFailure_BeforeFirstRecord_LeavesResponseToSafeErrorHandler`. Run; expect FAIL.
- [ ] Implement. Run `--filter SalesControllerTests`; expect PASS.

### Task 3: Redacting sink and source removal

Files: `Api/Logging/RedactingSink.cs`, `Api/Logging/LogEventRedactor.cs`,
`SerilogExtensions.cs`, delete `PiiScrubbingEnricher.cs`, `RuntimeRoleGuard.cs`;
tests `Api.Unit.Tests/Logging/SensitiveLoggingTests.cs`.

- [ ] Add `NestedSensitiveFields_AreRedactedAtEveryDepth`, `ExceptionDataAndMessage_DoNotReachSink`,
  `ResetLink_IsAbsentFromRenderedLogs`, `ExporterPayload_CarriesNoSentinel` (real OTLP sink
  over a capturing `HttpMessageHandler`) and bound/PII-switch cases. Run; expect FAIL.
- [ ] Implement the sink, wire it around every sink, remove the enricher, stop logging the
  retry exception message. Run `--filter SensitiveLoggingTests`; expect PASS.

### Task 4: End-to-end disclosure probe

Files: `Integration.Tests/ErrorDisclosureIntegrationTests.cs`.

- [ ] Over the real host and a captured DI sink: an unexpected failure returns the safe
  problem whose trace id finds the diagnostic; a snapshot failing after its first record
  ends with the safe `error` record and no `complete`; no sentinel appears in any response
  or rendered log event. Run
  `dotnet test tests/backend/integration/Intelibill.Integration.Tests --filter ErrorDisclosureIntegrationTests`;
  expect PASS.

### Task 5: Web snapshot client

Files: `offline-sales-snapshot-sync.service.ts`, `offline-sales-snapshot-indexeddb.service.ts`,
spec.

- [ ] Add a spec: an `error` record is terminal, keeps the server code and trace id, keeps
  the previous usable snapshot and ignores anything after it. Run; expect FAIL. Implement;
  expect PASS (`bun run test --run offline-sales-snapshot-sync`).

### Task 6: Documentation and full verification

- [ ] Record the contract and the logging boundary in `docs/backend-architecture.md`;
  update `CLAUDE.md`. Run the unit suites, the integration suite and the web tests.

## Review focus

1. Failure after stream flush — framing preserved, no `complete`.
2. Nested credential objects — dictionaries and arrays at depth.
3. Exception carrying a secret in `Data` or message — both routes blocked.
4. Invalid password validation — helpful field errors, no echoed value.
5. User-supplied trace id — bounded, validated, no log-line injection.
