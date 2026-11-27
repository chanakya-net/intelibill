# Barcode Master API Architecture

Barcode Master API is an ASP.NET Core service for creating and querying product records by barcode. The codebase follows a layered architecture with the HTTP boundary, domain model, and persistence concerns separated into different projects. For setup, configuration keys and commands see [../README.md](../README.md).

## Solution Layout

Paths are relative to the repository root.

```text
src/backend/barcode-master-api/
  BarcodeAPI.sln
  BarcodeAPI.Api/
  BarcodeAPI.Application/
  BarcodeAPI.Domain/
  BarcodeAPI.Infrastructure/
tests/backend/barcode-master-api/
  BarcodeAPI.Api.Tests/
```

`BarcodeAPI.sln` includes the test project; the root `Intelibill.slnx` lists all five projects too. Both directories carry their own `Directory.Build.props` / `Directory.Packages.props`, which keep the root build settings and Central Package Management out; package versions are pinned in each `.csproj`.

## Project Responsibilities

### BarcodeAPI.Api

`BarcodeAPI.Api` is the host and HTTP boundary. It contains:

- `Program.cs` for service registration, authentication, authorization, OpenAPI, and controller routing.
- `Controllers/` for MVC controllers.
- `Contracts/` for HTTP request and response DTOs.
- `Authentication/` for API key authentication.
- `Authorization/` for the `DualAuth` authorization policy support.

The API uses controllers through `AddControllers()` and `MapControllers()`. New endpoints should be added as controller actions rather than Minimal API mappings.

Besides `ProductsController`, the project still contains the template `WeatherForecastController` (`GET /weatherforecast`, no authorization) and its `WeatherForecastDto`.

### BarcodeAPI.Domain

`BarcodeAPI.Domain` contains the core business objects and interfaces:

- `Product` is the domain entity for barcode-backed product records.
- `BaseEntity` supplies `Id`, `CreatedAt`, and `UpdatedAt`.
- `IProductRepository` defines the persistence operations used by the API and application layers.
- `IOpenFoodFactsClient` defines the external product lookup used as a fallback.

The domain project should not depend on ASP.NET Core, EF Core, Dapper, or PostgreSQL-specific implementation details.

### BarcodeAPI.Infrastructure

`BarcodeAPI.Infrastructure` implements persistence and the external client:

- `ApplicationDbContext` is the EF Core database context.
- `ProductConfiguration` configures product persistence mapping.
- `ProductRepository` implements `IProductRepository`.
- `Migrations/` contains EF Core migrations (currently one, `InitialCreate`).
- `OpenFoodFacts/` contains `OpenFoodFactsClient` (implements `IOpenFoodFactsClient`) and `OpenFoodFactsOptions`.

The repository uses a mixed persistence strategy:

- EF Core for tracked inserts in `AddAsync`.
- Dapper for fast read queries in `GetByBarcodeAsync` and `GetByBarcodesAsync`.
- PostgreSQL-specific SQL for batch insert with `UNNEST` in `AddBatchAsync`.

The Dapper queries run on the EF Core connection and use quoted PascalCase identifiers (`"Products"`, `"Barcode"`), matching the migration. Read queries select `Id`, `Barcode`, `Name` and `CreatedAt` only.

### BarcodeAPI.Application

`BarcodeAPI.Application` is currently thin. It contains `CreateProductCommand` and `CreateProductHandler`, which skips barcodes that already exist and otherwise constructs a `Product` and delegates persistence to `IProductRepository`. No controller dispatches this command; the product endpoints call the repository directly.

This layer is the natural home for richer use cases if business workflows grow beyond simple controller orchestration.

## Runtime Composition

At startup, `Program.cs` wires the system together:

1. Registers `ApplicationDbContext` with the `ConnectionStrings:DefaultConnection` PostgreSQL connection string.
2. Registers `IProductRepository` to `ProductRepository`.
3. Binds `OpenFoodFactsOptions` from the `OpenFoodFacts` section and registers a typed `HttpClient` for `IOpenFoodFactsClient` with that base URL and timeout.
4. Configures JWT Bearer and API key authentication.
5. Configures the `DualAuth` authorization policy.
6. Registers Wolverine (`UseWolverine()`).
7. Registers MVC controllers and OpenAPI.
8. Maps the OpenAPI document in the Development environment only.
9. Adds authentication and authorization middleware.
10. Maps controllers.

Migrations are not applied at startup.

The project references are:

```text
BarcodeAPI.Api -> BarcodeAPI.Application
BarcodeAPI.Api -> BarcodeAPI.Domain
BarcodeAPI.Api -> BarcodeAPI.Infrastructure
BarcodeAPI.Infrastructure -> BarcodeAPI.Application
BarcodeAPI.Infrastructure -> BarcodeAPI.Domain
BarcodeAPI.Application -> BarcodeAPI.Domain
```

Controllers should depend on domain abstractions such as `IProductRepository` and `IOpenFoodFactsClient`, not on `ApplicationDbContext`.

## Product API Flow

### Single Product Lookup

`GET /api/products/{barcode}` is handled by `ProductsController.GetByBarcode`.

Flow:

1. Reject blank barcode values with `400 Bad Request`.
2. Trim the route barcode.
3. Call `IProductRepository.GetByBarcodeAsync` and return `ProductDto` when the product exists locally.
4. On a local miss, call `IOpenFoodFactsClient.GetByBarcodeAsync`.
5. Return `404 Not Found` when Open Food Facts has no usable product or the call throws (the exception is logged, not surfaced).
6. Persist the Open Food Facts hit with `IProductRepository.AddAsync` and return it as `ProductDto`.
7. If saving fails, re-read the barcode to pick up a concurrent save and return that record; if it is still missing or the re-read fails, return the unsaved product with `200 OK`.

`OpenFoodFactsClient` requests `api/v2/product/{barcode}` and returns `null` for a non-success status code, a response whose `status` is not `1`, or a missing/blank product name. The name is read from `product_name` at the root or under `product`.

### Batch Lookup

`POST /api/products/lookup` is handled by `ProductsController.Lookup`.

Flow:

1. Reject missing or empty barcode lists.
2. Trim barcode values and remove blank values.
3. Reject requests where no valid barcode remains.
4. Call `IProductRepository.GetByBarcodesAsync`.
5. Return found products and a `NotFound` list for missing barcodes.

This endpoint reads the local database only; it does not fall back to Open Food Facts.

### Batch Import

`POST /api/products/batch` is handled by `ProductsController.Batch`.

Flow:

1. Reject missing or empty product lists.
2. Validate each item for barcode and name.
3. Convert valid request items into `Product` entities.
4. Call `IProductRepository.AddBatchAsync`.
5. Merge request validation failures with repository/database failures.
6. Return totals and failed records.

The endpoint returns `200 OK` with partial failure information. This lets clients submit large batches without failing the whole request for individual bad records.

Failure reasons are prefixed: `INVALID:` (request validation), `DUPLICATE:` (barcode already in the database) and `INSERT_ERROR:` followed by the exception message. The insert is a single statement, so when it fails every record that was about to be inserted is reported as `INSERT_ERROR`.

## Authentication And Authorization

Product endpoints are protected with:

```csharp
[Authorize(Policy = "DualAuth")]
```

The `DualAuth` policy requires an authenticated user and accepts either:

- JWT Bearer authentication
- API key authentication

JWT validation uses a symmetric key from `Jwt:Secret` and checks issuer (`Jwt:Issuer`), audience (`Jwt:Audience`) and lifetime with zero clock skew. This service only validates tokens; it does not issue them.

The custom API key handler reads the key from the `X-API-Key` header and compares it to `Authentication:ApiKey` using a fixed-time comparison. When `Authentication:ApiKey` is not configured, API key authentication fails and a warning is logged.

## Data Model

`Product` has:

- `Id`: generated by `BaseEntity`.
- `Barcode`: required string.
- `Name`: required string.
- `CreatedAt`: set by `BaseEntity`.
- `UpdatedAt`: optional timestamp from `BaseEntity`.

`ProductConfiguration` owns the database mapping: `Id` is the key, `Barcode` is required with a maximum length of 50 and a unique index, and `Name` is required. The table is `Products`.

## Testing Strategy

The test project is `tests/backend/barcode-master-api/BarcodeAPI.Api.Tests` (xUnit). Run it from the repository root with `dotnet test src/backend/barcode-master-api/BarcodeAPI.sln`.

Current coverage:

- `ProductsControllerTests` drives the controller directly with in-memory fakes for `IProductRepository` and `IOpenFoodFactsClient`. It covers the single-lookup paths (local hit, Open Food Facts hit and persist, external miss, external failure, save failure, concurrent-save race, save and re-read failure) and batch lookup normalization.
- `OpenFoodFactsClientTests` covers blank and missing product names using a stub `HttpMessageHandler`.

There are no tests for the batch import endpoint, `ProductRepository` SQL, authentication, or the HTTP pipeline, and no test uses a database.

When adding behavior:

- Add controller tests for request validation and response shape.
- Add repository tests when SQL behavior changes.
- Add integration tests when middleware, authentication, routing, or dependency injection behavior changes.
- Keep tests local to the behavior being changed.

## Extension Points

Use these seams for future work:

- New HTTP endpoint: add a controller action and DTOs under `BarcodeAPI.Api`.
- New product persistence behavior: add to `IProductRepository`, implement in `ProductRepository`, and cover with tests.
- New business workflow: place orchestration in `BarcodeAPI.Application`, then call it from controllers.
- New authentication mechanism: register a new authentication scheme in `Program.cs` and include it in the appropriate authorization policy.
- Additional external product source: follow the `IOpenFoodFactsClient` pattern (interface in Domain, implementation in Infrastructure) and add tests before wiring it into product lookup.

## Architectural Constraints

- Keep controllers thin: validation, request normalization, calls to application/repository abstractions, and response mapping.
- Keep domain types independent from infrastructure and web frameworks.
- Keep PostgreSQL-specific SQL inside infrastructure.
- Do not reintroduce Minimal API endpoint mappings in `Program.cs`.
- Open Food Facts is the only external fallback and applies to single lookup only. Do not extend fallback behavior unless product requirements explicitly call for it.
- Several referenced packages are not used by any code yet: Serilog, Scalar, FluentValidation, ErrorOr, `EFCore.NamingConventions` and `Microsoft.Extensions.Http.Resilience`. In particular, logging uses the default providers and the Open Food Facts client has no retry policy beyond its timeout.
