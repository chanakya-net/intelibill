# intelibill

AI-powered inventory management system.

## Build Status

| | Backend | Frontend |
|---|---|---|
| Build | [![Backend CI](.github/badges/backend-build.svg)](https://github.com/chanakya-net/intelibill/actions/workflows/backend-main-ci.yml) | [![Frontend CI](.github/badges/frontend-build.svg)](https://github.com/chanakya-net/intelibill/actions/workflows/frontend-main-ci.yml) |
| Tests | [![Tests](.github/badges/tests.svg)](https://github.com/chanakya-net/intelibill/actions/workflows/backend-main-ci.yml) | [![Frontend Tests](.github/badges/frontend-tests.svg)](https://github.com/chanakya-net/intelibill/actions/workflows/frontend-main-ci.yml) |

## Tech Stack

| Layer | Technology |
|---|---|
| Backend | ASP.NET Core 10, C# latest, .NET 10.0.105 |
| Database | PostgreSQL via Npgsql + EF Core 10 |
| Messaging / CQRS | Wolverine 5.24 |
| Validation | FluentValidation 12 |
| Error handling | ErrorOr 2.0 |
| Frontend | Angular 21, SSR, PWA, Tailwind CSS 4, PrimeNG 21, NgRx 21, Bun |
| Mobile | .NET MAUI *(not yet started)* |

## Repository Structure

```
intelibill/
├── .claude/docs/               # Architecture & pattern docs
├── src/
│   ├── backend/                # ASP.NET Core — Clean Architecture
│   │   ├── Intelibill.Domain/
│   │   ├── Intelibill.Application/
│   │   ├── Intelibill.Infrastructure/
│   │   └── Intelibill.Api/
│   ├── frontend/               # Angular (Bun, SSR, PWA, NgRx)
│   └── mobile/                 # .NET MAUI (scaffolding only)
├── tests/
│   ├── backend/unit/
│   ├── backend/integration/
│   └── frontend/
├── Directory.Build.props
├── Directory.Packages.props
└── global.json
```

See [src/backend/CLAUDE.md](src/backend/CLAUDE.md) for backend architecture and [docs/architectural_patterns.md](docs/architectural_patterns.md) for design patterns.

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (10.0.105+)
- PostgreSQL running locally
- [Bun](https://bun.sh/) 1.3+

### Backend

```bash
dotnet build src/backend/Intelibill.slnx

# Edit src/backend/Intelibill.Api/appsettings.Development.json with DB credentials

dotnet ef database update \
  --project src/backend/Intelibill.Infrastructure \
  --startup-project src/backend/Intelibill.Api

dotnet run --project src/backend/Intelibill.Api
```

API: `http://localhost:5202` — OpenAPI: `http://localhost:5202/openapi/v1.json` (dev only)

### Frontend

```bash
cd src/frontend
bun install
bun run start
```

### Docker

```bash
# DB only (for local backend dev)
docker compose up -d

# Full stack
docker compose --profile full up -d
```

| Service  | URL |
|---|---|
| Backend  | `http://localhost:8080` |
| Frontend | `http://localhost:4000` |
| PostgreSQL | `localhost:5432` |

```bash
# Rebuild after code changes
docker compose --profile full up -d --build

# Tear down (add -v to wipe the database)
docker compose --profile full down
```
