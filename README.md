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
| Coverage (Latest) | ![Coverage](.github/badges/coverage.svg) |

</div>

<div>

### Frontend Build Status

| Metric | Status |
|---|---|
| Frontend Build Status | [![Frontend Main CI](https://img.shields.io/github/actions/workflow/status/chanakya-net/intelibill/frontend-main-ci.yml?branch=main&label=frontend%20build)](https://github.com/chanakya-net/intelibill/actions/workflows/frontend-main-ci.yml) |
| Frontend Tests (Latest) | ![Frontend Tests](.github/badges/frontend-tests.svg) |
| Frontend Coverage (Latest) | ![Frontend Coverage](.github/badges/frontend-coverage.svg) |

</div>

</div>

## Test Results

### Backend Test Cases

<!-- TEST_BREAKDOWN_START -->
| Project | Tests Passed | Failed | Coverage |
|---|---:|---:|---:|
| Intelibill.Integration.Tests | 2/2 | 0 | 7.1% |
| Intelibill.Api.Unit.Tests | 22/22 | 0 | 5.1% |
| Intelibill.Application.Unit.Tests | 25/25 | 0 | 75.0% |
| Intelibill.Domain.Unit.Tests | 22/22 | 0 | 94.3% |
| **Total** | **71/71** | **0** | **25.1%** |
<!-- TEST_BREAKDOWN_END -->

### Frontend Test Cases

<!-- FRONTEND_TEST_BREAKDOWN_START -->
| Project | Test Type | Tests | Status | Coverage |
|---|---|---:|:---:|---:|
| INVENTORY | Unit | 0/0 passed | ✓ | 100.0% |
<!-- FRONTEND_TEST_BREAKDOWN_END -->

# intelibill

AI-powered inventory management system.

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
