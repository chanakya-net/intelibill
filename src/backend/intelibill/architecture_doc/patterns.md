# Merchant Backend Patterns

Implementation patterns of the merchant API with canonical file references. See also:
- **Architecture**: [`architecture.md`](architecture.md)
- **Build/test commands**: [`README.md`](../README.md) and the repo-root [`AGENTS.md`](../../../../AGENTS.md)

Check the referenced files for the canonical implementation before adding new code.

### 1. Clean Architecture / Onion Layering

Dependencies flow strictly inward: `Domain ← Application ← Infrastructure ← Api`.

**Convention:** New features go in `Application/Features/<FeatureName>/`. Infrastructure concerns (DB, external APIs) never leak into Application or Domain.

See [`architecture.md`](architecture.md) for layer responsibilities.

---

### 2. DI Module Registration (Extension Method per Layer)

Each layer owns a single `DependencyInjection.cs` with an `IServiceCollection` extension method. `Program.cs` calls them in order.

**Where it appears:**
- `src/backend/intelibill/Intelibill.Application/DependencyInjection.cs:8` — `AddApplication()`
- `src/backend/intelibill/Intelibill.Infrastructure/DependencyInjection.cs:13` — `AddInfrastructure()`
- `src/backend/intelibill/Intelibill.Api/Program.cs:11-12` — call site

**Convention:** Register all of a layer's services here. Do not register Infrastructure services inside Application, and vice versa.

---

### 3. Repository Pattern

A generic interface defined in Domain; a generic EF Core base class in Infrastructure.

**Where it appears:**
- Contract: `src/backend/intelibill/Intelibill.Domain/Interfaces/Repositories/IRepository.cs:6-14`
- Implementation base: `src/backend/intelibill/Intelibill.Infrastructure/Repositories/RepositoryBase.cs:9-28`

**Convention:** Concrete repositories go in `Infrastructure/Repositories/`, extend `RepositoryBase<TEntity>`, and implement a domain-specific interface (e.g., `IProductRepository : IRepository<Product>`). Register scoped in `Infrastructure/DependencyInjection.cs`.

---

### 4. Unit of Work Pattern

Wraps `SaveChangesAsync` so Application handlers stay decoupled from EF Core.

**Where it appears:**
- Interface: `src/backend/intelibill/Intelibill.Domain/Interfaces/IUnitOfWork.cs:3-6`
- Implementation: `src/backend/intelibill/Intelibill.Infrastructure/Data/UnitOfWork.cs:5-8`
- DI registration: `src/backend/intelibill/Intelibill.Infrastructure/DependencyInjection.cs:27`

**Convention:** Handlers inject `IUnitOfWork` and call `SaveChangesAsync` once at the end of a command. Never call `DbContext.SaveChangesAsync` directly from Application code.

---

### 5. Result Pattern (ErrorOr)

All operations that can fail return `ErrorOr<T>` instead of throwing exceptions. Errors are declared centrally and mapped to HTTP Problem Details at the API boundary.

**Where it appears:**
- Error definitions: `src/backend/intelibill/Intelibill.Application/Common/Errors/Errors.cs:8-20` — `partial class Errors` with nested static classes per aggregate
- HTTP mapping: `src/backend/intelibill/Intelibill.Api/Extensions/ErrorOrExtensions.cs:8-40` — `ToActionResult<T>()` and `ToProblemResult()`
- Validation integration: `src/backend/intelibill/Intelibill.Application/Common/Behaviours/ValidationBehaviour.cs:28-29` — FluentValidation failures become `Error.Validation`

**Convention:**
- Extend `Errors.cs` with a new `partial class Errors { public static class <AggregateName> { ... } }` file per aggregate.
- Handler return types: `Task<ErrorOr<TResult>>`.
- Controllers call `.ToActionResult(value => Ok(value))` — never switch on error types in the controller.

---

### 6. Options Pattern (Strongly-Typed Configuration)

All settings are bound to validated `*Options` classes via `IOptions<T>`, never read directly from `IConfiguration`.

**Where it appears:**
- Options class: `src/backend/intelibill/Intelibill.Infrastructure/Options/DatabaseOptions.cs:5-25` — `[Required]` and `[Range]` annotations enable `ValidateDataAnnotations()`
- Registration: `src/backend/intelibill/Intelibill.Infrastructure/DependencyInjection.cs:15-18` — `.ValidateDataAnnotations().ValidateOnStart()` causes startup failure on misconfiguration
- Config section key: `DatabaseOptions.SectionName` constant at `src/backend/intelibill/Intelibill.Infrastructure/Options/DatabaseOptions.cs:7`

**Convention:** Add new `*Options` classes under the relevant layer's `Options/` folder. Always add `ValidateDataAnnotations()` and `ValidateOnStart()`. Section name is a `const string SectionName` on the class itself.

---

### 7. Domain Events

Entities raise events internally; Wolverine publishes them after `SaveChangesAsync`.

**Where it appears:**
- Event interface: `src/backend/intelibill/Intelibill.Domain/Common/IDomainEvent.cs:3-7`
- Collection on entity: `src/backend/intelibill/Intelibill.Domain/Common/BaseEntity.cs:9-13` — `AddDomainEvent` / `ClearDomainEvents`

**Convention:** Domain event classes live in `Domain/Events/`. Entities call `AddDomainEvent(...)` inside mutating methods. Infrastructure (or Wolverine outbox) dispatches and clears events after the transaction.

---

### 8. Value Objects

Structural equality by components, not by reference.

**Where it appears:**
- Base class: `src/backend/intelibill/Intelibill.Domain/Common/ValueObject.cs:3-18` — `GetEqualityComponents()` abstract method; `==` / `!=` operators wired up

**Convention:** Value objects go in `Domain/ValueObjects/`. Override `GetEqualityComponents()` to yield all identity fields. Properties are immutable (`init`-only).

---

### 9. EF Core Entity Configuration (Fluent API)

All EF mappings are in separate `IEntityTypeConfiguration<T>` classes, not in `OnModelCreating`.

**Where it appears:**
- Discovery call: `src/backend/intelibill/Intelibill.Infrastructure/Data/ApplicationDbContext.cs:11` — `ApplyConfigurationsFromAssembly(...)` scans Infrastructure automatically
- Config classes: `src/backend/intelibill/Intelibill.Infrastructure/Data/Configurations/`

**Convention:** One configuration class per entity, named `<Entity>Configuration.cs`. Do not use data annotations on domain entities.

---

### 10. Automatic Audit Fields

`UpdatedAt` is set by the `DbContext` on every `SaveChangesAsync`.

**Where it appears:**
- `src/backend/intelibill/Intelibill.Infrastructure/Data/ApplicationDbContext.cs:21-29` — iterates `ChangeTracker.Entries<BaseEntity>()` before delegating to base
- Audit properties: `src/backend/intelibill/Intelibill.Domain/Common/BaseEntity.cs:5-7`

---

### 11. Global Exception Handling Middleware

Unhandled exceptions are caught at the pipeline level and answered as RFC 7807 Problem Details with a stable `code` and `traceId` — never the exception's text. This is the last-resort safety net — expected errors use ErrorOr. See [`architecture.md`](architecture.md) § Safe Errors and Diagnostics.

**Where it appears:**
- Implementation (shared by the merchant and licensing APIs): `src/backend/shared/Intelibill.Shared.Web/Middleware/ExceptionHandlingMiddleware.cs`
- Public mapping: `src/backend/shared/Intelibill.Shared.Web/PublicErrors/SafeErrorFactory.cs`
- Diagnostics: `src/backend/shared/Intelibill.Shared.Web/Logging/SafeDiagnosticWriter.cs`
- Registration: `AddSafeErrorHandling()` + `UseMiddleware<ExceptionHandlingMiddleware>` in `src/backend/intelibill/Intelibill.Api/Program.cs` and `src/backend/licensing/Intelibill.Licensing.Api/Program.cs`

---

### 12. Wolverine Handler Discovery

Wolverine auto-discovers handlers by convention from the Application assembly.

**Where it appears:**
- `src/backend/intelibill/Intelibill.Api/Program.cs:14-17` — `UseWolverine` with `IncludeAssembly`
- `src/backend/intelibill/Intelibill.Application/Common/Behaviours/ValidationBehaviour.cs` — Wolverine middleware (`BeforeAsync`) integrating FluentValidation

**Convention:** Handler classes go in `Application/Features/<FeatureName>/`. Handler method named `Handle` or `HandleAsync`, first parameter is the command/query. `ValidationBehaviour<TMessage>` runs automatically for any message with a registered `IValidator<TMessage>`.

---

### 13. Streaming Catalog Endpoint

Large catalog reads are streamed via `IAsyncEnumerable<T>` to avoid loading all rows into memory before the first byte is sent.

**Where it appears:**
- Interface: `src/backend/intelibill/Intelibill.Application/Common/Interfaces/IItemCatalogStreamingService.cs` — `StreamByShopAsync` returns `IAsyncEnumerable<ItemCatalogEntryDto>`
- Controller: `src/backend/intelibill/Intelibill.Api/Controllers/ItemsController.cs` — `GET /api/items/stream` iterates with `await foreach` and writes JSON lines
- Query handler: `src/backend/intelibill/Intelibill.Application/Features/Items/Services/`

**Convention:** Use `IAsyncEnumerable<T>` for unbounded or large result sets. Controller writes directly to the response stream — do not buffer into a list. Pass `CancellationToken` through the entire chain.

---

### 14. Rate Limiting

MVC controller actions are rate-limited by default via a custom action filter, not ASP.NET Core's built-in rate limiting middleware.

**Where it appears:**
- Default config: `src/backend/intelibill/Intelibill.Api/appsettings.json` — `RateLimiting:Limit`, `PeriodInMinutes`, `BackoffMinutes`
- Override attribute: `src/backend/intelibill/Intelibill.Api/Middleware/RateLimiting/RateLimitAttribute.cs`
- Opt-out attribute: `src/backend/intelibill/Intelibill.Api/Middleware/RateLimiting/DisableRateLimitAttribute.cs`
- Filter: `src/backend/intelibill/Intelibill.Api/Middleware/RateLimiting/RateLimitFilter.cs`

**Convention:** Configure global defaults in the `RateLimiting` section (`Limit = 100`, `PeriodInMinutes = 1`, `BackoffMinutes = 3` in committed config). Use `[RateLimit]` only when an action or controller needs stricter/custom limits. Use `[DisableRateLimit]` as the explicit manual exemption for a controller or action.

This filter applies to MVC controllers only. Scalar/OpenAPI endpoints, Wolverine HTTP endpoints, and SignalR hubs are outside this MVC filter and need separate rate-limit protection if exposed.

---

### 15. Structured Logging (Serilog)

Serilog is configured with a `SensitiveDataDestructuringPolicy` to prevent PII/secrets from appearing in logs.

**Where it appears:**
- Setup: `src/backend/intelibill/Intelibill.Api/Extensions/SerilogExtensions.cs`
- Policy: `src/backend/intelibill/Intelibill.Api/Logging/SensitiveDataDestructuringPolicy.cs`

**Convention:** Mark sensitive domain properties with `[SensitiveData]` (`src/backend/intelibill/Intelibill.Domain/Attributes/SensitiveDataAttribute.cs`) — the policy strips them from log output automatically.

---

### 16. Central Package Management (NuGet)

All NuGet versions declared once in `Directory.Packages.props`; project files reference packages without versions.

**Where it appears:**
- Version declarations: `Directory.Packages.props` (repo root)
- Enabled by: `Directory.Build.props:14` — `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>`

**Convention:** To add a package: (1) add `<PackageVersion>` to `Directory.Packages.props`, (2) add a version-free `<PackageReference>` in the target `.csproj`.

---

### 17. Posting Handler Steps and Collaborators

Handlers that post money or stock read as an ordered list of named steps. Work that needs its own repositories lives in a small collaborator class next to the handler, so the handler injects only what its top-level flow calls.

**Where it appears:**
- Sale posting: `src/backend/intelibill/Intelibill.Application/Features/Sales/Commands/RecordSale/` — `RecordSaleCommandHandler` with `SaleReplayReader`, `SalePostingContextReader`, `SaleLinePricer`, `SaleCreditDecider`, `SaleCreditNoteRedeemer`, `SaleStockAndDuePoster`
- Sale returns: `src/backend/intelibill/Intelibill.Application/Features/Sales/Services/Returns/SaleReturnStockPoster.cs` (shared by record and void), `Commands/RecordSaleReturn/SaleReturnCreditIssuer.cs`
- Supplier returns: `src/backend/intelibill/Intelibill.Application/Features/SupplierReturns/Services/` — `SupplierReturnShipper`, `PurchaseReceiptDetailReader`, `PurchaseReceiptLotReverser`
- Reconciliation: `src/backend/intelibill/Intelibill.Application/Features/OfflineReconciliation/Commands/ResolveReconciliationCase/` — `ReconciliationCaseAuthorizer`, `ReconciliationExpiryReviewVerifier`, `ReconciliationStockCorrectionLinker`, `ReconciliationSaleLinker`

**Convention:**
- The handler's top-level method keeps the transaction (begin, save, commit, rollback), its `try`/`catch`/`finally`, and every lock or fence acquisition, in lock order. Do not put the licence fence, posting period policy, expiry fence, till posting or row-lock calls behind one facade.
- A collaborator is a `public sealed` class (a public handler constructor cannot take an internal type) with `internal` methods where its parameter types allow, registered `AddScoped` in `Intelibill.Application/DependencyInjection.cs` so it shares the request's unit of work. It holds no state between calls and never begins, commits or saves a transaction.
- A collaborator method that takes a row lock says so in its name (`LockCustomerAndDecideCreditAsync`, `ReverseStockUnderBatchLockAsync`).
- Never name a collaborator `*Handler` or `*Consumer`, or give it a `Handle`/`Consume` method: Wolverine would discover it as a message handler.
