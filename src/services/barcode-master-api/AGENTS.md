# Barcode Master API Agent Notes

This repository is a small layered ASP.NET Core API for storing and looking up product data by barcode.

## Architecture At A Glance

The solution uses four projects:

- `src/services/barcode-master-api/BarcodeAPI.Api`: HTTP/API boundary. Owns controllers, request/response contracts, authentication, authorization, and application startup.
- `src/services/barcode-master-api/BarcodeAPI.Application`: application use-case layer. Currently contains `CreateProductCommand`; this layer is still thin.
- `src/services/barcode-master-api/BarcodeAPI.Domain`: domain model and repository abstractions. Owns `Product`, `BaseEntity`, and `IProductRepository`.
- `src/services/barcode-master-api/BarcodeAPI.Infrastructure`: persistence implementation. Owns EF Core `ApplicationDbContext`, migrations, entity configuration, and `ProductRepository`.

The API is controller-based. Do not add new Minimal API endpoints in `Program.cs`; add controllers under `src/services/barcode-master-api/BarcodeAPI.Api/Controllers`.

## Request Flow

1. `Program.cs` registers services and middleware.
2. ASP.NET Core authentication runs before authorization.
3. Controllers handle HTTP requests and validate request shape.
4. Controllers depend on `IProductRepository`, not directly on EF Core or Dapper.
5. `ProductRepository` uses EF Core for tracked writes and Dapper/raw SQL for fast lookup queries.
6. API responses are built from DTOs in `src/services/barcode-master-api/BarcodeAPI.Api/Contracts`.

## Authentication And Authorization

Protected product endpoints use the `DualAuth` policy.

`DualAuth` accepts either:

- JWT Bearer auth through `JwtBearerDefaults.AuthenticationScheme`
- API key auth through the custom `ApiKeyAuthenticationHandler`

The API key is read from `Authentication:ApiKey`. The request header defaults to `X-API-Key`.

## Product Endpoints

`ProductsController` exposes:

- `GET /api/products/{barcode}`: trims and looks up one barcode.
- `POST /api/products/lookup`: trims and batch-looks-up barcodes, returning found products plus missing barcode values.
- `POST /api/products/batch`: validates product records, inserts valid records, and returns failed records with reasons.

Current behavior is local-database-only. OpenFoodFacts or other external fallback lookup is not implemented.

## Persistence

`ApplicationDbContext` maps the `Products` table.

`ProductRepository` behavior:

- `AddAsync`: EF Core insert plus `SaveChangesAsync`.
- `GetByBarcodeAsync`: Dapper query using the EF Core connection.
- `GetByBarcodesAsync`: Dapper batch query using PostgreSQL `ANY`.
- `AddBatchAsync`: checks existing barcodes, inserts via PostgreSQL `UNNEST`, and reports per-record failures.

## Testing

Tests live in `tests/services/barcode-master-api/BarcodeAPI.Api.Tests`.

The current test project uses xUnit. Add focused controller or behavior tests there when changing endpoint behavior. Run:

```bash
dotnet test
```

## Change Guidance

- Keep HTTP contracts in `BarcodeAPI.Api/Contracts`.
- Keep domain entities and interfaces free of ASP.NET Core dependencies.
- Keep persistence details in `BarcodeAPI.Infrastructure`.
- Prefer adding repository methods to `IProductRepository` when controllers need new persistence behavior.
- Avoid adding external provider behavior without explicit requirements and tests.
