# Backend Architecture

> High-level architecture reference. For implementation patterns (DI, Repository, ErrorOr, Options, etc.) see [`docs/architectural_patterns.md`](architectural_patterns.md). For build commands, config, and test snapshots see [`src/backend/CLAUDE.md`](../src/backend/CLAUDE.md).

## Stack

| Concern | Choice |
|---|---|
| Platform | .NET 10, ASP.NET Core, C# latest |
| Architecture | Clean Architecture / Onion (`Domain → Application → Infrastructure → Api`) |
| Request handling | Wolverine message bus — command/query handlers in Application |
| Validation | FluentValidation — validators auto-discovered from Application assembly |
| Data access | EF Core 10 + PostgreSQL (snake_case), repository + unit-of-work abstractions |
| Error flow | ErrorOr result pattern mapped to HTTP ProblemDetails at API boundary |
| Tenancy | Multi-shop isolation: JWT `active_shop_id` + PostgreSQL RLS session context |

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

- **Auth**: register/login (email), refresh/revoke token
- **Shops**: create shop, list memberships (`GET /api/shops/me`), switch active shop, set default shop
- **Security**: JWT bearer, `active_shop_id` claim, 403 mapping for membership violations

## Multi-Shop Tenancy

- `Shop`, `ShopMembership`, `ShopRole` entities in Domain
- Migration `20260327181741_AddShopIsolation` — tables `shops`, `shop_memberships` with RLS policies
- Session context interceptor sets `app.current_user_id` and `app.active_shop_id` per request
- Auth responses include `activeShopId` and accessible shop list

## Test Structure

| Suite | Path |
|---|---|
| Domain unit | `tests/backend/unit/Intelibill.Domain.Unit.Tests/` |
| Application unit | `tests/backend/unit/Intelibill.Application.Unit.Tests/` |
| API unit | `tests/backend/unit/Intelibill.Api.Unit.Tests/` |
| Integration | `tests/backend/integration/Intelibill.Integration.Tests/` |

Integration tests use `WebApplicationFactory<Program>` with in-memory SQLite for full pipeline coverage.
