# Barcode Master API Architecture

Barcode Master API is an ASP.NET Core service for creating and querying product records by barcode. The codebase follows a layered architecture with the HTTP boundary, domain model, and persistence concerns separated into different projects.

## Solution Layout

```text
src/services/barcode-master-api/
  BarcodeAPI.sln
  BarcodeAPI.Api/
  BarcodeAPI.Application/
  BarcodeAPI.Domain/
  BarcodeAPI.Infrastructure/
tests/services/barcode-master-api/
  BarcodeAPI.Api.Tests/
```

## Project Responsibilities

### BarcodeAPI.Api

`BarcodeAPI.Api` is the host and HTTP boundary. It contains:

- `Program.cs` for service registration, authentication, authorization, OpenAPI, and controller routing.
- `Controllers/` for MVC controllers.
- `Contracts/` for HTTP request and response DTOs.
- `Authentication/` for API key authentication.
- `Authorization/` for the `DualAuth` authorization policy support.

The API uses controllers through `AddControllers()` and `MapControllers()`. New endpoints should be added as controller actions rather than Minimal API mappings.

### BarcodeAPI.Domain

`BarcodeAPI.Domain` contains the core business objects and interfaces:

- `Product` is the domain entity for barcode-backed product records.
- `BaseEntity` supplies `Id`, `CreatedAt`, and `UpdatedAt`.
- `IProductRepository` defines the persistence operations used by the API and application layers.

The domain project should not depend on ASP.NET Core, EF Core, Dapper, or PostgreSQL-specific implementation details.

### BarcodeAPI.Infrastructure

`BarcodeAPI.Infrastructure` implements persistence:

- `ApplicationDbContext` is the EF Core database context.
- `ProductConfiguration` configures product persistence mapping.
- `ProductRepository` implements `IProductRepository`.
- `Migrations/` contains EF Core migrations.

The repository uses a mixed persistence strategy:

- EF Core for tracked inserts in `AddAsync`.
- Dapper for fast read queries in `GetByBarcodeAsync` and `GetByBarcodesAsync`.
- PostgreSQL-specific SQL for batch insert with `UNNEST` in `AddBatchAsync`.

### BarcodeAPI.Application

`BarcodeAPI.Application` is currently thin. It contains `CreateProductCommand`, which constructs a `Product` and delegates persistence to `IProductRepository`.

This layer is the natural home for richer use cases if business workflows grow beyond simple controller orchestration.

## Runtime Composition

At startup, `Program.cs` wires the system together:

1. Registers `ApplicationDbContext` with the configured PostgreSQL connection string.
2. Registers `IProductRepository` to `ProductRepository`.
3. Configures JWT Bearer and API key authentication.
4. Configures the `DualAuth` authorization policy.
5. Registers Wolverine.
6. Registers MVC controllers and OpenAPI.
7. Adds authentication and authorization middleware.
8. Maps controllers.

The dependency direction is:

```text
BarcodeAPI.Api -> BarcodeAPI.Domain
BarcodeAPI.Api -> BarcodeAPI.Infrastructure
BarcodeAPI.Infrastructure -> BarcodeAPI.Domain
BarcodeAPI.Application -> BarcodeAPI.Domain
```

Controllers should depend on domain abstractions such as `IProductRepository`, not on `ApplicationDbContext`.

## Product API Flow

### Single Product Lookup

`GET /api/products/{barcode}` is handled by `ProductsController.GetByBarcode`.

Flow:

1. Reject blank barcode values with `400 Bad Request`.
2. Trim the route barcode.
3. Call `IProductRepository.GetByBarcodeAsync`.
4. Return `404 Not Found` when no product exists locally or in Open Food Facts.
5. Fall back to Open Food Facts on local miss, persist usable hits, and return `ProductDto` when found.

### Batch Lookup

`POST /api/products/lookup` is handled by `ProductsController.Lookup`.

Flow:

1. Reject missing or empty barcode lists.
2. Trim barcode values and remove blank values.
3. Reject requests where no valid barcode remains.
4. Call `IProductRepository.GetByBarcodesAsync`.
5. Return found products and a `NotFound` list for missing barcodes.

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

## Authentication And Authorization

Product endpoints are protected with:

```csharp
[Authorize(Policy = "DualAuth")]
```

The `DualAuth` policy requires an authenticated user and accepts either:

- JWT Bearer authentication
- API key authentication

The custom API key handler reads the key from the `X-API-Key` header and compares it to `Authentication:ApiKey` using a fixed-time comparison.

## Data Model

`Product` has:

- `Id`: generated by `BaseEntity`.
- `Barcode`: required string.
- `Name`: required string.
- `CreatedAt`: set by `BaseEntity`.
- `UpdatedAt`: optional timestamp from `BaseEntity`.

`ProductConfiguration` owns database mapping details such as required fields, lengths, uniqueness, and indexes.

## Testing Strategy

The test project is `tests/services/barcode-master-api/BarcodeAPI.Api.Tests`.

Current coverage focuses on controller behavior. The existing controller test verifies that lookup input is normalized, found products are mapped to DTOs, and missing barcodes are reported.

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
- External product enrichment: add an explicit provider abstraction and tests before wiring it into product lookup.

## Architectural Constraints

- Keep controllers thin: validation, request normalization, calls to application/repository abstractions, and response mapping.
- Keep domain types independent from infrastructure and web frameworks.
- Keep PostgreSQL-specific SQL inside infrastructure.
- Do not reintroduce Minimal API endpoint mappings in `Program.cs`.
- Do not add external fallback behavior unless product requirements explicitly call for it.
