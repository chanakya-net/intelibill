# Architectural Patterns

Implementation patterns with canonical file references. For high-level architecture overview see:
- **Backend**: [`backend-architecture.md`](backend-architecture.md)
- **Frontend**: [`frontend-architecture.md`](frontend-architecture.md)

Check the referenced files for the canonical implementation before adding new code.

---

## Backend Patterns

### 1. Clean Architecture / Onion Layering

Dependencies flow strictly inward: `Domain ← Application ← Infrastructure ← Api`.

**Convention:** New features go in `Application/Features/<FeatureName>/`. Infrastructure concerns (DB, external APIs) never leak into Application or Domain.

See [`backend-architecture.md`](backend-architecture.md) for layer responsibilities.

---

### 2. DI Module Registration (Extension Method per Layer)

Each layer owns a single `DependencyInjection.cs` with an `IServiceCollection` extension method. `Program.cs` calls them in order.

**Where it appears:**
- `src/backend/Intelibill.Application/DependencyInjection.cs:8` — `AddApplication()`
- `src/backend/Intelibill.Infrastructure/DependencyInjection.cs:13` — `AddInfrastructure()`
- `src/backend/Intelibill.Api/Program.cs:11-12` — call site

**Convention:** Register all of a layer's services here. Do not register Infrastructure services inside Application, and vice versa.

---

### 3. Repository Pattern

A generic interface defined in Domain; a generic EF Core base class in Infrastructure.

**Where it appears:**
- Contract: `src/backend/Intelibill.Domain/Interfaces/Repositories/IRepository.cs:6-14`
- Implementation base: `src/backend/Intelibill.Infrastructure/Repositories/RepositoryBase.cs:9-28`

**Convention:** Concrete repositories go in `Infrastructure/Repositories/`, extend `RepositoryBase<TEntity>`, and implement a domain-specific interface (e.g., `IProductRepository : IRepository<Product>`). Register scoped in `Infrastructure/DependencyInjection.cs`.

---

### 4. Unit of Work Pattern

Wraps `SaveChangesAsync` so Application handlers stay decoupled from EF Core.

**Where it appears:**
- Interface: `src/backend/Intelibill.Domain/Interfaces/IUnitOfWork.cs:3-6`
- Implementation: `src/backend/Intelibill.Infrastructure/Data/UnitOfWork.cs:5-8`
- DI registration: `src/backend/Intelibill.Infrastructure/DependencyInjection.cs:27`

**Convention:** Handlers inject `IUnitOfWork` and call `SaveChangesAsync` once at the end of a command. Never call `DbContext.SaveChangesAsync` directly from Application code.

---

### 5. Result Pattern (ErrorOr)

All operations that can fail return `ErrorOr<T>` instead of throwing exceptions. Errors are declared centrally and mapped to HTTP Problem Details at the API boundary.

**Where it appears:**
- Error definitions: `src/backend/Intelibill.Application/Common/Errors/Errors.cs:8-20` — `partial class Errors` with nested static classes per aggregate
- HTTP mapping: `src/backend/Intelibill.Api/Extensions/ErrorOrExtensions.cs:8-40` — `ToActionResult<T>()` and `ToProblemResult()`
- Validation integration: `src/backend/Intelibill.Application/Common/Behaviours/ValidationBehaviour.cs:28-29` — FluentValidation failures become `Error.Validation`

**Convention:**
- Extend `Errors.cs` with a new `partial class Errors { public static class <AggregateName> { ... } }` file per aggregate.
- Handler return types: `Task<ErrorOr<TResult>>`.
- Controllers call `.ToActionResult(value => Ok(value))` — never switch on error types in the controller.

---

### 6. Options Pattern (Strongly-Typed Configuration)

All settings are bound to validated `*Options` classes via `IOptions<T>`, never read directly from `IConfiguration`.

**Where it appears:**
- Options class: `src/backend/Intelibill.Infrastructure/Options/DatabaseOptions.cs:5-25` — `[Required]` and `[Range]` annotations enable `ValidateDataAnnotations()`
- Registration: `src/backend/Intelibill.Infrastructure/DependencyInjection.cs:15-18` — `.ValidateDataAnnotations().ValidateOnStart()` causes startup failure on misconfiguration
- Config section key: `DatabaseOptions.SectionName` constant at `src/backend/Intelibill.Infrastructure/Options/DatabaseOptions.cs:7`

**Convention:** Add new `*Options` classes under the relevant layer's `Options/` folder. Always add `ValidateDataAnnotations()` and `ValidateOnStart()`. Section name is a `const string SectionName` on the class itself.

---

### 7. Domain Events

Entities raise events internally; Wolverine publishes them after `SaveChangesAsync`.

**Where it appears:**
- Event interface: `src/backend/Intelibill.Domain/Common/IDomainEvent.cs:3-7`
- Collection on entity: `src/backend/Intelibill.Domain/Common/BaseEntity.cs:9-13` — `AddDomainEvent` / `ClearDomainEvents`

**Convention:** Domain event classes live in `Domain/Events/`. Entities call `AddDomainEvent(...)` inside mutating methods. Infrastructure (or Wolverine outbox) dispatches and clears events after the transaction.

---

### 8. Value Objects

Structural equality by components, not by reference.

**Where it appears:**
- Base class: `src/backend/Intelibill.Domain/Common/ValueObject.cs:3-18` — `GetEqualityComponents()` abstract method; `==` / `!=` operators wired up

**Convention:** Value objects go in `Domain/ValueObjects/`. Override `GetEqualityComponents()` to yield all identity fields. Properties are immutable (`init`-only).

---

### 9. EF Core Entity Configuration (Fluent API)

All EF mappings are in separate `IEntityTypeConfiguration<T>` classes, not in `OnModelCreating`.

**Where it appears:**
- Discovery call: `src/backend/Intelibill.Infrastructure/Data/ApplicationDbContext.cs:11` — `ApplyConfigurationsFromAssembly(...)` scans Infrastructure automatically
- Config classes: `src/backend/Intelibill.Infrastructure/Data/Configurations/`

**Convention:** One configuration class per entity, named `<Entity>Configuration.cs`. Do not use data annotations on domain entities.

---

### 10. Automatic Audit Fields

`UpdatedAt` is set by the `DbContext` on every `SaveChangesAsync`.

**Where it appears:**
- `src/backend/Intelibill.Infrastructure/Data/ApplicationDbContext.cs:21-29` — iterates `ChangeTracker.Entries<BaseEntity>()` before delegating to base
- Audit properties: `src/backend/Intelibill.Domain/Common/BaseEntity.cs:5-7`

---

### 11. Global Exception Handling Middleware

Unhandled exceptions are caught at the pipeline level and serialised as RFC 7807 Problem Details. This is the last-resort safety net — expected errors use ErrorOr.

**Where it appears:**
- Implementation: `src/backend/Intelibill.Api/Middleware/ExceptionHandlingMiddleware.cs:6-44`
- Registration (first middleware): `src/backend/Intelibill.Api/Program.cs:21`

---

### 12. Wolverine Handler Discovery

Wolverine auto-discovers handlers by convention from the Application assembly.

**Where it appears:**
- `src/backend/Intelibill.Api/Program.cs:14-17` — `UseWolverine` with `IncludeAssembly`
- `src/backend/Intelibill.Application/Common/Behaviours/ValidationBehaviour.cs` — Wolverine middleware (`BeforeAsync`) integrating FluentValidation

**Convention:** Handler classes go in `Application/Features/<FeatureName>/`. Handler method named `Handle` or `HandleAsync`, first parameter is the command/query. `ValidationBehaviour<TMessage>` runs automatically for any message with a registered `IValidator<TMessage>`.

---

### 13. Central Package Management

All NuGet versions declared once in `Directory.Packages.props`; project files reference packages without versions.

**Where it appears:**
- Version declarations: `Directory.Packages.props` (repo root)
- Enabled by: `Directory.Build.props:14` — `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>`

**Convention:** To add a package: (1) add `<PackageVersion>` to `Directory.Packages.props`, (2) add a version-free `<PackageReference>` in the target `.csproj`.

---

## Frontend Patterns

See [`frontend-architecture.md`](frontend-architecture.md) for folder structure, state shape, and routing flow.

### 1. Standalone Components

No NgModules. Every component declares its own `imports` array.

**Convention:** Always use `standalone: true`. Import only what the component template directly uses — no barrel imports of entire feature sets.

---

### 2. Feature-First Layout

Each feature is self-contained: its own routes, state, and components under `features/<name>/`.

**Convention:** New features go in `src/frontend/src/app/features/<feature-name>/`. Shared concerns (auth service, guards, interceptors) go in `core/`. Do not put feature-specific state in the root store.

---

### 3. NgRx State Boundaries

Root store holds global/shared state; feature state is provided lazily per route.

**Where it appears:**
- Root reducers wired in: `src/frontend/src/app/app.config.ts`
- Feature state example: `src/frontend/src/app/features/overview/` and `features/operations/`

**Convention:** Use NgRx for shared or async state. Use component-local state (`signal` or property) for simple UI-only cases. Never put transient UI state (e.g., a single dropdown open/close) in the store.

---

### 4. Interceptor Chain

Auth and loading concerns are cross-cutting and wired once in `app.config.ts`.

**Where it appears:**
- `src/frontend/src/app/core/interceptors/` — `authInterceptor` (bearer token + 401 refresh with loop prevention), HTTP loading tracker
- Wired in: `src/frontend/src/app/app.config.ts`

**Convention:** Add new cross-cutting HTTP concerns as interceptors, not as service wrappers. Order matters — auth interceptor must run before the loading tracker.

---

### 5. Route Guards

`authGuard` protects all shell/app routes. Public routes (`/login`, `/register`) are unguarded.

**Where it appears:**
- `src/frontend/src/app/core/guards/`
- Applied in: `src/frontend/src/app/app.routes.ts`

**Convention:** Apply `authGuard` at the shell route level, not on individual leaf routes.
