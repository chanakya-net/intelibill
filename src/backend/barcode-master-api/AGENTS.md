# Barcode Master API Agent Notes

This service is a small layered ASP.NET Core API for storing and looking up product data by barcode. It lives in a monorepo but has its own solution (`BarcodeAPI.sln`); paths below are relative to the repository root.

Read first: [README.md](README.md) for setup, configuration and commands, and [architecture_doc/architecture.md](architecture_doc/architecture.md) for the full design.

## Architecture At A Glance

The solution uses four projects:

- `src/backend/barcode-master-api/BarcodeAPI.Api`: HTTP/API boundary. Owns controllers, request/response contracts, authentication, authorization, and application startup.
- `src/backend/barcode-master-api/BarcodeAPI.Application`: application use-case layer. Currently contains `CreateProductCommand`; this layer is still thin.
- `src/backend/barcode-master-api/BarcodeAPI.Domain`: domain model and abstractions. Owns `Product`, `BaseEntity`, `IProductRepository`, and `IOpenFoodFactsClient`.
- `src/backend/barcode-master-api/BarcodeAPI.Infrastructure`: persistence and external client implementation. Owns EF Core `ApplicationDbContext`, migrations, entity configuration, `ProductRepository`, and `OpenFoodFactsClient`.

The API is controller-based. Do not add new Minimal API endpoints in `Program.cs`; add controllers under `src/backend/barcode-master-api/BarcodeAPI.Api/Controllers`.

## Request Flow

1. `Program.cs` registers services and middleware.
2. ASP.NET Core authentication runs before authorization.
3. Controllers handle HTTP requests and validate request shape.
4. Controllers depend on `IProductRepository` and `IOpenFoodFactsClient`, not directly on EF Core, Dapper, or `HttpClient`.
5. `ProductRepository` uses EF Core for tracked writes and Dapper/raw SQL for fast lookup queries.
6. API responses are built from DTOs in `src/backend/barcode-master-api/BarcodeAPI.Api/Contracts`.

## Authentication And Authorization

Protected product endpoints use the `DualAuth` policy.

`DualAuth` accepts either:

- JWT Bearer auth through `JwtBearerDefaults.AuthenticationScheme`
- API key auth through the custom `ApiKeyAuthenticationHandler`

The API key is read from `Authentication:ApiKey`. The request header defaults to `X-API-Key`.

## Product Endpoints

`ProductsController` exposes:

- `GET /api/products/{barcode}`: trims and looks up one barcode; on a local miss it falls back to Open Food Facts and saves the hit.
- `POST /api/products/lookup`: trims and batch-looks-up barcodes, returning found products plus missing barcode values.
- `POST /api/products/batch`: validates product records, inserts valid records, and returns failed records with reasons.

Only the single-barcode `GET` uses the Open Food Facts fallback (`IOpenFoodFactsClient`, configured by the `OpenFoodFacts` section). Batch lookup and batch import are local-database-only.

## Persistence

`ApplicationDbContext` maps the `Products` table.

`ProductRepository` behavior:

- `AddAsync`: EF Core insert plus `SaveChangesAsync`.
- `GetByBarcodeAsync`: Dapper query using the EF Core connection.
- `GetByBarcodesAsync`: Dapper batch query using PostgreSQL `ANY`.
- `AddBatchAsync`: checks existing barcodes, inserts via PostgreSQL `UNNEST`, and reports per-record failures.

## Testing

Tests live in `tests/backend/barcode-master-api/BarcodeAPI.Api.Tests`.

The current test project uses xUnit. Add focused controller or behavior tests there when changing endpoint behavior. Run:

```bash
# from the repository root
dotnet test src/backend/barcode-master-api/BarcodeAPI.sln
```

The tests use in-memory fakes; they need neither Docker nor a database.

## Change Guidance

- Keep HTTP contracts in `BarcodeAPI.Api/Contracts`.
- Keep domain entities and interfaces free of ASP.NET Core dependencies.
- Keep persistence details in `BarcodeAPI.Infrastructure`.
- Prefer adding repository methods to `IProductRepository` when controllers need new persistence behavior.
- Avoid adding further external provider behavior without explicit requirements and tests.
- Package versions are pinned in each `.csproj`; never add this service's packages to the root `Directory.Packages.props`.
