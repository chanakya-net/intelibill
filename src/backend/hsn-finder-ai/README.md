# HSN Finder AI

Small ASP.NET Core API that returns the GST HSN code(s) and tax-rate scenarios for a product name in India. It forwards the product name to an agent hosted in Azure AI Foundry and returns the agent's JSON answer in a fixed response envelope.

It is a separately deployable service inside the [intelibill monorepo](../../../README.md) with its own solution; it is not part of `Intelibill.slnx` and shares no code with the other backend projects.

## Stack

- .NET 10 (`net10.0`), ASP.NET Core MVC controllers; SDK pinned by the root `global.json`
- `Azure.AI.Projects` (prerelease) + `Azure.Identity` (`DefaultAzureCredential`) for the agent call
- `Microsoft.Extensions.Resilience` (Polly retry), `IMemoryCache`, ASP.NET Core rate limiter
- `ErrorOr` for service results
- `Microsoft.AspNetCore.OpenApi` + `Scalar.AspNetCore` for the API reference UI

Package versions are pinned in `Sankra.HsnFinder.Api/HsnFinder.Api.csproj`. The local `Directory.Build.props` / `Directory.Packages.props` stop the root build settings and Central Package Management from applying; never add this service's packages to the root `Directory.Packages.props`. The project sets `TreatWarningsAsErrors` itself.

## Layout

```text
src/backend/hsn-finder-ai/
  Sankra.HsnFinder.sln
  Directory.Build.props, Directory.Packages.props   # isolate from root MSBuild/CPM
  Sankra.HsnFinder.Api/
    HsnFinder.Api.csproj        # root namespace HsnFinder.Api
    Program.cs                  # composition root and pipeline
    Controllers/HsnController.cs
    Services/                   # IHsnLookupService, HsnLookupService (agent call, cache, parsing)
    Middleware/                 # ApiKeyMiddleware
    Configuration/              # options classes
    Models/                     # Requests, Responses, Agent (agent JSON shape)
    appsettings.json
    appsettings.secrets.json.example
```

There is no test project.

## API

`POST /api/hsn` with header `X-Api-Key` and body `{ "productName": "..." }`.

Success (`200`):

```json
{
  "success": true,
  "data": {
    "name": "...",
    "hsn": ["..."],
    "taxScenarios": [{ "condition": "...", "taxPercentage": "..." }]
  },
  "error": null
}
```

Status codes: `400` (missing product name), `401` (missing or wrong API key, empty body), `429` (rate limit, plain-text body), `503` (agent call or response parsing failed). See [architecture_doc/architecture.md](architecture_doc/architecture.md) for details.

## Configuration

Sources, in order: `appsettings.json`, optional `appsettings.secrets.json` (gitignored), .NET user secrets, plus the ASP.NET Core defaults (environment variables, command line).

| Key | Purpose |
|---|---|
| `AzureAi:Endpoint` | Azure AI Foundry project endpoint URL |
| `AzureAi:AgentName` | Name of the Foundry agent to call |
| `AzureAi:AgentVersion` | Agent version |
| `ApiKey:Key` | Shared key callers must send in `X-Api-Key` |
| `Cache:TtlDays` | In-memory cache lifetime per product name (`30` in `appsettings.json`; must be greater than 0) |
| `RateLimit:PermitLimit` | Requests per window (`10` in `appsettings.json`; non-positive values fall back to 10) |
| `RateLimit:WindowSeconds` | Fixed window length (`60` in `appsettings.json`; non-positive values fall back to 60) |

`AzureAi:*` and `ApiKey:Key` are empty in the committed `appsettings.json`. For local runs copy `Sankra.HsnFinder.Api/appsettings.secrets.json.example` to `appsettings.secrets.json` (or use `dotnet user-secrets`) and fill them in. There is no key for Azure credentials: the service authenticates with `DefaultAzureCredential`, so the running identity (for example an `az login` session locally) needs access to the Foundry project.

## Build and run

From the repository root:

```bash
dotnet build src/backend/hsn-finder-ai/Sankra.HsnFinder.sln
dotnet run --project src/backend/hsn-finder-ai/Sankra.HsnFinder.Api/HsnFinder.Api.csproj
```

Launch profiles listen on `http://localhost:5000` (and `https://localhost:5001` for the `https` profile). The OpenAPI document (`/openapi/v1.json`) and the Scalar UI (`/scalar/v1`) are served in every environment and do not require the API key.

## Tests

None exist. `dotnet test src/backend/hsn-finder-ai/Sankra.HsnFinder.sln` runs no tests.

## CI

[`.github/workflows/hsn-finder-ai-ci.yml`](../../../.github/workflows/hsn-finder-ai-ci.yml) restores, builds (`Release`) and runs `dotnet test` on the solution for pull requests to and pushes on `main` that touch `src/backend/hsn-finder-ai/**`, `tests/backend/hsn-finder-ai/**`, the workflow file, or `global.json`. The repository has no Dockerfile or deployment workflow for this service.

## Docs

- [architecture_doc/architecture.md](architecture_doc/architecture.md) — components, request flow, dependencies, error handling, constraints
