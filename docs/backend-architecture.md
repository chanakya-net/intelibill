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

- **Auth**: email + phone registration, external OAuth (Google/Facebook), password reset, token refresh/revoke
- **Shops**: create/update shop, list memberships, switch active shop, set default shop; `GET /api/shops/me`
- **Items**: catalog CRUD + SSE streaming endpoint (`GET /api/items/stream`)
- **Inventory**: inbound stock (single + batch), batch management, void/reassign
- **Sales**: record sale, list/detail, profit-loss report
- **Customers**: CRUD + credit ledger + payment recording
- **Suppliers**: CRUD + purchase ledger + payment recording
- **Expenses**: record + correct + categorise
- **Bank Accounts**: CRUD per shop
- **Users**: shop user management (add/edit), profile self-service, password change
- **Security**: JWT bearer, `active_shop_id` claim, RLS, 403 mapping, rate limiting middleware

## Multi-Shop Tenancy

- `Shop`, `ShopMembership`, `ShopRole` entities in Domain
- Migration `20260425075353_InitialCreate` — consolidated schema including RLS policies on `shops` / `shop_memberships`
- `PostgresSessionContextInterceptor` sets `app.current_user_id` and `app.active_shop_id` per request
- Auth responses include `activeShopId` and accessible shop list

## Test Structure

| Suite | Path | Count |
|---|---|---|
| Domain unit | `tests/backend/unit/Intelibill.Domain.Unit.Tests/` | 83 |
| Application unit | `tests/backend/unit/Intelibill.Application.Unit.Tests/` | 330 |
| API unit | `tests/backend/unit/Intelibill.Api.Unit.Tests/` | 139 |
| Integration | `tests/backend/integration/Intelibill.Integration.Tests/` | 115 |

Integration tests use `WebApplicationFactory<Program>` backed by **Testcontainers** (`Testcontainers.PostgreSql`) — Docker required.
