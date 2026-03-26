# Build Status

| Metric | Status |
|---|---|
| Overall Build Status | [![Backend Main CI](https://img.shields.io/github/actions/workflow/status/chanakya-net/inventory.ai/backend-main-ci.yml?branch=main&label=overall%20build)](https://github.com/chanakya-net/inventory.ai/actions/workflows/backend-main-ci.yml) |
| Build Pass Status | [![Backend Build](https://img.shields.io/github/actions/workflow/status/chanakya-net/inventory.ai/backend-main-ci.yml?branch=main&label=build)](https://github.com/chanakya-net/inventory.ai/actions/workflows/backend-main-ci.yml) |
| Test Pass Status | [![Backend Tests](https://img.shields.io/github/actions/workflow/status/chanakya-net/inventory.ai/backend-main-ci.yml?branch=main&label=tests)](https://github.com/chanakya-net/inventory.ai/actions/workflows/backend-main-ci.yml) |
| Tests Run (Latest) | ![Tests Run](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/chanakya-net/inventory.ai/main/.github/badges/tests.json) |
| Coverage (Latest) | ![Coverage](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/chanakya-net/inventory.ai/main/.github/badges/coverage.json) |

### Latest Backend Test Breakdown

<!-- TEST_BREAKDOWN_START -->
| Project | Tests Passed | Failed | Coverage |
|---|---:|---:|---:|
| InventoryAI.Integration.Tests | 1/1 | 0 | 0.0% |
| InventoryAI.Api.Unit.Tests | 14/14 | 0 | 5.0% |
| InventoryAI.Application.Unit.Tests | 23/23 | 0 | 77.3% |
| InventoryAI.Domain.Unit.Tests | 1/1 | 0 | 0.0% |
| **Total** | **39/39** | **0** | **9.9%** |
<!-- TEST_BREAKDOWN_END -->

# inventory.ai

AI-powered inventory management system.

## Tech Stack

| Layer | Technology |
|---|---|
| Backend | ASP.NET Core 10, C# latest, .NET 10.0.105 |
| Database | PostgreSQL via Npgsql + EF Core 10 |
| Messaging / CQRS | Wolverine 5.24 |
| Validation | FluentValidation 12 |
| Error handling | ErrorOr 2.0 (result pattern) |
| Frontend | Angular PWA *(not yet started)* |
| Mobile | .NET MAUI *(not yet started)* |

## Repository Structure

```
inventory.ai/
├── .github/workflows/          # CI/CD pipelines (pending)
├── .claude/docs/               # Architecture & pattern docs for AI assistance
├── docs/                       # Project documentation
├── src/
│   ├── backend/                # ASP.NET Core — Clean Architecture / Onion Design
│   │   ├── InventoryAI.slnx
│   │   ├── InventoryAI.Domain/         # Entities, value objects, domain interfaces
│   │   ├── InventoryAI.Application/    # Handlers (Wolverine), validators, use cases
│   │   ├── InventoryAI.Infrastructure/ # EF Core, repositories, PostgreSQL
│   │   └── InventoryAI.Api/            # ASP.NET Core host, controllers, middleware
│   ├── frontend/               # Angular PWA (scaffolding only)
│   └── mobile/                 # .NET MAUI (scaffolding only)
├── tests/
│   ├── backend/
│   │   ├── unit/
│   │   │   ├── InventoryAI.Domain.Unit.Tests/
│   │   │   └── InventoryAI.Application.Unit.Tests/
│   │   └── integration/
│   │       └── InventoryAI.Integration.Tests/
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

### Backend

```bash
# From repo root

# Restore & build
dotnet build src/backend/InventoryAI.slnx

# Configure local database (gitignored)
# Edit src/backend/InventoryAI.Api/appsettings.Development.json

# Apply migrations
dotnet ef database update \
  --project src/backend/InventoryAI.Infrastructure \
  --startup-project src/backend/InventoryAI.Api

# Run
dotnet run --project src/backend/InventoryAI.Api

# Test
dotnet test src/backend/InventoryAI.slnx
```

API is available at `http://localhost:5202`. OpenAPI docs at `http://localhost:5202/openapi/v1.json` (Development only).

### Frontend

*Not yet started.*

### Mobile

*Not yet started.*
