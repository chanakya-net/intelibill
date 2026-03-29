# Build Status

<div style="display: grid; grid-template-columns: 1fr 1fr; gap: 20px;">

<div>

### Backend Build Status

| Metric | Status |
|---|---|
| Overall Build Status | [![Backend Main CI](https://img.shields.io/github/actions/workflow/status/chanakya-net/intelibill/backend-main-ci.yml?branch=main&label=overall%20build)](https://github.com/chanakya-net/intelibill/actions/workflows/backend-main-ci.yml) |
| Build Pass Status | [![Backend Build](https://img.shields.io/github/actions/workflow/status/chanakya-net/intelibill/backend-main-ci.yml?branch=main&label=build)](https://github.com/chanakya-net/intelibill/actions/workflows/backend-main-ci.yml) |
| Test Pass Status | [![Backend Tests](https://img.shields.io/github/actions/workflow/status/chanakya-net/intelibill/backend-main-ci.yml?branch=main&label=tests)](https://github.com/chanakya-net/intelibill/actions/workflows/backend-main-ci.yml) |
| Tests Run (Latest) | ![Tests Run](.github/badges/tests.svg) |

</div>

<div>

### Frontend Build Status

| Metric | Status |
|---|---|
| Frontend Build Status | [![Frontend Main CI](https://img.shields.io/github/actions/workflow/status/chanakya-net/intelibill/frontend-main-ci.yml?branch=main&label=frontend%20build)](https://github.com/chanakya-net/intelibill/actions/workflows/frontend-main-ci.yml) |
| Frontend Tests (Latest) | ![Frontend Tests](.github/badges/frontend-tests.svg) |

</div>

</div>

<<<<<<< ours
||||||| ancestor
## Test Results

### Backend Test Cases

<!-- TEST_BREAKDOWN_START -->
| Project | Tests Passed | Failed | Coverage |
|---|---:|---:|---:|
| Intelibill.Integration.Tests | 15/15 | 0 | 22.6% |
| Intelibill.Api.Unit.Tests | 62/62 | 0 | 8.4% |
| Intelibill.Application.Unit.Tests | 40/40 | 0 | 74.0% |
| Intelibill.Domain.Unit.Tests | 25/25 | 0 | 92.2% |
| **Total** | **142/142** | **0** | **38.0%** |
<!-- TEST_BREAKDOWN_END -->

### Frontend Test Cases

<!-- FRONTEND_TEST_BREAKDOWN_START -->
| Project | Tests | Coverage |
|---|---|---:|
| INVENTORY | 67/67 passed | 100.0% |
<!-- FRONTEND_TEST_BREAKDOWN_END -->
=======
## Test Results

### Backend Test Cases

<!-- TEST_BREAKDOWN_START -->
| Project | Tests Passed | Failed | Coverage |
|---|---:|---:|---:|
| Intelibill.Integration.Tests | 15/15 | 0 | 22.6% |
| Intelibill.Api.Unit.Tests | 62/62 | 0 | 8.4% |
| Intelibill.Application.Unit.Tests | 40/40 | 0 | 74.0% |
| Intelibill.Domain.Unit.Tests | 25/25 | 0 | 92.2% |
| **Total** | **142/142** | **0** | **38.0%** |
<!-- TEST_BREAKDOWN_END -->

### Frontend Test Cases

<!-- FRONTEND_TEST_BREAKDOWN_START -->
| Project | Tests | Coverage |
|---|---|---:|
| INVENTORY | 66/66 passed | 100.0% |
<!-- FRONTEND_TEST_BREAKDOWN_END -->
>>>>>>> theirs

# intelibill

AI-powered inventory management system.

## Current Overview (March 2026)

The backend is now running a multi-shop tenancy model with database-backed isolation primitives.

- One user can belong to multiple shops through role-based membership (`Owner`, `Manager`, `Staff`).
- Users can set a default shop and switch active shop.
- Access tokens now include `active_shop_id` claim and are rotated when switching shops.
- PostgreSQL migration includes shop tables and Row Level Security (RLS) policies for shop access boundaries.
- API now includes shop management endpoints (`/api/shops/me`, `/api/shops`, `/api/shops/switch`, `/api/shops/default`).
- Domain, application, API, and integration tests were expanded for tenancy behavior and auth scope validation.

## Coverage Improvements (March 2026)

Recent backend coverage work focused on API behavior, error mapping, and startup/pipeline execution.

- Expanded Intelibill.Api.Unit.Tests for controller edge paths in auth and shops flows.
- Added dedicated API unit tests for:
  - ErrorOr to ProblemDetails/status mapping.
  - Global exception middleware branch handling.
  - App option validation behavior.
- Expanded Intelibill.Integration.Tests with additional shop/auth handler scenarios.
- Added WebApplicationFactory-based HTTP pipeline tests using an in-memory SQLite test host to cover:
  - Development OpenAPI endpoint exposure.
  - Authorization challenge behavior for protected endpoints.
  - End-to-end register/login flow through the full API pipeline.
- Integration test host notes:
  - Replace the production DbContext registration and remove existing DbContext options configuration.
  - Keep snake_case naming enabled for SQLite test schema compatibility with filtered indexes.

## Tech Stack

| Layer | Technology |
|---|---|
| Backend | ASP.NET Core 10, C# latest, .NET 10.0.105 |
| Database | PostgreSQL via Npgsql + EF Core 10 |
| Messaging / CQRS | Wolverine 5.24 |
| Validation | FluentValidation 12 |
| Error handling | ErrorOr 2.0 (result pattern) |
| Frontend | Angular 21 (standalone), SSR + hydration, PWA, Tailwind CSS 4, PrimeNG 21, NgRx 21, Bun |
| Mobile | .NET MAUI *(not yet started)* |

## Repository Structure

```
intelibill/
├── .github/workflows/          # CI/CD pipelines (pending)
├── .claude/docs/               # Architecture & pattern docs for AI assistance
├── docs/                       # Project documentation
├── src/
│   ├── backend/                # ASP.NET Core — Clean Architecture / Onion Design
│   │   ├── Intelibill.slnx
│   │   ├── Intelibill.Domain/         # Entities, value objects, domain interfaces
│   │   ├── Intelibill.Application/    # Handlers (Wolverine), validators, use cases
│   │   ├── Intelibill.Infrastructure/ # EF Core, repositories, PostgreSQL
│   │   └── Intelibill.Api/            # ASP.NET Core host, controllers, middleware
│   ├── frontend/               # Angular frontend app (Bun, SSR, PWA, NgRx)
│   └── mobile/                 # .NET MAUI (scaffolding only)
├── tests/
│   ├── backend/
│   │   ├── unit/
│   │   │   ├── Intelibill.Domain.Unit.Tests/
│   │   │   └── Intelibill.Application.Unit.Tests/
│   │   └── integration/
│   │       └── Intelibill.Integration.Tests/
│   ├── frontend/
│   └── mobile/
├── Directory.Build.props       # Shared MSBuild settings (all projects)
├── Directory.Packages.props    # Central NuGet package versions
└── global.json                 # Pins .NET SDK to 10.0.105
```

## Backend Architecture

The backend follows **Clean Architecture** with an Onion design. Dependencies point strictly inward:

```
Domain  ←  Application  ←  Infrastructure  ←  Api
```

| Layer | Responsibility |
|---|---|
| `Domain` | Entities, value objects, domain events, repository/UoW interfaces |
| `Application` | Wolverine handlers, FluentValidation validators, ErrorOr error definitions |
| `Infrastructure` | EF Core `DbContext`, repository implementations, PostgreSQL, options binding |
| `Api` | ASP.NET Core host, controllers, global exception middleware, DI wiring |

### Multi-Shop Tenancy Design

- **Core entities**: `Shop`, `ShopMembership`, and user membership links.
- **Auth context**: access token includes `active_shop_id` and auth response includes active shop + memberships.
- **Selection rules**:
  - single shop user gets automatic default selection
  - multi-shop user can set default and switch active shop
- **DB isolation foundation**:
  - migration `20260327181741_AddShopIsolation`
  - RLS enabled for `shops` and `shop_memberships`
  - session context keys are set as `app.current_user_id` and `app.active_shop_id`

### Key Backend Endpoints

- Auth: `POST /api/auth/register/email`, `POST /api/auth/login/email`, `POST /api/auth/token/refresh`
- Shops: `GET /api/shops/me`, `POST /api/shops`, `POST /api/shops/switch`, `POST /api/shops/default`

See [src/backend/CLAUDE.md](src/backend/CLAUDE.md) for build commands and [.claude/docs/architectural_patterns.md](.claude/docs/architectural_patterns.md) for design patterns.

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (10.0.105+)
- PostgreSQL running locally
- [Node.js](https://nodejs.org/) (Angular CLI runtime)
- [Bun](https://bun.sh/) 1.3+

### Backend

```bash
# From repo root

# Restore & build
dotnet build src/backend/Intelibill.slnx

# Configure local database (gitignored)
# Edit src/backend/Intelibill.Api/appsettings.Development.json

# Apply migrations
dotnet ef database update \
  --project src/backend/Intelibill.Infrastructure \
  --startup-project src/backend/Intelibill.Api

# Run
dotnet run --project src/backend/Intelibill.Api

# Test
dotnet test src/backend/Intelibill.slnx
```

API is available at `http://localhost:5202`. OpenAPI docs at `http://localhost:5202/openapi/v1.json` (Development only).

### Frontend

```bash
# From repo root
cd src/frontend

# Install dependencies
bun install

# Run development server
bun run start

# Build (browser + server bundles)
bun run build

# Run tests
bun run test --watch=false

# Serve built SSR app
bun run serve:ssr:INVENTORY
```

Frontend stack highlights:

- Standalone component architecture with lazy-loaded feature routes
- Feature-first NgRx state architecture with root store wiring
- Tailwind CSS + PrimeNG enterprise UI baseline
- SSR-first rendering with hydration
- PWA support with service worker + manifest
- Responsive layout for mobile, tablet, and desktop

### Mobile

*Not yet started.*
