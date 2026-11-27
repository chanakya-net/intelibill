# HSN Finder AI Architecture

HSN Finder AI is a single-project ASP.NET Core API (`Sankra.HsnFinder.Api/HsnFinder.Api.csproj`, namespace `HsnFinder.Api`). It exposes one endpoint that turns a product name into GST HSN code(s) and tax-rate scenarios by asking an agent hosted in Azure AI Foundry. It has no database and no dependency on other projects in the repository. For setup and commands see [../README.md](../README.md).

## Components

| Component | File | Responsibility |
|---|---|---|
| Composition root | `Program.cs` | Configuration sources, options binding, rate limiter, retry pipeline, DI, HTTP pipeline |
| `HsnController` | `Controllers/HsnController.cs` | `POST /api/hsn`; request validation; maps service results to HTTP responses |
| `IHsnLookupService` / `HsnLookupService` | `Services/` | Cache lookup, prompt construction, agent call, JSON extraction and parsing, cache write |
| `ApiKeyMiddleware` | `Middleware/` | Shared-key check on `X-Api-Key` |
| Options | `Configuration/` | `AzureAiSettings`, `ApiKeySettings`, `CacheSettings`, `RateLimitSettings` |
| Models | `Models/` | `HsnLookupRequest`; `ApiResponse<T>`, `HsnLookupResponse`, `TaxScenario`; internal `HsnAgentResult` and `HsnStringOrArrayConverter` for the agent's JSON |

DI lifetimes: `HsnLookupService` is scoped, `ApiKeyMiddleware` is transient (`IMiddleware`), the Polly `ResiliencePipeline` is a singleton, and `IMemoryCache` is the default in-process cache.

## Request flow

Pipeline order in `Program.cs`: HTTPS redirection, API key middleware, rate limiter, controllers.

1. `ApiKeyMiddleware` lets `/openapi` and `/scalar` through; every other path needs a valid `X-Api-Key` or gets `401`.
2. The rate limiter applies the `api-key-policy` to the lookup action.
3. `HsnController.LookupHsn` rejects a missing body or missing/empty `productName` with `400`. Automatic model-state responses are disabled (`SuppressModelStateInvalidFilter`), so the controller builds the error envelope itself.
4. `HsnLookupService.LookupAsync` builds the cache key `hsn:{productName lower-cased and trimmed}` and returns the cached response on a hit.
5. On a miss it creates an `AIProjectClient` for `AzureAi:Endpoint` with `DefaultAzureCredential`, gets a responses client for the agent reference (`AzureAi:AgentName`, `AzureAi:AgentVersion`), and sends a prompt built from `AgentPromptTemplate` with the product name inserted. The prompt asks for a JSON object with `name`, `HSN` (one string or an array of strings) and `tax_scenarios` (`condition`, `tax_percentage`).
6. The call runs inside the retry pipeline: up to 2 retries with exponential backoff, each retry logged as a warning.
7. The first balanced `{...}` object is extracted from the agent's text (wrapper text around it is tolerated and logged) and deserialized case-insensitively into `HsnAgentResult`. `HsnStringOrArrayConverter` accepts a string, an array of strings, or `null` for the HSN field.
8. The result is mapped to `HsnLookupResponse`, cached for `Cache:TtlDays` days, and returned as `ApiResponse<HsnLookupResponse>`.

## External dependencies

- **Azure AI Foundry agent** — the only source of HSN and tax data. The agent itself (model, instructions, tools) is defined in Foundry, not in this repository; the service only references it by name and version.
- **Azure identity** — `DefaultAzureCredential`; no Azure key or connection string is read from configuration.

Client library: `Azure.AI.Projects` (a prerelease version, pinned in the csproj) and `Azure.Identity`.

## Configuration

Bound in `Program.cs` from the sections `AzureAi`, `ApiKey`, `Cache` and `RateLimit`. Sources: `appsettings.json`, optional `appsettings.secrets.json`, user secrets, and the ASP.NET Core defaults. The key table is in [../README.md](../README.md#configuration).

- Rate-limit values are read once at startup; non-positive or missing values fall back to 10 permits per 60 seconds.
- `Cache:TtlDays` has no fallback and is passed directly to `IMemoryCache.Set` as a relative expiration, so it must be greater than 0.
- Options are not validated at startup. An empty `AzureAi:Endpoint` only fails when the first lookup runs, and surfaces as `503`.

## Authentication and rate limiting

- One shared key, `ApiKey:Key`, compared to the `X-Api-Key` header with a length check plus `CryptographicOperations.FixedTimeEquals`. There are no users, roles or JWTs.
- A request without the header is rejected, so an unset `ApiKey:Key` rejects every request to the endpoint.
- `/openapi` and `/scalar` are exempt, and both are mapped in every environment.
- Rate limiting is a fixed window partitioned by the `X-Api-Key` header value with no queueing. Because there is a single valid key, the limit is effectively shared by all callers. Cache hits count against the limit.

## Error handling

All controller responses use `ApiResponse<T>` (`success`, `data`, `error`). The service returns `ErrorOr<HsnLookupResponse>` and does not throw to the controller.

| Situation | Status | Body |
|---|---|---|
| Success | `200` | `ApiResponse<HsnLookupResponse>` |
| Missing body or product name | `400` | envelope, `"Product name is required."` |
| Missing or invalid API key | `401` | empty (written by the middleware) |
| Rate limit exceeded | `429` | plain text (written by the rate limiter) |
| No JSON object in agent output, or JSON does not match the expected shape (`deserialization_failed`) | `503` | envelope with the error description |
| Agent call failed after retries, or any other exception in the lookup (`azure_ai_unavailable`) | `503` | envelope with the error description |

The controller maps `ErrorType.Validation` to `400` and every other error type to `503`; the service currently produces no validation errors. Failed lookups are not cached.

Logging uses the default ASP.NET Core logging providers. The service logs the product name, a preview of the raw agent response (up to 2000 characters), and exception details on failure.

## Testing status

There is no test project and no automated test for this service. The CI workflow (`.github/workflows/hsn-finder-ai-ci.yml`) builds the solution and runs `dotnet test`, which currently executes nothing; its path filter already includes `tests/backend/hsn-finder-ai/**`, a directory that does not exist yet.

## Constraints

- The cache is in-process: it is lost on restart and not shared between instances. The same applies to rate-limit counters.
- The agent call uses the SDK's synchronous `CreateResponse`; the request's cancellation token is checked before each attempt but is not passed to the SDK call.
- A new `AIProjectClient` and `DefaultAzureCredential` are created on every cache miss.
- Answers come from an AI agent and are returned as received once they parse; the service does not validate HSN codes or tax percentages against any reference data.
- Package versions live in the csproj; the local `Directory.Build.props` / `Directory.Packages.props` keep the root build settings and Central Package Management out. Do not add this service's packages to the root `Directory.Packages.props`.
