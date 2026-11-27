# HSN Finder AI Architecture

HSN Finder AI is a single-project ASP.NET Core API (`Sankra.HsnFinder.Api/HsnFinder.Api.csproj`, namespace `HsnFinder.Api`). It exposes one endpoint that turns a product name into GST HSN code(s) and tax-rate scenarios by asking an AI model through an OpenAI-compatible endpoint (OpenAI by default). It has no database and no dependency on other projects in the repository. A `Dockerfile` in the project folder builds its image, and the local Docker stack runs it as `hsn-finder`; there is no deployment workflow. For setup and commands see [../README.md](../README.md).

## Components

| Component | File | Responsibility |
|---|---|---|
| Composition root | `Program.cs` | Configuration sources, options binding, rate limiter, retry pipeline, DI, HTTP pipeline |
| `HsnController` | `Controllers/HsnController.cs` | `POST /api/hsn`; request validation; maps service results to HTTP responses |
| `IHsnLookupService` / `HsnLookupService` | `Services/` | Cache lookup, message construction, model call, JSON extraction and parsing, cache write |
| `ApiKeyMiddleware` | `Middleware/` | Shared-key check on `X-Api-Key` |
| Options | `Configuration/` | `AiSettings`, `ApiKeySettings`, `CacheSettings`, `RateLimitSettings`; `AddAiChatClient` registers and validates the `Ai` settings and the `IChatClient` |
| `AiChatOptionsFactory` | `Services/AiChatOptionsFactory.cs` | Builds the per-call `ChatOptions` (reasoning effort, hosted web search, verbosity) from `AiSettings` |
| Models | `Models/` | `HsnLookupRequest`; `ApiResponse<T>`, `HsnLookupResponse`, `TaxScenario`; internal `HsnAgentResult` and `HsnStringOrArrayConverter` for the model's JSON |

DI lifetimes: `HsnLookupService` is scoped, `ApiKeyMiddleware` is transient (`IMiddleware`), the Polly `ResiliencePipeline` and the `IChatClient` are singletons, and `IMemoryCache` is the default in-process cache.

## Request flow

Pipeline order in `Program.cs`: HTTPS redirection, API key middleware, rate limiter, controllers.

1. `ApiKeyMiddleware` lets `/openapi` and `/scalar` through; every other path needs a valid `X-Api-Key` or gets `401`.
2. The rate limiter applies the `api-key-policy` to the lookup action.
3. `HsnController.LookupHsn` rejects a missing body or missing/empty `productName` with `400`. Automatic model-state responses are disabled (`SuppressModelStateInvalidFilter`), so the controller builds the error envelope itself.
4. `HsnLookupService.LookupAsync` builds the cache key `hsn:{productName lower-cased and trimmed}` and returns the cached response on a hit.
5. On a miss it sends two messages to the singleton `IChatClient`: a system message (the `SystemInstructions` constant in `HsnLookupService`) and a user message containing only the trimmed product name. The instructions ask for authoritative, current Indian sources, the most specific (normally 8-digit) HSN code, one `tax_scenarios` entry per condition, and a JSON object with `name`, `HSN` (one string or an array of strings) and `tax_scenarios` (`condition`, `tax_percentage`), and tell the model to treat the user message as data. `ChatOptions` come from `AiChatOptionsFactory`; the call is asynchronous and receives the request's cancellation token.
6. The call runs inside the retry pipeline: up to 2 retries with exponential backoff, each retry logged as a warning. A cancelled request is not retried and not answered with `503`; the cancellation propagates.
7. The first balanced `{...}` object is extracted from the model's text (wrapper text around it is tolerated and logged) and deserialized case-insensitively into `HsnAgentResult`. `HsnStringOrArrayConverter` accepts a string, an array of strings, or `null` for the HSN field.
8. The result is mapped to `HsnLookupResponse`, cached for `Cache:TtlDays` days, and returned as `ApiResponse<HsnLookupResponse>`.

## External dependencies

- **OpenAI-compatible AI endpoint** — the only source of HSN and tax data. `Ai:Endpoint` defaults to `https://api.openai.com/v1`; the model, reasoning effort, verbosity and hosted web search are chosen by `Ai:*` configuration, and the instructions are the `SystemInstructions` constant in this repository (they used to live in an Azure AI Foundry agent). The key is read from `Ai:ApiKey`; there is no cloud identity.
- **Hosted web search** — optional (`Ai:WebSearch:Enabled`). It only works where the provider implements it. Elsewhere set it to `false`; answers then come from the model's knowledge and can be stale for GST rates.

Client libraries: `Microsoft.Extensions.AI` (`IChatClient`) and `Microsoft.Extensions.AI.OpenAI` over the official `OpenAI` SDK, pinned in the csproj. `Ai:Api` selects the Responses client (default) or the Chat Completions client. `text.verbosity` is set through an experimental `OpenAI` SDK hook (`JsonPatch` on the response text options). Web search country and context size, and verbosity, are sent only when `Ai:Api=Responses`; under `ChatCompletions` with `Ai:WebSearch:Enabled=true` the request carries an empty `web_search_options` object (no country or context size), and reasoning effort is sent as `reasoning_effort`.

## Configuration

Bound in `Program.cs` from the sections `Ai`, `ApiKey`, `Cache` and `RateLimit`. Sources: `appsettings.json`, the ASP.NET Core defaults, then optional `appsettings.secrets.json` and user secrets, which `Program.cs` adds after the defaults and which therefore override environment variables. The key table is in [../README.md](../README.md#configuration).

- Rate-limit values are read once at startup; non-positive or missing values fall back to 10 permits per 60 seconds.
- `Cache:TtlDays` has no fallback and is passed directly to `IMemoryCache.Set` as a relative expiration, so it must be greater than 0.
- `Ai` options are validated at startup (`ValidateOnStart`): `Ai:Endpoint` must be an absolute http(s) URL, `Ai:ApiKey` and `Ai:Model` must be non-empty, and `Ai:ReasoningEffort`, when set, must be a valid `ReasoningEffort` name. A violation stops the host with an `OptionsValidationException` that names the key and does not contain its value. The `ApiKey`, `Cache` and `RateLimit` options are not validated.

## Container and local stack

The image (`Dockerfile`, build context = repository root) is built from the digest-pinned SDK and ASP.NET images the other APIs use, runs as a non-root user on port 8080 and defaults to `ASPNETCORE_ENVIRONMENT=Production`. It copies only `global.json` and this project, so the local `Directory.Build.props` / `Directory.Packages.props` apply. The root `.dockerignore` excludes `appsettings.secrets.json` from every build context; since `Program.cs` loads that file after environment variables, it would otherwise override the container's configuration. All secrets reach the container as environment variables.

In `docker-compose.yml` (profile `full`) the `hsn-finder` service publishes `127.0.0.1:5279`, is health-checked on `/openapi/v1.json` (served without the API key) and has no `depends_on`. It sets `Ai__ApiKey` to a placeholder when `INTELIBILL_HSN_AI_API_KEY` is unset, so the host starts (it refuses a blank key) and lookups answer `503` until a real key is supplied. The merchant API is given `HsnService__BaseUrl=http://hsn-finder:8080` and the same shared key, but does not depend on this service starting.

## Authentication and rate limiting

- One shared key, `ApiKey:Key`, compared to the `X-Api-Key` header with a length check plus `CryptographicOperations.FixedTimeEquals`. There are no users, roles or JWTs.
- A request without the header is rejected, so an unset `ApiKey:Key` rejects every request to the endpoint.
- `/openapi` and `/scalar` are exempt, and both are mapped in every environment.
- Rate limiting is a fixed window partitioned by the `X-Api-Key` header value with no queueing. Because there is a single valid key, the limit is effectively shared by all callers. Cache hits count against the limit.

## Error handling

All controller responses use `ApiResponse<T>` (`success`, `data`, `error`). The service returns `ErrorOr<HsnLookupResponse>` and does not throw to the controller, except that `OperationCanceledException` propagates when the caller cancels.

| Situation | Status | Body |
|---|---|---|
| Success | `200` | `ApiResponse<HsnLookupResponse>` |
| Missing body or product name | `400` | envelope, `"Product name is required."` |
| Missing or invalid API key | `401` | empty (written by the middleware) |
| Rate limit exceeded | `429` | plain text (written by the rate limiter) |
| No JSON object in model output, or JSON does not match the expected shape (`deserialization_failed`) | `503` | envelope with the error description |
| Model call failed after retries, or any other exception in the lookup (`ai_provider_unavailable`, "AI provider is currently unavailable.") | `503` | envelope with the error description |
| Request cancelled by the caller | none | cancellation propagates; no `503` is produced |

The controller maps `ErrorType.Validation` to `400` and every other error type to `503`; the service currently produces no validation errors. Failed lookups are not cached.

Logging uses the default ASP.NET Core logging providers. The service logs the product name, a preview of the raw model response (up to 2000 characters), and exception details on failure.

## Testing

`tests/backend/hsn-finder-ai/HsnFinder.Api.Tests` (xUnit, `Microsoft.AspNetCore.Mvc.Testing`) runs with `dotnet test src/backend/hsn-finder-ai/Sankra.HsnFinder.sln`, which the CI workflow (`.github/workflows/hsn-finder-ai-ci.yml`) also does. It covers:

- `HsnLookupService`: parsing (plain, wrapped, array HSN), caching, the system and user messages, retries, `ai_provider_unavailable`, `deserialization_failed`, and cancellation, using a `FakeChatClient`.
- `AiChatOptionsFactory`, and the real OpenAI client against a stubbed HTTP handler (request shape for the Responses API).
- `AddAiChatClient` validation (missing key, model, bad endpoint, bad reasoning effort) and the singleton registration.
- `POST /api/hsn` through `WebApplicationFactory<Program>` with the chat client replaced: `401`, `200`, `400`, `503`, and a host that fails to start without `Ai:Model`. The `ChatCompletions` request shape is checked against a stubbed HTTP transport (the DI registration has no transport seam, so that test builds the client the same way as the wire test). The endpoint tests override the whole `AiSettings` object (endpoint, key, model, API, reasoning effort, verbosity, web search), `CacheSettings.TtlDays` and `ApiKeySettings.Key` after all configuration sources, so a developer's local `appsettings.secrets.json` or user secrets cannot change them.

No test makes a network call.

## Constraints

- The cache is in-process: it is lost on restart and not shared between instances. The same applies to rate-limit counters.
- The `OpenAI` SDK has its own built-in retries. They stack with the service's Polly retry (2 retries, exponential), so a failing provider can be called more than 3 times per lookup.
- An uncached lookup takes several seconds (a live run with a reasoning model and web search took about 6 to 9 seconds); cache hits are immediate.
- Answers come from an AI model and are returned as received once they parse; the service does not validate HSN codes or tax percentages against any reference data.
- Package versions live in the csproj; the local `Directory.Build.props` / `Directory.Packages.props` keep the root build settings and Central Package Management out. Do not add this service's packages to the root `Directory.Packages.props`. Its projects are also listed in the root `Intelibill.slnx` (`ProjectBoundaryAssertions` checks this), but they build with these local settings, not the root ones.
