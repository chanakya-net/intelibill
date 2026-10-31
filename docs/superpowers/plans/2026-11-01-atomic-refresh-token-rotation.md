# Atomic refresh token rotation — implementation plan (adapted)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Store no reusable refresh bearer values, and make concurrent refreshes of one credential produce at most one live successor.

**Architecture:** Refresh credentials are stored as SHA-256 hashes and linked into a chain (`ParentTokenId`) inside the device session that owns them. `AuthSessionService` rotates under the existing lock order (users → auth_sessions → refresh_tokens), re-reading the presented credential under its row lock; a consumed credential presented again revokes its session and every credential in it. Browser tabs serialize refreshes per user/session with the Web Locks API (bounded localStorage lease fallback) and adopt a successor another tab already stored.

**Tech Stack:** ASP.NET Core 10, EF Core 10 / Npgsql / PostgreSQL 17, ErrorOr, xUnit + NSubstitute + Testcontainers; Angular 21, RxJS, Vitest, Bun.

**Spec:** `docs/reviews/2026-10-21-market-readiness.md`, `docs/reviews/2026-10-21-market-readiness-evidence/api-security.md`; source plan item 04 (P1, public-SaaS launch gate).

**Branch:** `feat/atomic-refresh-token-rotation` · **Adapted at:** 2026-11-01, HEAD `228409310`

## Why this plan differs from the source plan

The source plan was verified at `5485507e`. Plan 03 (sessions, PR #771) has since landed:

1. **Rotation already lives in one transaction owner.** `AuthSessionService.RotateAsync`
   (Application) begins the transaction, takes the account lock through
   `IAuthSessionRepository`, and is what `RefreshTokenCommandHandler` delegates to. A separate
   `IRefreshTokenRotator` would be a second owner of the same transaction. **Ruling:** no new
   rotator type; `RotateAsync` becomes the rotator, and sign-out moves into the same service
   (`SignOutAsync`) so both share one lock-and-reuse path.
2. **The swallowed-exception test is gone.** `RefreshTokenCommandHandlerTests` now only checks
   delegation. The rotation rules are covered in `AuthSessionServiceTests` (unit) and the new
   `RefreshRotationConcurrencyTests` (integration, real PostgreSQL).
3. **A session already is the rotation family.** Every `AuthSession` is created by exactly one
   credential check with exactly one root credential, and no path adds a second root. A
   `FamilyId` column would duplicate `AuthSessionId`. **Ruling:** the session is the family;
   revoking a family means revoking the session and all its credentials, which
   `IAuthSessionRepository.RevokeAsync` already does. `AuthSessionId` keeps its name.
4. **Expand/contract is mandatory.** `docs/infrastructure-implementation-guide.md` §11: the old
   revision serves until the migration job succeeds. The old revision reads/writes `token` and
   `is_revoked`, so this migration keeps both columns (nullable `token`, scrubbed) and
   `IsRevoked` stays mapped and in step with `RevokedAt`. Dropping `token` and making
   `token_hash` NOT NULL is the follow-up contract migration.

## Global constraints

Web/API only; mobile excluded. Domain ← Application ← Infrastructure ← Api; repository
abstractions and `IUnitOfWork` for writes; versionless `csproj` references; warnings-as-errors;
standalone Angular; Bun only. Migrations run in the migration job, never at API startup.
Rate limiting on refresh is plan 08's, reuse metrics are plan 46's: neither is built here.

## Decisions

| Decision | Value |
|---|---|
| Transport secret | 64 bytes from `RandomNumberGenerator`, Base64Url (86 chars) |
| Stored value | `Convert.ToHexString(SHA256(UTF-8(token)))`, `varchar(64)`, unique index — same scheme as invitation tokens |
| Chain | `parent_token_id` unique where not null → a credential has at most one child |
| Reuse | presented credential with `consumed_at` set → revoke its session + every credential in it (`refresh-reuse`), commit, return generic `Auth.InvalidRefreshToken` |
| Unknown credential | generic `Auth.InvalidRefreshToken`, nothing revoked |
| Revoked / expired (`now >= ExpiresAt`) credential | generic error, nothing revoked |
| Sign-out with a consumed credential | treated as reuse (same outcome a thief's replay gets) |
| Lost response | strict: the client must sign in again; no successor is retained server-side for replay |
| Migration cutover | revoke every existing credential and live session (`credential-format-cutover`), null raw values; everyone signs in once |
| Audit | `LoggerMessage` warning with session id, credential id, user id — never token material |

## File map

**Modify (backend):** `Domain/Entities/RefreshToken.cs`, `Domain/Interfaces/Repositories/IRefreshTokenRepository.cs`,
`Domain/Interfaces/Repositories/IAuthSessionRepository.cs`, `Application/Common/Interfaces/ITokenService.cs`,
`Application/Common/Interfaces/IAuthSessionService.cs`, `Application/Features/Auth/Services/AuthSessionService.cs`,
`Application/Features/Auth/Commands/RevokeToken/RevokeTokenCommandHandler.cs`, both refresh/revoke validators,
`Infrastructure/Services/Auth/TokenService.cs`, `Infrastructure/Repositories/RefreshTokenRepository.cs`,
`Infrastructure/Repositories/AuthSessionRepository.cs`, `Infrastructure/Repositories/EmailOwnershipChallengeRepository.cs`,
`Infrastructure/Data/Configurations/RefreshTokenConfiguration.cs`.

**Create (backend):** `Domain/ValueObjects/RefreshTokenOwner.cs`, `Application/Common/Security/RefreshTokenSecrets.cs`,
`Application/Common/Interfaces/RefreshSecret.cs`, migration `HashRefreshTokensAndChainRotations` (+ designer, snapshot),
`tests/backend/integration/Intelibill.Integration.Tests/RefreshRotationConcurrencyTests.cs`.

**Frontend:** create `core/auth/refresh-coordinator.service.ts` + spec; modify `auth-token.service.ts`,
`auth.service.test-utils.ts`, `auth.service.token-flows.spec.ts`.

---

### Task 1: Red integration suite against today's code

**Files:** Create `tests/backend/integration/Intelibill.Integration.Tests/RefreshRotationConcurrencyTests.cs`

The suite decorates `IAuthSessionRepository` with a `RotationBarrier` that can pause a request
(a) on entry to `LockUserAuthorityAsync` — after the credential lookup, before any lock — for N
parties, or (b) on entry to `CommitAsync` — holding every lock. `WaitForLockWaiterAsync` polls
`pg_stat_activity` (`wait_event_type = 'Lock'`) to prove a competing request is queued.

- [x] Write `ParallelRefresh_AtMostOneSuccessAndNoLiveBranchAfterReuse`: arm (a) for 2 parties,
  fire two refreshes of one credential, release together. Assert exactly one `200`, at most one
  row whose `ParentTokenId` is the presented credential, zero usable credentials in the session,
  the session revoked, and the `200` response's refresh token refused afterwards.
- [x] Run `dotnet test tests/backend/integration/Intelibill.Integration.Tests --filter RefreshRotationConcurrencyTests`.
  **Expected:** FAIL — both requests return `200` (record the output in the PR).
- [x] Commit the red test.

### Task 2: Hash-only storage and the chain (Domain + Infrastructure + migration)

**Interfaces produced:**

```csharp
// Domain
RefreshToken.Issue(Guid userId, Guid authSessionId, long securityVersion, string tokenHash, DateTimeOffset expiresAt) : RefreshToken
RefreshToken.Rotate(string successorHash, DateTimeOffset successorExpiresAt, DateTimeOffset now) : RefreshToken  // throws if not usable
RefreshToken.Revoke(string reason, DateTimeOffset now)          // first reason kept
RefreshToken.IsUsableAt(DateTimeOffset now) : bool             // not revoked, not consumed, now < ExpiresAt
record RefreshTokenOwner(Guid TokenId, Guid UserId, Guid? AuthSessionId)
IRefreshTokenRepository.FindOwnerByHashAsync(string tokenHash, CancellationToken) : Task<RefreshTokenOwner?>  // untracked
IRefreshTokenRepository.LockAsync(Guid tokenId, CancellationToken) : Task<RefreshToken?>                       // FOR UPDATE, fresh values
IAuthSessionRepository.LockAsync(Guid userId, Guid sessionId, CancellationToken) : Task<AuthSession?>         // FOR UPDATE, fresh values
// Application
record RefreshSecret(string Token, string TokenHash, DateTimeOffset ExpiresAt)
ITokenService.CreateRefreshSecret() : RefreshSecret             // replaces CreateRefreshToken(Guid)
static RefreshTokenSecrets.Hash(string token) : string
```

- [x] Domain unit tests (`RefreshTokenTests`): `Issue_StoresOnlyTheHash`, `Rotate_ConsumesThePredecessorAndLinksOneChild`,
  `Rotate_WhenAlreadyConsumed_Throws`, `IsUsableAt_ExactExpiry_IsFalse`, `Revoke_KeepsTheFirstReason`. Red, then green.
- [x] `TokenServiceTests.CreateRefreshSecret_ReturnsBase64UrlTokenAndItsHash` (86 chars, hash = `RefreshTokenSecrets.Hash(token)`).
- [x] Configuration: drop the `Token` mapping; `TokenHash` max 64, unique, optional until contract;
  `ParentTokenId` unique filtered `parent_token_id IS NOT NULL`; `RevocationReason` max 64.
- [x] `dotnet ef migrations add HashRefreshTokensAndChainRotations`; hand-edit `Up` to keep `token`
  (alter to nullable) instead of dropping it, then scrub/revoke existing rows and live sessions;
  `Down` deletes hash-only rows before restoring `token NOT NULL`.
- [x] Every set-based refresh revocation (`AuthSessionRepository`, `EmailOwnershipChallengeRepository`)
  also writes `revocation_reason`.

### Task 3: Atomic rotation, reuse revocation, sign-out under the same locks

**Interfaces produced:** `IAuthSessionService.SignOutAsync(string refreshToken, CancellationToken) : Task<ErrorOr<Success>>`.

- [x] `AuthSessionServiceTests` (rewrite rotation tests onto the new repository API):
  `RotateAsync_ConsumesThePresentedCredentialAndAddsOneChild`, `RotateAsync_ConsumedCredential_RevokesItsSessionAndCommits`,
  `RotateAsync_UnknownCredential_RevokesNothing`, `RotateAsync_AtExactExpiry_IsRefused`,
  `RotateAsync_WhenSigningFails_DoesNotCommit`, `RotateAsync_TakesLocksInOrder` (Received.InOrder:
  user → session → credential), `SignOutAsync_EndsTheSession`, `SignOutAsync_ConsumedCredential_TreatedAsReuse`,
  `RevokeAsync_TakesTheAccountLockFirst`.
- [x] Implement. Order inside `RotateAsync`: hash → `FindOwnerByHashAsync` → begin → lock user →
  lock session → lock credential → reuse check (commit revocation) → usable/authority/session/epoch
  checks → load user → `Rotate` → scope/extend → sign → commit. Signing failure throws before commit,
  so the transaction rolls back and no successor survives.
- [x] `RevokeTokenCommandHandler` delegates to `SignOutAsync`; `AuthSessionService.RevokeAsync`
  takes the account lock then the session lock.
- [x] Integration (same suite as Task 1): `RefreshDatabase_ContainsHashOnly` (no column of any
  `refresh_tokens` row equals a raw request or response token), `ConsumedPredecessor_RevokesItsFamilyOnly`
  (second device untouched), `UnknownToken_DoesNotRevokeSessions`, `RefreshAtExpiry_IsRejected`
  (frozen `TimeProvider` at exact expiry → 401; one second earlier → 200).
- [x] Run Task 1's filter. **Expected:** all green.

### Task 4: Logout and reset racing refresh

- [x] `LogoutRacingRefresh_NoSurvivingSuccessor` as a `[Theory]` over `RefreshFirst` / `LogoutFirst`;
  `ResetRacingRefresh_NoSurvivingSuccessor` likewise. Loser is held at barrier (a) or the winner at (b)
  with the competitor proven queued via `pg_stat_activity`. After both finish: zero usable credentials
  for the user, every session revoked, old access token → 401.
- [x] Run the suite. **Expected:** green.

### Task 5: Browser coordination

**Interfaces produced:**

```ts
export const REFRESH_LOCK_MANAGER: InjectionToken<RefreshLockManager | null>;   // navigator.locks or null
export const REFRESH_LEASE_STORAGE: InjectionToken<Storage | null>;            // localStorage or null
RefreshCoordinator.runExclusive<T>(scope: { userId: string; sessionId: string | null }, work: () => Observable<T>): Observable<T>
```

- [x] `refresh-coordinator.service.spec.ts`: `serializesWorkForOneSession`, `doesNotSerializeDifferentSessions`,
  `releasesTheLockWhenWorkFails`, `fallbackLease_SerializesWithoutWebLocks`, `fallbackLease_GivesUpAfterTheBound`.
- [x] `auth.service.token-flows.spec.ts`: `twoTabsRotateOnce`, `waiterUsesStoredSuccessor`,
  `waiterIgnoresAnotherUsersStoredSession`, `rejectedRefreshIsAuthoritative`, `lostRefreshResponseRequiresReauthentication`;
  replace the old "adopts a fresher stored session after a 401" test, which contradicts the strict policy.
- [x] `AuthTokenService.refreshAccessToken` runs inside `runExclusive`; after acquiring, it adopts a
  stored/in-memory session for the same user id and session id whose refresh token differs and does
  not expire earlier; otherwise it calls the API. A rejection is never followed by adoption.
- [x] From `src/frontend`: `bun run test --run src/app/core/auth/refresh-coordinator.service.spec.ts`,
  `bun run test --run src/app/core/auth/auth.service.token-flows.spec.ts`, then `bun run test`.

### Task 6: Whole-suite verification and docs

- [x] `dotnet build src/backend/Intelibill.slnx`; the three unit suites; the full integration suite; `bun run test`; `bun run build`.
- [x] `docs/backend-architecture.md`: refresh rotation, reuse policy and the support answer for
  "I was signed out after refreshing in two places / after a network drop".

## Rollout

1. Migration job applies `HashRefreshTokensAndChainRotations`: all users sign in once. The old
   revision keeps serving (it still finds `token`/`is_revoked`), but its credentials are revoked.
2. New revision writes hash-only rows. Rows the old revision wrote between the job and the swap
   carry a raw `token` and no hash; the new revision cannot find them, so those people sign in again.
3. **Follow-up contract migration** (separate PR, after archival sign-off): hash/revoke and empty rows
   with `token_hash IS NULL` (written by the old revision during the deploy window), end the sessions
   that hold no hashed credential, drop `token` and its index, make `token_hash` NOT NULL, drop
   `is_revoked`.
4. Rollback: redeploy the previous digest only with refresh disabled; never restore raw storage.

## Review focus

1. Two refreshes of one credential at once → one `200`, and after reuse nothing in the session works (Task 1).
2. A valid refresh whose response is lost → next attempt is refused and the browser asks for sign-in (Task 5).
3. A stolen, already-rotated credential replayed → that device's session ends; other devices keep working (Task 3).
4. Logout or reset overlapping a refresh in either order → no surviving credential (Task 4).
5. Refresh exactly at expiry → refused, using a frozen clock rather than sleeps (Task 3).

## Outcome (2026-11-01)

Implemented as planned, with three adjustments found while executing:

- `dotnet ef` stamped the migration `20261031183735`, which sorts before the hand-named
  `20261031210000_AddAuthSessions`; it was renamed `20261101090000_…` so it applies after it.
- `RefreshDatabase_ContainsHashOnly` found the raw secret in MVC's Trace-level action-argument log,
  which renders records through `ToString()`. `RefreshTokenRequest`, `RevokeTokenRequest`, both
  commands and `AuthResult` now omit bearer credentials from their string form.
- The fallback lease key uses the `intelibill.` prefix: `inventory.` is an i18n namespace, and the
  translation-coverage spec reads dotted literals under it as translation keys.

Task 1's red run: both parallel refreshes answered `200`.

Fixed after an independent review of the branch:

- `AuthStorage` dropped `sessionId` on reload, so a restored tab and a tab that had just renewed took
  different lock names and could both present one credential — the reuse path would then end the
  session. Storage now keeps it, the lock is keyed per user, and a missing session id matches the same
  user's session.
- A renewal answering after the tab signed out or switched account could have resurrected the old
  session or cleared the new one. Results are now applied only to the session that was presented, and
  the interceptor no longer replays (or clears on behalf of) a request made as another account.
- Reuse revocation now commits with `CancellationToken.None`: a victim closing the tab must not roll
  back the revocation of an attacker's successor.
- The refresh request times out after 12 s (below the 15 s fallback lease) instead of holding the lock.

