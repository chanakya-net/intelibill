# Backend Architecture Snapshot

Purpose: quick architecture context for humans and AI.

## Stack and Core Patterns
- Platform: .NET 10, ASP.NET Core API, C#.
- Architecture: clean/layered (`Domain -> Application -> Infrastructure -> Api`).
- Request handling: Wolverine message bus with command/query handlers in Application.
- Validation: FluentValidation validators discovered from Application assembly.
- Data access: EF Core + PostgreSQL (snake_case), repository + unit-of-work abstractions.
- Error flow: ErrorOr result pattern mapped to HTTP ProblemDetails.
- Tenancy model: multi-shop isolation using JWT `active_shop_id` + PostgreSQL RLS session context.

## Solution Layout (Backend)
- `src/backend/Intelibill.Domain/`
  - Entities, enums, domain events, repository interfaces, no infrastructure concerns.
- `src/backend/Intelibill.Application/`
  - Feature handlers (auth, shops), DTOs, validators, app interfaces.
- `src/backend/Intelibill.Infrastructure/`
  - `ApplicationDbContext`, EF configs/migrations, repositories, auth/token services, interceptors.
  - `PostgresSessionContextInterceptor` sets DB session vars for RLS.
- `src/backend/Intelibill.Api/`
  - Host/bootstrap (`Program.cs`), controllers, middleware, options, session context adapter.

## API/Runtime Flow (High Level)
1. Controller receives HTTP request and sends command/query through Wolverine.
2. Application handler validates input and executes business logic.
3. Infrastructure repositories + unit-of-work persist changes.
4. ErrorOr result maps to HTTP response via API extension methods.
5. For authenticated requests, active shop context is propagated into JWT and DB session.

## Key Functional Areas
- Auth: register/login (email + external), refresh/revoke token, password reset.
- Shops: create shop, list memberships (`/api/shops/me`), switch active shop, set default shop.
- Security: JWT bearer auth, active shop claim, forbidden mapping for membership violations.

## Test Locations
- Solution reference file: `src/backend/Intelibill.slnx`.
- Unit tests:
  - `tests/backend/unit/Intelibill.Api.Unit.Tests/`
  - `tests/backend/unit/Intelibill.Application.Unit.Tests/`
  - `tests/backend/unit/Intelibill.Domain.Unit.Tests/`
- Integration tests:
  - `tests/backend/integration/Intelibill.Integration.Tests/`
  - Uses `WebApplicationFactory<Program>` with in-memory SQLite for pipeline/integration coverage.

## Practical Notes
- Shared build/package governance is centralized at repo root:
  - `Directory.Build.props`
  - `Directory.Packages.props`
  - `global.json`
