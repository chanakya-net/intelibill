# intelibill

AI-powered inventory management system. Backend is the only active layer; frontend (Angular PWA) and mobile (.NET MAUI) directories exist as scaffolding only.

## Tech Stack

| Layer | Technology |
|---|---|
| Backend API | ASP.NET Core 10, C# latest, .NET 10.0.105 |
| Database | PostgreSQL via Npgsql + EF Core 10 |
| Messaging / CQRS | Wolverine 5.24 |
| Validation | FluentValidation 12 |
| Error handling | ErrorOr 2.0 (result pattern) |
| Tests | xUnit 2.9 + coverlet |
| Frontend | Angular PWA *(scaffolding only)* |
| Mobile | .NET MAUI *(scaffolding only)* |

## Key Directories

Paths are relative to this file (`src/backend/`).

| Path | Purpose |
|---|---|
| `Intelibill.Domain/` | Entities, value objects, domain interfaces — zero dependencies |
| `Intelibill.Application/` | Wolverine handlers, validators, error definitions — depends on Domain only |
| `Intelibill.Infrastructure/` | EF Core DbContext, repositories, database options — depends on Domain + Application |
| `Intelibill.Api/` | ASP.NET Core host, controllers, middleware — depends on Application + Infrastructure |
| `../../tests/backend/unit/` | Domain and Application unit tests |
| `../../tests/backend/integration/` | Integration tests referencing the API project |
| `../../Directory.Build.props` | Shared MSBuild properties: nullable, warnings-as-errors, analysis level, CPM flag |
| `../../Directory.Packages.props` | Central Package Management — all NuGet versions are declared here |
| `../../global.json` | Pins SDK to 10.0.105 with `latestMinor` roll-forward |

## Build & Test

Commands run from the **repo root** unless noted. Solution file: `src/backend/Intelibill.slnx`.

```bash
# Build
dotnet build src/backend/Intelibill.slnx

# Run API (picks up appsettings.Development.json automatically)
dotnet run --project src/backend/Intelibill.Api

# Test — full solution
dotnet test src/backend/Intelibill.slnx

# Test — individual projects
dotnet test tests/backend/unit/Intelibill.Domain.Unit.Tests
dotnet test tests/backend/unit/Intelibill.Application.Unit.Tests
dotnet test tests/backend/integration/Intelibill.Integration.Tests

# EF Core migrations
dotnet ef migrations add <MigrationName> \
  --project src/backend/Intelibill.Infrastructure \
  --startup-project src/backend/Intelibill.Api

dotnet ef database update \
  --project src/backend/Intelibill.Infrastructure \
  --startup-project src/backend/Intelibill.Api
```

## Configuration

Database credentials use the Options Pattern bound to the `"Database"` config section.
See `Intelibill.Infrastructure/Options/DatabaseOptions.cs:7`.

- `Intelibill.Api/appsettings.json` — intentionally empty strings; safe to commit
- `Intelibill.Api/appsettings.Development.json` — local defaults (`localhost:5432/intelibill_dev`)
- Production — supply values via environment variables or secrets manager

## Adding NuGet Packages

1. Add `<PackageVersion Include="..." Version="..." />` to `../../Directory.Packages.props`
2. Add `<PackageReference Include="..." />` (no version) to the relevant `.csproj`

## Additional Documentation

| Topic | File |
|---|---|
| Architecture, design patterns, conventions | [../../.claude/docs/architectural_patterns.md](../../.claude/docs/architectural_patterns.md) |
