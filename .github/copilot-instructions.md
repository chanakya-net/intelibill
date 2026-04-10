# Project Guidelines

## Scope
Apply to all Copilot/Copilot agent requests in workspace.

## Build And Test
- Backend commands run from repo root.
- Preferred full backend build: dotnet build src/backend/Intelibill.slnx
- Preferred full backend test: dotnet test src/backend/Intelibill.slnx
- Targeted backend tests:
  - dotnet test tests/backend/unit/Intelibill.Domain.Unit.Tests
  - dotnet test tests/backend/unit/Intelibill.Application.Unit.Tests
  - dotnet test tests/backend/unit/Intelibill.Api.Unit.Tests
  - dotnet test tests/backend/integration/Intelibill.Integration.Tests
- Frontend commands run from src/frontend.
- Frontend package manager: Bun only (not npm/yarn).
- Preferred frontend commands:
  - bun install
  - bun run start
  - bun run build
  - bun run test

## Architecture Boundaries
- Backend layer direction: Domain <- Application <- Infrastructure <- Api.
- Domain: no deps on other layers/frameworks.
- Application: depends only on Domain.
- Infrastructure: depends on Domain + Application.
- Api: depends on Application + Infrastructure.
- Dependency registration in each layer's DependencyInjection.cs.

## Backend Conventions
- Use ErrorOr result pattern for app flows; no exception-driven control flow in handlers.
- Define/reuse domain/app errors in Application/Common/Errors/Errors.cs.
- Use FluentValidation for request validation.
- Use repository abstractions + IUnitOfWork for writes from Application handlers.
- EF Core entity mapping in Infrastructure/Data/Configurations via IEntityTypeConfiguration<T>.
- No data annotations on domain entities.
- C# warnings = errors; keep builds analyzer-clean.
- NuGet versioning via Central Package Management:
  - Versions only in Directory.Packages.props.
  - csproj PackageReference entries versionless.

## Frontend Conventions
- Angular standalone components (no NgModules for new work).
- Use NgRx patterns in features + root store.
- Use PrimeNG + existing styling conventions in src/frontend/src/app.
- i18n keys in locale files under src/frontend/public/assets/i18n.

## Multi-Shop Tenancy Rules
- Preserve active shop scoping.
- Backend auth/session relies on `active_shop_id` claim.
- Don't bypass RLS-aware request/session context in Infrastructure.

## Environment Pitfalls
- .NET SDK pinned by global.json (10.0.105, latestMinor roll-forward).
- Local backend needs PostgreSQL creds in src/backend/Intelibill.Api/appsettings.Development.json.
- Quick local DB: docker compose up -d
- Frontend tests via Bun from src/frontend.

## Link Targets
Link these; don't duplicate:
- README.md
- CLAUDE.md
- docs/architectural_patterns.md
- docs/backend-architecture.md
- docs/frontend-architecture.md
- Directory.Build.props
- Directory.Packages.props
- global.json