# Backend Architecture

> High-level architecture reference. For implementation patterns (DI, Repository, ErrorOr, Options, etc.) see [`docs/architectural_patterns.md`](architectural_patterns.md). For build commands, config, and test snapshots see [`AGENTS.md`](../AGENTS.md) at repo root.

## Stack

| Concern | Choice |
|---|---|
| Platform | .NET 10, ASP.NET Core, C# latest |
| Architecture | Clean Architecture / Onion (`Domain → Application → Infrastructure → Api`) |
| Request handling | Wolverine 5.24 message bus — command/query handlers in Application |
| Validation | FluentValidation 12 — validators auto-discovered from Application assembly |
| Data access | EF Core 10 + PostgreSQL (snake_case), repository + unit-of-work abstractions |
| Error flow | ErrorOr 2.0 result pattern mapped to HTTP ProblemDetails at API boundary |
| Tenancy | Multi-shop isolation: JWT `active_shop_id` + PostgreSQL RLS session context |
| Testing | xUnit 2.9 + coverlet; integration via Testcontainers (real PostgreSQL) |

## Layer Responsibilities

| Layer | Path | Responsibility |
|---|---|---|
| Domain | `src/backend/Intelibill.Domain/` | Entities, value objects, domain events, repository interfaces — zero dependencies |
| Application | `src/backend/Intelibill.Application/` | Wolverine handlers, validators, use cases, error definitions — depends on Domain only |
| Infrastructure | `src/backend/Intelibill.Infrastructure/` | EF Core DbContext, repositories, migrations, auth/token services, `PostgresSessionContextInterceptor` for RLS |
| Api | `src/backend/Intelibill.Api/` | ASP.NET Core host, controllers, global exception middleware, DI wiring |

## Request Flow

```
HTTP Request
  → Controller
  → Wolverine (ValidationBehaviour runs FluentValidation)
  → Application Handler (returns ErrorOr<T>)
  → Infrastructure (repositories, UoW, SaveChangesAsync)
  → ErrorOr maps to ProblemDetails / HTTP response
```

For authenticated requests, `active_shop_id` is extracted from JWT and set as a PostgreSQL session variable (`app.active_shop_id`) by the `PostgresSessionContextInterceptor`, enabling RLS row filtering.

## Key Functional Areas

- **Auth**: verified email enrollment, phone registration, password reset, token refresh/revoke. There is no external identity provider: an account is created only by proving ownership of its own email address or phone number.
- **Shops**: create/update shop, list memberships, switch active shop, set default shop; `GET /api/shops/me`
- **Items**: catalog CRUD + SSE streaming endpoint (`GET /api/items/stream`)
- **Inventory**: inbound stock (single + batch), batch management, void/reassign
- **Sales**: record sale, list/detail, profit-loss report, cost tracking per batch at sale time
- **Customers**: CRUD + credit ledger + payment recording
- **Suppliers**: CRUD + purchase ledger + payment recording, status tracking
- **Expenses**: record + correct + categorise
- **Bank Accounts**: CRUD per shop
- **Users**: shop user management (add/edit/roles), profile self-service, password change
- **Security**: JWT bearer, `active_shop_id` claim, RLS, 403 mapping, rate limiting middleware (`RateLimitAttribute` / `RateLimitFilter`)

## Multi-Shop Tenancy

- `Shop`, `ShopMembership`, `ShopRole` entities in Domain
- Migration `20260425075353_InitialCreate` — consolidated schema including RLS policies on `shops` / `shop_memberships`
- `PostgresSessionContextInterceptor` sets `app.current_user_id` and `app.active_shop_id` per request
- Auth responses include `activeShopId` and accessible shop list

## Refresh Credentials

A refresh credential renews one device session (`AuthSession`) and is single-use.

- **Storage.** The client receives 64 random bytes as Base64Url. `refresh_tokens.token_hash` keeps the
  hex SHA-256 of that string (`RefreshTokenSecrets`); the secret itself is never stored or logged.
  Request, command and `AuthResult` records leave bearer credentials out of their `ToString()`.
- **Rotation** (`AuthSessionService.RotateAsync`). Look the credential up by hash, then lock
  `users` → `auth_sessions` → `refresh_tokens` (the order every security transition takes) and decide
  only on values read under those locks. The presented credential is marked `consumed_at` and exactly
  one successor is linked to it (`parent_token_id`, unique). The access token is signed before commit,
  so a signing or database failure leaves no successor.
- **Reuse.** The session is the rotation family. A consumed credential presented again — to renew or to
  sign out — ends its session and every credential it issued (`refresh-reuse`), commits that, then
  returns the generic `Auth.InvalidRefreshToken`. The warning log names session, credential and user
  ids only. Unknown, revoked and expired (`now >= ExpiresAt`) credentials are refused and end nothing.
- **Browser.** `RefreshCoordinator` serializes renewals per user across tabs (Web Locks API, bounded
  localStorage lease fallback); keyed by user because a restored tab may not know its session id. A tab
  that waited adopts the successor another tab stored for the same user and session. A refusal is final
  and is never followed by adopting stored state. The refresh request times out after 12 s (treated as
  a lost connection), and a result that arrives after the tab signed out or switched account is
  dropped rather than applied.

**Support answer — "I was signed out after a refresh."** Policy is strict: there is no grace window and
no replayable copy of a successor is kept. If a renewal reached the server but its response was lost
(network drop, closed laptop lid), the browser still holds the spent credential and the next attempt
ends that device's session; the person signs in again. The same happens if a copy of the credential is
used elsewhere — which is the point. Other devices on the account are unaffected.

**Schema rollout.** `20261101090000_HashRefreshTokensAndChainRotations` is the expand step: `token`
stays (nullable, emptied) and `is_revoked` stays in step with `revoked_at` while the previous revision
serves. Every pre-existing credential and live session is revoked, so everyone signs in once. The
contract migration (separate change) re-runs the hash-and-revoke step for rows the previous revision
wrote during the deploy window, ends the sessions that hold no hashed credential, drops `token` and its
index, and makes `token_hash` required.

## Test Structure

| Suite | Path | Count |
|---|---|---|
| Domain unit | `tests/backend/unit/Intelibill.Domain.Unit.Tests/` | 83 |
| Application unit | `tests/backend/unit/Intelibill.Application.Unit.Tests/` | 330 |
| API unit | `tests/backend/unit/Intelibill.Api.Unit.Tests/` | 139 |
| Integration | `tests/backend/integration/Intelibill.Integration.Tests/` | 115 |
| **Total** | | **667** |

Integration tests use `WebApplicationFactory<Program>` backed by **Testcontainers** (`Testcontainers.PostgreSql`) — Docker required. Non-integration suite: 552+ passing.
