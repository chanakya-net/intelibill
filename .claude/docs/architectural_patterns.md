# Architectural Patterns

Patterns present across multiple files in the backend. Check the referenced files for the canonical implementation before adding new code.

---

## 1. Clean Architecture / Onion Layering

Dependencies flow strictly inward: `Domain ← Application ← Infrastructure ← Api`.

**Where it appears:**
- Enforced by project references in all four `.csproj` files (`src/backend/`)
- Domain has no `<PackageReference>` or `<ProjectReference>` at all — verified in `src/backend/Intelibill.Domain/Intelibill.Domain.csproj`
- Application references only Domain: `src/backend/Intelibill.Application/Intelibill.Application.csproj`
- Infrastructure references Domain + Application: `src/backend/Intelibill.Infrastructure/Intelibill.Infrastructure.csproj`
- API references Application + Infrastructure: `src/backend/Intelibill.Api/Intelibill.Api.csproj`

**Convention:** New features go in `Application/Features/<FeatureName>/`. Infrastructure concerns (DB, external APIs) never leak into Application or Domain.

---

## 2. DI Module Registration (Extension Method per Layer)

Each layer owns a single `DependencyInjection.cs` with a `IServiceCollection` extension method. `Program.cs` calls them in order.

**Where it appears:**
- `src/backend/Intelibill.Application/DependencyInjection.cs:8` — `AddApplication()`
- `src/backend/Intelibill.Infrastructure/DependencyInjection.cs:13` — `AddInfrastructure()`
- `src/backend/Intelibill.Api/Program.cs:11-12` — call site

**Convention:** Register all of a layer's services here. Do not register Infrastructure services inside Application, and vice versa.

---

## 3. Repository Pattern

A generic interface defined in Domain; a generic EF Core base class in Infrastructure.

**Where it appears:**
- Contract: `src/backend/Intelibill.Domain/Interfaces/Repositories/IRepository.cs:6-14`
- Implementation base: `src/backend/Intelibill.Infrastructure/Repositories/RepositoryBase.cs:9-28`

**Convention:** Concrete repositories (e.g., `ProductRepository`) go in `Infrastructure/Repositories/`, extend `RepositoryBase<TEntity>`, and implement a domain-specific interface (e.g., `IProductRepository : IRepository<Product>`). Register them scoped in `Infrastructure/DependencyInjection.cs`.

---

## 4. Unit of Work Pattern

Wraps `SaveChangesAsync` so Application handlers remain decoupled from EF Core.

**Where it appears:**
- Interface: `src/backend/Intelibill.Domain/Interfaces/IUnitOfWork.cs:3-6`
- Implementation: `src/backend/Intelibill.Infrastructure/Data/UnitOfWork.cs:5-8`
- DI registration: `src/backend/Intelibill.Infrastructure/DependencyInjection.cs:27`

**Convention:** Handlers inject `IUnitOfWork` and call `SaveChangesAsync` once at the end of a command. Never call `DbContext.SaveChangesAsync` directly from Application code.

---

## 5. Result Pattern (ErrorOr)

All operations that can fail return `ErrorOr<T>` instead of throwing exceptions. Errors are declared centrally and mapped to HTTP Problem Details at the API boundary.

**Where it appears:**
- Error definitions: `src/backend/Intelibill.Application/Common/Errors/Errors.cs:8-20` — `partial class Errors` with nested static classes per aggregate
- HTTP mapping: `src/backend/Intelibill.Api/Extensions/ErrorOrExtensions.cs:8-40` — `ToActionResult<T>()` and `ToProblemResult()`
- Validation integration: `src/backend/Intelibill.Application/Common/Behaviours/ValidationBehaviour.cs:28-29` — FluentValidation failures become `Error.Validation`

**Convention:**
- Extend `Errors.cs` with a new `partial class Errors { public static class <AggregateName> { ... } }` file per aggregate.
- Handler return types: `Task<ErrorOr<TResult>>`.
- Controllers call `.ToActionResult(value => Ok(value))` — never `switch` on error types in the controller.

---

## 6. Options Pattern (Strongly-Typed Configuration)

All settings are bound to validated `*Options` classes via `IOptions<T>`, never read directly from `IConfiguration`.

**Where it appears:**
- Options class: `src/backend/Intelibill.Infrastructure/Options/DatabaseOptions.cs:5-25` — `[Required]` and `[Range]` annotations enable `ValidateDataAnnotations()`
- Registration: `src/backend/Intelibill.Infrastructure/DependencyInjection.cs:15-18` — `.ValidateDataAnnotations().ValidateOnStart()` causes startup failure on misconfiguration
- Config section key: `DatabaseOptions.SectionName` constant at `src/backend/Intelibill.Infrastructure/Options/DatabaseOptions.cs:7`

**Convention:** Add new `*Options` classes under the relevant layer's `Options/` folder. Always add `ValidateDataAnnotations()` and `ValidateOnStart()`. Section name is a `const string SectionName` on the class itself.

---

## 7. Domain Events

Entities raise events internally; the dispatcher (Wolverine) publishes them after `SaveChangesAsync`.

**Where it appears:**
- Event interface: `src/backend/Intelibill.Domain/Common/IDomainEvent.cs:3-7`
- Collection on entity: `src/backend/Intelibill.Domain/Common/BaseEntity.cs:9-13` — private `_domainEvents` list, public `AddDomainEvent` / `ClearDomainEvents`

**Convention:** Domain event classes live in `Domain/Events/`. Entities call `AddDomainEvent(new SomethingHappenedEvent(...))` inside mutating methods. Infrastructure (or a Wolverine outbox) is responsible for dispatching and clearing events after the transaction.

---

## 8. Value Objects

Structural equality by components, not by reference.

**Where it appears:**
- Base class: `src/backend/Intelibill.Domain/Common/ValueObject.cs:3-18` — `GetEqualityComponents()` abstract method; `==` / `!=` operators and `IEquatable<ValueObject>` wired up

**Convention:** Value object classes go in `Domain/ValueObjects/`. Override `GetEqualityComponents()` to yield all fields that constitute identity. Value objects are immutable (`init`-only properties).

---

## 9. EF Core Entity Configuration (Fluent API)

All EF mappings are in separate `IEntityTypeConfiguration<T>` classes, not in `DbContext.OnModelCreating`.

**Where it appears:**
- Discovery call: `src/backend/Intelibill.Infrastructure/Data/ApplicationDbContext.cs:11` — `ApplyConfigurationsFromAssembly(...)` scans the Infrastructure assembly automatically
- Config classes go in: `src/backend/Intelibill.Infrastructure/Data/Configurations/` *(empty, to be populated)*

**Convention:** One configuration class per entity, file named `<Entity>Configuration.cs`. Use `builder.HasKey`, `builder.Property`, etc. Do not use data annotations on domain entities.

---

## 10. Automatic Audit Fields

`UpdatedAt` is set automatically by the `DbContext` on every `SaveChangesAsync`, keeping audit logic out of handlers.

**Where it appears:**
- `src/backend/Intelibill.Infrastructure/Data/ApplicationDbContext.cs:21-29` — iterates `ChangeTracker.Entries<BaseEntity>()` before delegating to base
- Audit properties defined: `src/backend/Intelibill.Domain/Common/BaseEntity.cs:5-7`

---

## 11. Global Exception Handling Middleware

Unhandled exceptions are caught at the pipeline level and serialised as RFC 7807 Problem Details JSON. This is the last-resort safety net only — expected errors should use `ErrorOr`.

**Where it appears:**
- Implementation: `src/backend/Intelibill.Api/Middleware/ExceptionHandlingMiddleware.cs:6-44`
- Registration (first middleware): `src/backend/Intelibill.Api/Program.cs:21`

---

## 12. Wolverine Handler Discovery

Wolverine auto-discovers handlers by convention. The Application assembly is explicitly registered so handlers defined there are found.

**Where it appears:**
- `src/backend/Intelibill.Api/Program.cs:14-17` — `UseWolverine` with `IncludeAssembly`
- `src/backend/Intelibill.Application/Common/Behaviours/ValidationBehaviour.cs` — Wolverine middleware (`BeforeAsync`) integrating FluentValidation

**Convention:** Handler classes go in `Application/Features/<FeatureName>/`. A handler method is named `Handle` or `HandleAsync` and takes the command/query as its first parameter. The `ValidationBehaviour<TMessage>` pipeline middleware runs automatically for any message that has a registered `IValidator<TMessage>`.

---

## 13. Central Package Management

All NuGet versions are declared once in `Directory.Packages.props` at the repo root. Project files reference packages without versions.

**Where it appears:**
- Version declarations: `Directory.Packages.props` (repo root) — grouped by layer via `Label` attributes
- Enabled by: `Directory.Build.props:14` — `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>`
- Consumed by: all seven `.csproj` files — `<PackageReference Include="..." />` with no `Version` attribute

**Convention:** To add a package: (1) add `<PackageVersion>` to `Directory.Packages.props` under the correct label group, (2) add a version-free `<PackageReference>` in the target `.csproj`.
