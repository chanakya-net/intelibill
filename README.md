# intelibill

AI-powered inventory management system.

## Build Status

| | Backend | Frontend | Mobile |
|---|---|---|---|
| Build | [![Backend CI](.github/badges/backend-build.svg)](https://github.com/chanakya-net/intelibill/actions/workflows/deploy.yml) | [![Frontend CI](.github/badges/frontend-build.svg)](https://github.com/chanakya-net/intelibill/actions/workflows/deploy.yml) | [![Mobile CI](.github/badges/mobile-build.svg)](https://github.com/chanakya-net/intelibill/actions/workflows/mobile-main-ci.yml) |
| Tests | [![Tests](.github/badges/tests.svg)](https://github.com/chanakya-net/intelibill/actions/workflows/deploy.yml) | [![Frontend Tests](.github/badges/frontend-tests.svg)](https://github.com/chanakya-net/intelibill/actions/workflows/deploy.yml) | [![Mobile Tests](.github/badges/mobile-tests.svg)](https://github.com/chanakya-net/intelibill/actions/workflows/mobile-main-ci.yml) |

## Tech Stack

| Layer | Technology |
|---|---|
| Backend | ASP.NET Core 10, C# latest, .NET 10.0.105 |
| Database | PostgreSQL via Npgsql + EF Core 10 |
| Messaging / CQRS | Wolverine 5.24 |
| Validation | FluentValidation 12 |
| Error handling | ErrorOr 2.0 |
| Frontend | Angular 21 (client-rendered), PWA, Tailwind CSS 4, PrimeNG 21, NgRx 21, Transloco, Bun |
| Mobile | Flutter Android (Riverpod, GoRouter, Dio, Freezed) |

## Repository Structure

```
intelibill/
├── docs/                       # Architecture & pattern docs
├── src/
│   ├── backend/                # ASP.NET Core — Clean Architecture
│   │   ├── Intelibill.Domain/
│   │   ├── Intelibill.Application/
│   │   ├── Intelibill.Infrastructure/
│   │   └── Intelibill.Api/
│   ├── frontend/               # Angular (Bun, PWA, NgRx, offline IndexedDB)
│   ├── mobile/
│   │   └── android/
│   │       └── intelibill_mobile/  # Flutter Android
│   └── services/               # Supporting services (own solutions, own package versions)
│       ├── barcode-master-api/ # Product lookup by barcode (BarcodeAPI.sln)
│       └── hsn-finder-ai/      # AI HSN code lookup (Sankra.HsnFinder.sln)
├── tests/
│   ├── backend/unit/
│   ├── backend/integration/    # Requires Docker (Testcontainers)
│   ├── services/               # Supporting service tests
│   └── frontend/
├── Directory.Build.props
├── Directory.Packages.props
└── global.json
```

See [CLAUDE.md](CLAUDE.md) for backend architecture and [docs/architectural_patterns.md](docs/architectural_patterns.md) for design patterns.

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

### Mobile

Flutter Android (Android-only):

```bash
cd src/mobile/android/intelibill_mobile

# Setup
flutter pub get
dart run build_runner build --delete-conflicting-outputs

# Development
flutter analyze
dart format --set-exit-if-changed .
flutter test --coverage

# Build debug APK
flutter build apk --debug
```

See [docs/mobile-architecture.md](docs/mobile-architecture.md) for architecture and testing details.

### Docker

```bash
# DB only (for local backend dev)
docker compose up -d

# Full stack: merchant API/web, licensing API/admin web, explicit migrations, Mailpit
docker compose --profile full up -d --build
```

The full stack prepares the databases and applies both schemas itself, in order,
before either API starts: `db-bootstrap` (roles, licensing database, connection
isolation) → `merchant-migrate` / `licensing-migrate` (EF migration bundles,
run as each database's owner) → `db-runtime-role` / `licensing-runtime-role`
(data grants) → `backend` / `licensing-api` → `frontend` / `admin-frontend`.
Neither API migrates or creates anything at startup, and each connects as a
restricted login (`intelibill_app`, `intelibill_licensing_app`) that cannot open
the other's database, change schema or bypass row-level security. A failure in
one branch never holds the other back. Details, configuration and
troubleshooting: [docs/licensing-rollout.md](docs/licensing-rollout.md).

When you run the DB-only profile and develop the API on the host, apply the
merchant schema yourself with the `dotnet ef database update` command under
[Backend](#backend) above, pointed at `localhost:5432`.

Identity email is captured locally by the `mailpit` service rather than
delivered: registration and password-reset messages appear at
`http://localhost:8025` and never leave the machine. See
[docs/auth-email-setup.md](docs/auth-email-setup.md).

The stack also sets `RateLimiting__RelaxEndpointPolicies`, so the auth endpoints
use the global default (100 per minute) instead of their own hardened figures —
sign-up is otherwise 5 requests per 15 minutes shared by every browser behind the
web proxy, which stops you testing the flow. The API refuses to start with that
setting in any environment but Development or Testing.

| Service  | URL |
|---|---|
| Merchant API | `http://127.0.0.1:5277` |
| Merchant web | `http://127.0.0.1:4000` |
| Licensing API | `http://127.0.0.1:5278` |
| Admin web | `http://127.0.0.1:4001` |
| Mail capture | `http://127.0.0.1:8025` |
| PostgreSQL | `localhost:5432` |

```bash
# Rebuild after code changes (also re-runs migrations and grants)
docker compose --profile full up -d --build

# Stop, keeping data (add -v only if you mean to wipe both databases)
docker compose --profile full down
```

## Azure App Service — WebSocket Configuration

SignalR is embedded in the ASP.NET Core app. To enable WebSockets on Azure App Service:

**Portal → App Service → Configuration → General Settings → Web Sockets: On**

SignalR automatically falls back to Server-Sent Events (SSE) or Long Polling if WebSockets are disabled — no code change required. WebSockets should be enabled in production for lowest latency.

## License

This project is licensed under the GNU Affero General Public License v3.0. See [LICENSE](LICENSE) for details.
