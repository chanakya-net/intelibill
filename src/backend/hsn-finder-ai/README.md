# HSN Finder AI

Small ASP.NET Core API that returns the GST HSN code(s) and tax-rate scenarios for a product name in India. It sends the product name to an AI model through any OpenAI-compatible endpoint (OpenAI by default; the provider, endpoint, key and model are configuration) and returns the model's JSON answer in a fixed response envelope.

It is a separately deployable service inside the [intelibill monorepo](../../../README.md) with its own solution; it is not part of `Intelibill.slnx` and shares no code with the other backend projects.

## Stack

- .NET 10 (`net10.0`), ASP.NET Core MVC controllers; SDK pinned by the root `global.json`
- `Microsoft.Extensions.AI` (`IChatClient`) + `Microsoft.Extensions.AI.OpenAI` (over the official `OpenAI` SDK) for the model call
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
    Services/                   # IHsnLookupService, HsnLookupService (model call, cache, parsing), AiChatOptionsFactory
    Middleware/                 # ApiKeyMiddleware
    Configuration/              # options classes, AiSettings, AddAiChatClient (IChatClient registration)
    Models/                     # Requests, Responses, Agent (model JSON shape)
    appsettings.json
    appsettings.secrets.json.example
tests/backend/hsn-finder-ai/
  HsnFinder.Api.Tests/          # xUnit tests (no network)
```

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

Status codes: `400` (missing product name), `401` (missing or wrong API key, empty body), `429` (rate limit, plain-text body), `503` (AI provider call or response parsing failed). See [architecture_doc/architecture.md](architecture_doc/architecture.md) for details.

## Configuration

Sources, in order (later sources override earlier ones): `appsettings.json`, the ASP.NET Core defaults (environment variables, command line), then optional `appsettings.secrets.json` (gitignored) and .NET user secrets. `Program.cs` adds the last two after the default sources, so a value in `appsettings.secrets.json` or user secrets overrides the same key set as an environment variable.

| Key | Default | Purpose |
|---|---|---|
| `Ai:Endpoint` | `https://api.openai.com/v1` | Base URL of an OpenAI-compatible API |
| `Ai:ApiKey` | empty | Provider key (secret; never commit it) |
| `Ai:Model` | empty | Model or deployment name |
| `Ai:Api` | `Responses` | `Responses` or `ChatCompletions` |
| `Ai:ReasoningEffort` | `low` in `appsettings.json` | Optional: `None`, `Low`, `Medium`, `High` or `ExtraHigh` (case-insensitive); any other value stops the host |
| `Ai:Verbosity` | `low` in `appsettings.json` | Optional: `low`, `medium`, `high` (Responses only) |
| `Ai:WebSearch:Enabled` | `true` in `appsettings.json` (`false` if unset) | Adds the hosted web search tool |
| `Ai:WebSearch:Country` | `IN` in `appsettings.json` | Optional ISO country code for the search location (Responses only) |
| `Ai:WebSearch:ContextSize` | `low` in `appsettings.json` | Optional: `low`, `medium`, `high` (Responses only) |
| `ApiKey:Key` | empty | Shared key callers must send in `X-Api-Key` |
| `Cache:TtlDays` | `30` | In-memory cache lifetime per product name (must be greater than 0) |
| `RateLimit:PermitLimit` | `10` | Requests per window (non-positive values fall back to 10) |
| `RateLimit:WindowSeconds` | `60` | Fixed window length (non-positive values fall back to 60) |

The host refuses to start when `Ai:Endpoint` is not an absolute http(s) URL, when `Ai:ApiKey` or `Ai:Model` is empty, or when `Ai:ReasoningEffort` is set to an invalid value. The error names the key and never includes the key's value.

`Ai:ApiKey`, `Ai:Model` and `ApiKey:Key` are empty in the committed `appsettings.json`. For local runs either copy `Sankra.HsnFinder.Api/appsettings.secrets.json.example` to `appsettings.secrets.json` and fill it in, or use user secrets or environment variables:

```bash
dotnet user-secrets set "Ai:ApiKey" "<provider key>" --project src/backend/hsn-finder-ai/Sankra.HsnFinder.Api
dotnet user-secrets set "Ai:Model" "<model name>" --project src/backend/hsn-finder-ai/Sankra.HsnFinder.Api
# or: export Ai__ApiKey=... Ai__Model=...
```

### Switching provider

Change `Ai:Endpoint`, `Ai:ApiKey` and `Ai:Model` to the new provider's values; no code change is needed for any OpenAI-compatible API (for example Azure OpenAI v1, OpenRouter, Groq, Ollama).

- For a provider without the Responses API, set `Ai:Api=ChatCompletions`.
- For a provider without hosted web search, set `Ai:WebSearch:Enabled=false`. It is `true` in `appsettings.json`. Under `ChatCompletions` with it enabled the request carries an empty `web_search_options` object (the country and context size are not sent); `Ai:Verbosity`, the web search country and the context size are sent only when `Ai:Api=Responses`.
- For a model or provider without reasoning support, clear `Ai:ReasoningEffort` (set it to empty). Under `ChatCompletions` it is sent as `reasoning_effort`, and `appsettings.json` ships `low`.
- Without web search the answer comes from the model's own knowledge and may be out of date for GST rates.
- A provider with a native, non-OpenAI-compatible API needs one more registration branch in `Configuration/AiServiceCollectionExtensions.cs`; the lookup service only depends on `IChatClient`.

## Build and run

From the repository root:

```bash
dotnet build src/backend/hsn-finder-ai/Sankra.HsnFinder.sln
dotnet run --project src/backend/hsn-finder-ai/Sankra.HsnFinder.Api/HsnFinder.Api.csproj
```

Launch profiles listen on `http://localhost:5000` (and `https://localhost:5001` for the `https` profile). The OpenAPI document (`/openapi/v1.json`) and the Scalar UI (`/scalar/v1`) are served in every environment and do not require the API key.

## Tests

```bash
dotnet test src/backend/hsn-finder-ai/Sankra.HsnFinder.sln
```

`tests/backend/hsn-finder-ai/HsnFinder.Api.Tests` covers the lookup service (parsing, caching, retries, cancellation, the messages sent), the chat options built from settings, the OpenAI client wiring against a stubbed HTTP handler, startup validation of the `Ai` settings, and `POST /api/hsn` end to end (`401`, `200`, `400`, `503`) through `WebApplicationFactory`. No test calls a real provider. The endpoint tests override the whole `Ai` settings object (endpoint, key, model, API, reasoning effort, verbosity, web search), `Cache:TtlDays` and `ApiKey:Key` after all configuration sources, so a local `appsettings.secrets.json` or user secrets do not affect them.

## CI

[`.github/workflows/hsn-finder-ai-ci.yml`](../../../.github/workflows/hsn-finder-ai-ci.yml) restores, builds (`Release`) and runs `dotnet test` on the solution for pull requests to and pushes on `main` that touch `src/backend/hsn-finder-ai/**`, `tests/backend/hsn-finder-ai/**`, the workflow file, or `global.json`. The repository has no Dockerfile or deployment workflow for this service.

## Docs

- [architecture_doc/architecture.md](architecture_doc/architecture.md) — components, request flow, dependencies, error handling, constraints
