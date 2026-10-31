# Session revocation and device management — implementation plan (adapted)

**Spec:** `docs/reviews/2026-10-21-market-readiness.md`, `docs/reviews/2026-10-21-market-readiness-evidence/api-security.md`
**Source plan:** item 03 (P0, public-signup blocker)
**Branch:** `feat/session-revocation-device-management`
**Adapted at:** 2026-10-31, HEAD `83cf6a362`

## Why this plan differs from the source plan

The source plan was verified statically at `5485507e` on 2026-10-21. The repository has moved on,
and three of its premises no longer hold:

1. **External identity providers are gone** (`3d19cc364`, migration `20261031151608_RemoveExternalLogins`).
   `ExternalLogin` is not an issuer and never will be; credentials are the only way in. Every
   "password/OAuth/refresh" matrix in the source plan collapses to password/refresh, and
   `ProviderProofVersionChanged_RequiresFreshAuthentication` has no provider to exercise — the
   equivalent coverage is the email-ownership (enrollment/claim) proof, which is this codebase's
   only non-password credential path.
2. **`User.SecurityVersion` already exists**, as does `RefreshToken.SecurityVersion` with
   `BindSecurityVersion`, and `ResetPassword` already runs under an advisory lock plus
   `SELECT ... FOR UPDATE` on `users`. The new schema is therefore `auth_sessions` plus a session
   link on `refresh_tokens`, not a SecurityVersion column.
3. **Live validation is already wired.** `Program.cs` `OnTokenValidated` calls
   `LiveSessionValidator`, which re-checks account state and the live membership role/status
   rather than trusting the token's claim. What it cannot do is distinguish one device from
   another: there is no session row, so revocation is all-or-nothing through a SecurityVersion
   bump.

What remains unbuilt, and is what this plan delivers:

- **No per-device session record.** A user cannot see or revoke one device.
- **Four hidden token issuers.** `UpdateMyProfile`, `CreateShop`, `SwitchActiveShop` and
  `SetDefaultShop` each mint a *new refresh token* from an access token. This is the bypass the
  review found: an access token that outlived a password reset can still be traded for a fresh
  refresh family through an ordinary profile save.
- **`ChangeMyPassword` is not an atomic security transition.** Unlike `ResetPassword` it takes no
  row lock and runs outside a transaction, so a concurrent refresh can outlive it.
- **No sessions endpoints and no web UI.**

## Scope rulings

- **Recovery epoch (plan 44's territory):** this plan adds the `RecoveryEpoch` column and an
  `IRecoveryEpochProvider` seam with a config-backed default (`Auth:RecoveryEpoch`, default `"1"`),
  and rejects sessions stamped with another epoch. Plan 44 replaces the provider with its external
  store. Cost if wrong: the fence is only as durable as configuration until 44 lands.
- **Legacy tokens:** a token with no `sid` is rejected. Access tokens live 15 minutes; refresh rows
  with a null `auth_session_id` are refused on rotation. This is the source plan's "one controlled
  cutover that requires fresh login", and it never invents a device session from a legacy token.
- **Password change forces re-authentication.** Self-service password change revokes every session
  including the acting one and mints nothing, so it shares exactly one code path with recovery.
  Cost if wrong: one extra sign-in after changing a password; no security cost.
- **MFA / step-up is not in this plan.** Nothing here should be described as implementing it.
- **`auth_sessions` carries no RLS policy**, matching `users`, `refresh_tokens` and
  `password_reset_tokens`: it is account-scoped, not shop-scoped, and it must be readable from
  `OnTokenValidated`, which runs before `HttpContext.User` (and so the RLS session context) is set.
  Per-user scoping is enforced in the repository by `UserId`.

## Design

### Domain

`AuthSession` (`src/backend/Intelibill.Domain/Entities/AuthSession.cs`):

| Field | Purpose |
|---|---|
| `Id` | the `sid` claim |
| `UserId` | owner |
| `SecurityVersion` | snapshot of `User.SecurityVersion` when the proof was accepted |
| `RecoveryEpoch` | epoch the proof was accepted under |
| `ActiveShopId` | this device's current shop scope |
| `ScopeVersion` | bumped when this session's scope changes |
| `DeviceLabel`, `UserAgent`, `CreatedFromIp` | identification only, never authentication proof |
| `CreatedAt`, `LastSeenAt`, `ExpiresAt`, `RevokedAt`, `RevocationReason` | lifecycle |

`RefreshToken` gains `AuthSessionId` (nullable in the database, required for any new row) so a
refresh family belongs to one device.

### Claims

Access tokens add `sid` and `scope_version` to the existing subject / `security_version` /
`active_shop_id` / `active_shop_role` / `language` set.

### Application

`IAuthSessionService` (`Application/Common/Interfaces`), implemented in
`Infrastructure/Services/Auth/AuthSessionService.cs` because it needs row locks:

```csharp
Task<ErrorOr<AuthResult>>        StartAsync(AuthenticationProof proof, DeviceMetadata device, Guid? preferredShopId, CancellationToken ct);
Task<ErrorOr<AuthResult>>        RotateAsync(string refreshToken, Guid? preferredShopId, CancellationToken ct);
Task<ErrorOr<AccessScopeResult>> RescopeAsync(Guid sessionId, Guid userId, Guid shopId, CancellationToken ct);
Task<ErrorOr<IReadOnlyList<AuthSessionDto>>> ListAsync(Guid userId, Guid currentSessionId, CancellationToken ct);
Task<ErrorOr<bool>>              RevokeAsync(Guid userId, Guid sessionId, CancellationToken ct);
Task<ErrorOr<int>>               RevokeAllAsync(Guid userId, Guid? exceptSessionId, string reason, CancellationToken ct);
```

`AuthenticationProof(UserId, CredentialKind, VerifiedSecurityVersion, RecoveryEpoch, VerifiedAt, ChallengeId?)`
is constructed only inside Application, by static factories on the successful side of a credential
check (`FromPassword`, `FromEmailOwnership`, `FromRegistration`). No controller references it, so no
client field can become one.

`StartAsync` runs after the expensive verification: begin transaction, `SELECT ... FOR UPDATE` the
user row, re-read enabled / verified / `SecurityVersion` / epoch, compare against the proof, resolve
shop selection, insert the session, insert the refresh row bound to it, sign the access token,
commit. A moved version or epoch rejects the proof; the latest version is never copied onto an
earlier proof.

`AccessScopeResult(AccessToken, AccessTokenExpiresAt, ActiveShopId, ScopeVersion, Shops)` is what
scope changes return — a new access token and no new refresh secret.

### Live validation

`LiveSessionValidator` additionally requires: a parseable `sid`; a session row for that id whose
`UserId` matches the subject, not revoked, not expired; `session.SecurityVersion == user.SecurityVersion`;
`session.RecoveryEpoch == current epoch`; and `scope_version` matching the row. Lock order for every
security transition stays `users → auth_sessions → refresh_tokens`.

### Api

`GET /api/users/me/sessions`, `DELETE /api/users/me/sessions/{id}`,
`POST /api/users/me/sessions/revoke-all`.

### Web

`src/frontend/src/app/features/users/services/session.service.ts` and a sessions panel on the
existing account-security page; `UpdateMyProfile` and the shop-scope calls stop expecting a refresh
token.

## Global constraints

Web/API only; mobile excluded. Domain ← Application ← Infrastructure ← Api preserved; repository
abstractions and `IUnitOfWork` for writes; versionless `csproj` references with versions in
`Directory.Packages.props`; warnings-as-errors; standalone Angular components; Bun only. Migrations
are applied by the migration job, never by API startup.

## Tasks

### Task 1 — Failing security regression suite

- Write `tests/backend/integration/Intelibill.Integration.Tests/SessionLifecycleIntegrationTests.cs`
  with `ResetPassword_OldAccessCannotMintOrMutate` as a `[Theory]` over every issuer route
  (profile save, create shop, switch shop, set default shop, token refresh) asserting 401 and no
  new refresh row, and `DisabledAccount_PasswordAndRefreshDenied`.
- Run `dotnet test tests/backend/integration/Intelibill.Integration.Tests --filter SessionLifecycleIntegrationTests`.
  **Expected:** the theory fails on the four issuer routes (they currently answer 200 and mint a
  refresh token); the disabled-account case may already pass.
- Commit the red suite.

### Task 2 — `AuthSession` entity, configuration and migration

- Domain unit tests first (`Intelibill.Domain.Unit.Tests`): a new session is active; `Revoke`
  records reason and time and clears `IsActive`; an expired session is not active;
  `ChangeScope` bumps `ScopeVersion`; `Touch` moves `LastSeenAt` only.
  **Expected:** red, `AuthSession` does not exist.
- Add the entity, `AuthSessionConfiguration`, the `DbSet`, `RefreshToken.AuthSessionId`, and
  migration `AddAuthSessions`.
- Run `dotnet test tests/backend/unit/Intelibill.Domain.Unit.Tests`. **Expected:** green.
- Run `dotnet ef migrations list --project src/backend/Intelibill.Infrastructure --startup-project src/backend/Intelibill.Api`.
  **Expected:** `AddAuthSessions` listed last.

### Task 3 — Claims, session service and live validation

- `Intelibill.Api.Unit.Tests` first: the access token carries `sid` and `scope_version`.
  **Expected:** red.
- Add the claims to `TokenService`/`ITokenService`, `IAuthSessionService` + `AuthSessionService`,
  `IRecoveryEpochProvider`, and extend `LiveSessionValidator` to require a live session row.
- Run `dotnet test tests/backend/unit/Intelibill.Api.Unit.Tests`. **Expected:** green.

### Task 4 — Credential paths create sessions

- Rewrite `Login`, `RegisterWithPhone`, `RefreshToken` and `EmailOwnershipService` confirmation to
  build an `AuthenticationProof` and call `StartAsync`/`RotateAsync`.
- Integration tests: `PasswordLoginRacesReset_OldProofCannotStartSession`,
  `PasswordLoginRacesDisable_CannotStartSession`,
  `EmailOwnershipProofVersionChanged_RequiresFreshAuthentication`,
  `RestoredSessionOrProof_OldRecoveryEpochDenied`, each pausing after verification, committing the
  competing change, then resuming. **Expected:** red first, then green.
- Run the Application unit suite. **Expected:** green after the handler tests are updated.

### Task 5 — Remove issuance from non-credential handlers

- `UpdateMyProfile` returns `UserDto`. `CreateShop`, `SwitchActiveShop` and `SetDefaultShop` return
  `AccessScopeResult` from `RescopeAsync`, changing only the requesting session's scope; the
  default-shop preference update becomes independent of active scope.
- Tests: `ProfileUpdate_DoesNotCreateSession`, `SwitchShop_ChangesOnlyRequestingSessionScope`, plus
  the updated unit tests for all four handlers.
- Run `dotnet test tests/backend/unit/Intelibill.Application.Unit.Tests` and the Task 1 suite.
  **Expected:** both green; Task 1's theory now passes on every route.

### Task 6 — Atomic password change and reset

- `ChangeMyPassword` runs inside a transaction, locks the user row, verifies the current password
  against the locked row, bumps `SecurityVersion` and revokes every session and refresh row.
  `ResetPassword` revokes sessions alongside the refresh tokens it already revokes.
- Tests: `ResetRacingRefresh_NoPostResetLiveSession`, `Reenable_DoesNotReviveRevokedSession`,
  `ResetToken_ConcurrentUse_OnlyOnePasswordWins`, `ChangePassword_RevokesEverySession`, each
  asserting persisted state after two independent requests rather than mock calls.

### Task 7 — Sessions endpoints

- `SessionsController` with the three routes; `SessionList_CannotReadOrRevokeAnotherUser`;
  current-session labelling in the response.

### Task 8 — Web contract and sessions UI

- `session.service.ts` + spec; sessions panel on the account-security page; profile and shop-scope
  callers updated to the new payloads; clear stored auth on revoke-all without discarding unsynced
  invoices.
- From `src/frontend`: `bun run test`. **Expected:** green.

### Task 9 — Whole-suite verification

- `dotnet build src/backend/Intelibill.slnx`, the three unit suites, the integration suite, and
  `bun run test`. **Expected:** all green, warnings-as-errors clean.

## Review focus

1. Old access after recovery reaches no issuer, including the four hidden ones.
2. Reset versus refresh race commits as one boundary.
3. Re-enabling an account does not revive a revoked session.
4. A replayed reset proof cannot produce last-writer-wins on the password.
5. A shop switch on one device never changes another device's scope.
6. A token with no `sid`, a forged `sid`, and a `sid` belonging to another subject are all refused.
