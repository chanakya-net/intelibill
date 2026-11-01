# Abuse-resistant rate limiting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enforce atomic request, account-attempt and recovery-destination budgets across replicas without permanent account lockout.

**Architecture:** `RateLimitFilter` stops reading and writing `IDistributedCache` and asks a new `IAtomicRateLimitStore` to check and consume every budget for a request in one short PostgreSQL transaction (rows locked in sorted order, fixed windows, optional backoff). `RateBudgetFactory` composes the budgets from stable route metadata, a normalized client address and, where an endpoint declares one, a normalized account or destination subject, all keyed through HMAC so no raw identifier is stored. Store failure answers 503 on fail-closed (auth) endpoints and falls back to a bounded, metered in-process limiter elsewhere.

**Tech Stack:** ASP.NET Core 10, C#, Npgsql/PostgreSQL, ErrorOr, xUnit 2.9, Testcontainers.

**Spec:** Plan 08 "Abuse-resistant rate limiting" (pasted in the session that produced this file; it argues from `docs/reviews/2026-10-21-market-readiness.md` and its `api-security.md` evidence). Mobile and web are out of scope: no client change is needed for 429/503 bodies that keep their existing shape.

## Global Constraints

- Layer order Domain ← Application ← Infrastructure ← Api; the store interface lives in Application, the PostgreSQL implementation in Infrastructure, HTTP composition in Api.
- Warnings are errors; versions only in `Directory.Packages.props` (no new package is needed).
- API startup never applies migrations; the table arrives through a migration the migration job runs.
- The table is infrastructure, not tenant data: no RLS, no shop column, listed beside the identity tables that must stay outside RLS in `scripts/db/verify-tenant-policies.sql` and `scripts/db/phase7-bootstrap.sql`.
- Public 429 keeps its current body (`Title = "RateLimitExceeded"`, `errors[0].Code = "RateLimitExceeded"`) and headers (`X-RateLimit-Limit/Remaining/Reset`, `Retry-After`); Retry-After is capped at 86400 seconds.
- Headers describe only the request (route + caller + address) budget. Account and destination budgets never surface a count.
- Account/destination budgets count every attempt, known account or not.
- Recommended values (configurable): generic 100/min per user+address+route; login 10/min per address plus 20 per 15 min per normalized account; sends 5/15 min per address plus 3/15 min and 10/day per normalized destination.
- Never log a raw identifier, bucket material or exception text by hand; logging goes through the existing redacted sinks.

## Review Focus

1. Parallel replicas — two stores on two data sources, 100 barrier-released acquisitions, limit 10: exactly ten admitted (`TwoInstances_ParallelBurst_AdmitsExactlyLimit`, Task 1).
2. Rotating source addresses against one mailbox — password-reset requests for one address from ten peers issue at most three reset tokens (`RotatingIps_CannotExceedDestinationBudget`, Task 4).
3. Body omitted or malformed — the 400 from `[ApiController]` model validation still spends the address budget, because the filter is ordered before `ModelStateInvalidFilter` (`MissingBody_StillConsumesAddressBudget`, Task 4).
4. Spoofed `X-Forwarded-For` from an untrusted peer — the bucket stays the peer's; from a trusted proxy the forwarded address is used (`UntrustedForwardedFor_DoesNotCreateFreshBucket`, Task 4).
5. Cooldown turning into lockout — after the window and the backoff pass, the same subject is admitted again (`BudgetExpiry_AllowsRecoveryAgain`, Task 2).

---

## File map

| File | Responsibility |
|---|---|
| Create `src/backend/Intelibill.Application/Common/Interfaces/IAtomicRateLimitStore.cs` | `RateBudget`, `RateDecision`, `RateLimitStoreUnavailableException`, `IAtomicRateLimitStore` |
| Create `src/backend/Intelibill.Infrastructure/Services/RateLimiting/PostgresRateLimitStore.cs` | Atomic multi-budget acquisition and expired-row deletion |
| Create `src/backend/Intelibill.Infrastructure/Services/RateLimiting/RateLimitBucketCleanupWorker.cs` | Periodic batched cleanup |
| Create `src/backend/Intelibill.Infrastructure/Options/RateLimitStoreOptions.cs` | Command timeout, cleanup interval and batch size |
| Create `src/backend/Intelibill.Infrastructure/Migrations/20261101150000_AddAtomicRateLimitBuckets.cs` | `rate_limit_buckets` table and expiry index |
| Modify `src/backend/Intelibill.Infrastructure/DependencyInjection.cs` | Register store, options, worker |
| Create `src/backend/Intelibill.Api/Middleware/RateLimiting/RateBudgetFactory.cs` | Route id, address normalization, subject normalization, budget list |
| Create `src/backend/Intelibill.Api/Middleware/RateLimiting/RateLimitKeyHasher.cs` | HMAC-SHA256 bucket keys, current + previous key |
| Create `src/backend/Intelibill.Api/Middleware/RateLimiting/IRateLimitSubject.cs` | Request models name the account/destination they act on |
| Create `src/backend/Intelibill.Api/Middleware/RateLimiting/LocalFallbackRateLimiter.cs` | Bounded in-process fixed windows for fail-open endpoints |
| Create `src/backend/Intelibill.Api/Middleware/RateLimiting/RateLimitMetrics.cs` | Decision and outage counters with bounded tags |
| Modify `RateLimitFilter.cs`, `RateLimitAttribute.cs`, `RateLimitPolicy.cs`, `RateLimitPolicyResolver.cs` | Use the store; carry `Subject` and `FailClosed` |
| Modify `src/backend/Intelibill.Api/Options/RateLimitingOptions.cs`, `RateLimitingOptionsValidator.cs` | Subject budgets, bucket keys, subject mode |
| Modify `Program.cs`, `AuthController.cs`, `EmailOwnershipController.cs`, `PhoneOwnershipController.cs` | Filter order and registrations; endpoint subjects |
| Tests | `tests/backend/unit/Intelibill.Api.Unit.Tests/Middleware/{RateLimitFilterTests,RateBudgetFactoryTests,RateLimitPolicyResolverTests}.cs`, `.../Options/RateLimitingOptionsValidatorTests.cs`, `tests/backend/integration/Intelibill.Integration.Tests/{AtomicRateLimitTests,RateLimitIntegrationTests,ApiWebApplicationFactory}.cs` |
| Docs/config | `docs/backend-architecture.md` § Rate Limiting, `CLAUDE.md`, `docker-compose.yml`, `scripts/db/*.sql` |

---

### Task 1: Atomic PostgreSQL budget store

**Files:** Create the Application interface file, `PostgresRateLimitStore.cs`, `RateLimitStoreOptions.cs`, the migration; modify Infrastructure `DependencyInjection.cs`; test `AtomicRateLimitTests.cs`.

**Interfaces — Produces:**

```csharp
public sealed record RateBudget(string PolicyId, string Key, int Limit, int WindowSeconds, int BackoffSeconds = 0);
// Limit/Remaining/ResetAt describe budgets[0]; RetryAt is set only when denied.
public sealed record RateDecision(bool Allowed, int Limit, int Remaining, DateTimeOffset ResetAt, DateTimeOffset? RetryAt);
public sealed class RateLimitStoreUnavailableException(Exception inner) : Exception("The rate limit store is unavailable.", inner);
public interface IAtomicRateLimitStore
{
    Task<RateDecision> AcquireAsync(IReadOnlyList<RateBudget> budgets, DateTimeOffset now, CancellationToken ct);
    Task<int> DeleteExpiredAsync(DateTimeOffset now, int batchSize, CancellationToken ct);
}
```

Table `rate_limit_buckets(policy_id varchar(200), bucket_key varchar(128), window_start timestamptz, window_seconds int, count int, blocked_until timestamptz null, expires_at timestamptz)`, PK `(policy_id, bucket_key)`, index on `expires_at`, check `count >= 0`, `window_seconds > 0`. Hand-written like `AddDistributedCacheTable` (raw SQL, not an EF entity, with `[DbContext]`/`[Migration]` attributes). Logged, not UNLOGGED: a failover must not hand out fresh budgets.

Acquisition algorithm (the part the signature does not determine):
1. Merge duplicate `(PolicyId, Key)` keeping the smallest limit; sort ordinal by `PolicyId`, then `Key`.
2. One Read Committed transaction with `SET LOCAL lock_timeout`. One `NpgsqlBatch`, one upsert per budget in sorted order: `INSERT … ON CONFLICT (policy_id, bucket_key) DO UPDATE` that resets `window_start = @now, count = 0` only when `window_start + window_seconds <= @now`, clears an elapsed `blocked_until`, and `RETURNING count, window_start, window_seconds, blocked_until`. The upsert locks each row in sorted order.
3. Denied when any row has `blocked_until > @now` or `count >= limit`. Allowed → one batch incrementing every row. Denied → for each exhausted, not-yet-blocked row with `BackoffSeconds > 0`, set `blocked_until = @now + backoff`; nothing else is incremented.
4. `expires_at = greatest(window end, blocked_until)` on every write. Commit.
5. `NpgsqlException`, `TimeoutException` and `DbException` become `RateLimitStoreUnavailableException`; cancellation propagates.

`DeleteExpiredAsync` deletes up to `batchSize` rows with `expires_at <= @now`, selected `ORDER BY policy_id, bucket_key FOR UPDATE SKIP LOCKED` and rechecked in the outer `WHERE`, so it never waits on, or deletes, a row an acquirer has just renewed.

- [ ] **Step 1: Write `TwoInstances_ParallelBurst_AdmitsExactlyLimit`** — two `PostgresRateLimitStore` instances on two `NpgsqlDataSource`s built from `RuntimeConnectionString`; 100 tasks released by one `TaskCompletionSource`, alternating stores, each acquiring `new RateBudget("test:burst", key, 10, 60)`. Assert 10 allowed, 90 denied, persisted `count == 10`.
- [ ] **Step 2: Run** `dotnet test tests/backend/integration/Intelibill.Integration.Tests --filter AtomicRateLimitTests` — Expected: fails to compile (no store).
- [ ] **Step 3: Implement** the interface file, options (`RateLimitStore` section: `CommandTimeoutSeconds = 2`, `CleanupInterval = 00:05:00`, `CleanupBatchSize = 1000`), the store, the migration, and DI (`AddSingleton<IAtomicRateLimitStore, PostgresRateLimitStore>`).
- [ ] **Step 4: Run** the filter again — Expected: PASS.
- [ ] **Step 5: Commit** `feat(rate-limit): add atomic PostgreSQL budget store`.

### Task 2: Multi-budget semantics, expiry and cleanup

**Files:** Modify `AtomicRateLimitTests.cs`; add `RateLimitBucketCleanupWorker.cs`; register it.

**Interfaces — Consumes:** Task 1's store.

- [ ] **Step 1: Write tests**
  - `MultipleBudgets_DenialDoesNotPartiallyConsume`: budgets A (limit 5) and B (limit 1); acquire twice; second denied; A's persisted count is 1.
  - `WindowBoundary_ResetsOnce`: limit 2, window 60s; consume 2 at `t0`; at `t0+60s` fire 5 parallel acquisitions; exactly 2 allowed and `window_start == t0+60s`.
  - `ReorderedBudgets_DoNotDeadlock`: 50 parallel acquisitions, half passing `[A, B]`, half `[B, A]`, limit 1000; all complete within 30s, none throw, both counts 50.
  - `BudgetExpiry_AllowsRecoveryAgain`: limit 1, window 60s, backoff 300s; deny at `t0+1s` sets `RetryAt = t0+301s`; still denied at `t0+120s`; allowed at `t0+302s`.
  - `Cleanup_DoesNotDeleteActiveWindow`: one expired row, one active row, one blocked row whose window elapsed but backoff has not; `DeleteExpiredAsync` returns 1 and only the expired row is gone.
- [ ] **Step 2: Run** — Expected: the backoff and cleanup tests fail if any rule above is missing.
- [ ] **Step 3: Implement** fixes in the store and `RateLimitBucketCleanupWorker : BackgroundService` (`PeriodicTimer` at `CleanupInterval`; loops `DeleteExpiredAsync` while a full batch comes back; logs failures through `ILogger` and keeps running).
- [ ] **Step 4: Run** — Expected: all `AtomicRateLimitTests` PASS.
- [ ] **Step 5: Commit** `feat(rate-limit): enforce all-or-nothing budgets and expiry cleanup`.

### Task 3: Budget composition, keyed buckets, filter on the store

**Files:** Create `RateBudgetFactory.cs`, `RateLimitKeyHasher.cs`, `IRateLimitSubject.cs`, `LocalFallbackRateLimiter.cs`, `RateLimitMetrics.cs`; modify `RateLimitFilter.cs`, `RateLimitAttribute.cs`, `RateLimitPolicy.cs`, `RateLimitPolicyResolver.cs`, `RateLimitingOptions.cs`, `RateLimitingOptionsValidator.cs`, `Program.cs`; tests `RateBudgetFactoryTests.cs`, `RateLimitFilterTests.cs`, `RateLimitPolicyResolverTests.cs`, `RateLimitingOptionsValidatorTests.cs`.

**Interfaces — Produces:**

```csharp
public enum RateLimitSubject { None, Account, Destination }
public interface IRateLimitSubject { string? RateLimitSubject { get; } }
// RateLimitAttribute gains: RateLimitSubject Subject { get; set; }, bool FailClosed { get; set; }
// RateLimitPolicy gains init properties: RateLimitSubject Subject, bool FailClosed
public sealed class RateBudgetFactory { IReadOnlyList<RateBudget> Create(ActionExecutingContext context, RateLimitPolicy policy); }
public sealed class RateLimitKeyHasher { IReadOnlyList<string> Hash(string material); } // current key first
public enum SubjectBudgetMode { Enforce, Shadow }
public sealed class SubjectBudgetOptions { int Limit; int PeriodInMinutes; int DailyLimit; }
// RateLimitingOptions gains: Account (20/15, daily 0), Destination (3/15, daily 10),
// SubjectMode = Enforce, BucketKey, PreviousBucketKey (base64, 32 bytes)
```

Composition rules:
- Request budget first: `PolicyId = "req:{METHOD} {attribute route template, lower-case}"` (fallback `controller/action` route values, never the raw path); key material `"{user}|{address}"` where user is `NameIdentifier`, else `sub`, else `anonymous`.
- Address: IPv4-mapped IPv6 → IPv4; other IPv6 → its /64 (`"{prefix}/64"`); missing → `unknown`.
- Subject: from the first action argument implementing `IRateLimitSubject`, else (Account only) the authenticated user id. Normalized `email:{trim, lower}` when it contains `@`, `phone:{E.164}` when `PhoneNumberFormat.TryNormalize` succeeds with `IPhoneAuthPolicy.DefaultCountryCode`, else `raw:{trim, lower}`. Empty → no subject budget.
- Account → `subj:account` (Account options). Destination → `subj:destination:window` and, when `DailyLimit > 0`, `subj:destination:day` (1440 min). Subject budgets have no backoff; their window is the cooldown.
- `RelaxEndpointPolicies` also widens subject budgets to the global default.
- Every budget's key is emitted once per configured hash key (current, then previous), so a key rotation cannot reset an allowance during the overlap.
- Missing `BucketKey` is refused outside Development/Testing; there a per-process random key is used.

Filter flow: disabled → next. Build budgets. In `Shadow` mode acquire request budgets (enforced) and subject budgets separately (denial only recorded). Store unavailable → `FailClosed` answers 503 `{ Title = "RateLimit.Unavailable" }` through `ToUnavailableResult`, else the local fallback on the request budget (capacity 10 000 windows; full → deny new keys). Allowed → headers, next. Denied → 429 headers + existing body. Registered with `options.Filters.AddService<RateLimitFilter>(RateLimitFilter.FilterOrder)` where `FilterOrder = -3000`, ahead of `ModelStateInvalidFilter` (-2000).

- [ ] **Step 1: Write unit tests**
  - `RateBudgetFactoryTests`: `EmailCaseAndWhitespace_ShareKey` (`" User@Example.COM "` vs `"user@example.com"`), `PhoneFormats_ShareKey` (`"+91 98765 43210"` vs `"+919876543210"`), `DifferentRecordIds_ShareRouteBudget` (template `api/items/{itemId}`, paths `/api/items/1` and `/api/items/2` → same PolicyId and Key), `Keys_ContainNoRawIdentifiers`, `Ipv4MappedAddress_SharesIpv4Bucket`, `Ipv6Addresses_InOneSlash64_ShareBucket`, `AccountWithoutBody_UsesAuthenticatedUser`, `PreviousKey_DuplicatesEveryBudget`, `Destination_AddsWindowAndDailyBudgets`.
  - `RateLimitFilterTests` (fake store recording budgets): headers on allow; 429 body and `Retry-After` from `RetryAt`; disabled skips the store; `StoreUnavailable_FailClosed_Returns503`; `StoreUnavailable_FailOpen_UsesBoundedFallback` (limit 1: second call 429); `ShadowMode_SubjectDenialDoesNotBlock`.
  - Validator: `MissingBucketKey_FailsOutsideDevelopment`, `MalformedBucketKey_Fails`, `SubjectLimits_MustBePositive`.
- [ ] **Step 2: Run** `dotnet test tests/backend/unit/Intelibill.Api.Unit.Tests --filter "RateLimitFilter|RateBudgetFactory|RateLimitPolicyResolver|RateLimitingOptionsValidator"` — Expected: compile failure.
- [ ] **Step 3: Implement** the types above and register `RateBudgetFactory`, `RateLimitKeyHasher`, `LocalFallbackRateLimiter`, `RateLimitMetrics` as singletons; `RateLimitFilter` scoped. Metrics on the existing `InteliBill.Application` meter: `intelibill.rate_limit.decisions{outcome}` and `intelibill.rate_limit.store_unavailable{mode}`.
- [ ] **Step 4: Run** — Expected: PASS.
- [ ] **Step 5: Commit** `feat(rate-limit): compose keyed budgets and enforce through the atomic store`.

### Task 4: Auth endpoints, proxy trust, outage, HTTP-level proof

**Files:** Modify `AuthController.cs`, `EmailOwnershipController.cs`, `PhoneOwnershipController.cs`, `ApiWebApplicationFactory.cs`, `RateLimitIntegrationTests.cs`, `docker-compose.yml`, `scripts/db/*.sql`.

Endpoint subjects (all these attributes also get `FailClosed = true`):
- Account: `login/email`, `login`, `password-reset/confirm`, `reauthenticate` (authenticated user).
- Destination: `register/email`, `password-reset/request`, `enrollment/email/request`, `enrollment/email/claim/request`, `email-change/request`, `phone/enrollment/request`, `phone/login/request`, `phone/change/request`.
- Confirmation endpoints keep address budgets only; per-challenge attempts stay with the challenge (plan 05).
- Request records implement `IRateLimitSubject` explicitly (`string? IRateLimitSubject.RateLimitSubject => Email;`).

Integration factory: default store is a permissive `UnlimitedRateLimitStore` (what the no-op cache gave every other test); rate-limit tests put `PostgresRateLimitStore` back. A test `IStartupFilter` sets `Connection.RemoteIpAddress` from `X-Test-Peer` before forwarded headers run.

- [ ] **Step 1: Write integration tests** (real store, real PostgreSQL)
  - `RotatingIps_CannotExceedDestinationBudget`: verified user; ten `password-reset/request` calls from ten peers; three 200s, seven 429s; `password_reset_tokens` rows for the user ≤ 3.
  - `EmailCaseAndWhitespace_ShareBudget`: `RateLimiting:Account:Limit = 3`; four `login/email` with case/whitespace variants from four peers; fourth 429.
  - `DifferentRecordIds_ShareRouteBudget`: test route `items/{id}`, limit 2; ids 1, 2, 3 → third 429.
  - `UnknownAndKnownDestination_HaveSamePublicOutcome`: four reset requests each for a verified and an unknown address, each from a fresh peer; same status sequence (200, 200, 200, 429) and identical bodies and `X-RateLimit-Remaining`.
  - `PersistedBuckets_ContainNoRawIdentifiers`: after the above, no `policy_id` or `bucket_key` contains the email or the peer address.
  - `MissingBody_StillConsumesAddressBudget`: eleven `login/email` with `{}`; first ten 400, eleventh 429.
  - `UntrustedForwardedFor_DoesNotCreateFreshBucket`: `Proxy:Enabled`, `KnownProxies = ["10.0.0.1"]`, default limit 2; peer `198.51.100.7` with three different `X-Forwarded-For` → third 429; peer `10.0.0.1` forwarding two different clients → both 200.
  - `AuthBudgetStoreUnavailable_Returns503WithoutIssuance`: store on a data source pointing at a closed port; `password-reset/request` → 503 `RateLimit.Unavailable` and no `password_reset_tokens` row; `login/email` → 503; a default-policy route → 200 through the fallback.
  - Existing `RateLimitIntegrationTests` keep their assertions on the real store.
- [ ] **Step 2: Run** `timeout 1800 dotnet test tests/backend/integration/Intelibill.Integration.Tests --filter "AtomicRateLimitTests|RateLimitIntegrationTests"` — Expected: subject tests fail until endpoints declare subjects.
- [ ] **Step 3: Implement** the endpoint changes, factory changes, `RateLimiting__BucketKey` placeholder in `docker-compose.yml`, and `rate_limit_buckets` in the outside-RLS lists of both SQL scripts.
- [ ] **Step 4: Run** — Expected: PASS.
- [ ] **Step 5: Commit** `feat(rate-limit): apply account and destination budgets to auth endpoints`.

### Task 5: Documentation, full verification, live calls

- [ ] **Step 1:** Add `docs/backend-architecture.md` § Rate Limiting (budgets, keys and rotation overlap, fail-closed vs fallback, shadow rollout, cleanup, NAT trade-off) and update the CLAUDE.md rate-limiting line.
- [ ] **Step 2: Run** `dotnet build src/backend/Intelibill.slnx`, the three unit projects, and `timeout 1800 dotnet test tests/backend/integration/Intelibill.Integration.Tests` — Expected: all pass.
- [ ] **Step 3: Live calls:** apply the migration to the local database, run the API, and with `curl` show: login burst → 429 after 10; spoofed `X-Forwarded-For` does not reset it; one destination via mixed-case emails → 429 after 3 sends; `rate_limit_buckets` holds only hashed keys; with the store unreachable, login → 503.
- [ ] **Step 4: Commit** docs, push, open the PR.

## Rollout notes

Deploy the migration first. `RateLimiting:SubjectMode = Shadow` measures subject budgets (metric `outcome=shadow_denied`) without refusing; switch to `Enforce` once the numbers look right. Existing cache counters are not migrated; they expire on their own. Rollback cannot return to unlimited issuance on store failure: fail-closed endpoints answer 503 until the store is back.
