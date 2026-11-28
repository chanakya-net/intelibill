# Shared Backend Libraries

Three class libraries (`net10.0`) that hold the code the merchant API and the licensing service must
run identically: the wire contracts between the two services, the credential and service-assertion
primitives, and the web-host building blocks for safe errors, logging, tracing and outbox delivery.

They are not deployable and have no solution of their own. They are compiled into both consumers:

- Merchant backend: `src/backend/intelibill` ([architecture](../intelibill/architecture_doc/architecture.md))
- Licensing service: `src/backend/licensing` ([architecture](../licensing/architecture_doc/architecture.md))

Design, key types, invariants and consumer wiring: [architecture_doc/architecture.md](architecture_doc/architecture.md).

## Layout

| Path | Purpose |
|---|---|
| `Intelibill.Shared.Contracts/V1/` | Versioned DTOs, paths, limits and validation shared by both services (`*V1` types). No project or package references. |
| `Intelibill.Shared.Security/` | Password hashing, opaque tokens, signed service assertions, entitlement transport options. References `Contracts` and `BCrypt.Net-Next` only. |
| `Intelibill.Shared.Web/` | `Logging/`, `PublicErrors/`, `Middleware/`, `Observability/`, `Http/`, `Persistence/`, `Delivery/`. ASP.NET Core, EF Core, Serilog, OpenTelemetry, FluentValidation; no project references. |
| `../../../tests/backend/shared/` | Consumer regression test projects plus two source files linked into other test projects. |

## Build

The three projects are listed in every backend solution, so any of these builds them:

```bash
dotnet build Intelibill.slnx                                    # repository root: all backends, shared, all tests
dotnet build src/backend/intelibill/Intelibill.slnx            # merchant backend + shared
dotnet build src/backend/licensing/Intelibill.Licensing.slnx   # licensing service + shared
```

Root `Directory.Build.props` and `Directory.Packages.props` apply: warnings are errors, and package
versions live only in `Directory.Packages.props` (`<PackageReference>` entries here are versionless).

## Test

The libraries have no unit test project of their own. They are verified through their consumers:

```bash
dotnet test tests/backend/shared/P0102ConsumerRegressionTests   # Shared.Web: errors, log sinks, spans
dotnet test tests/backend/shared/P0103ConsumerRegressionTests   # Shared.Security / Contracts: hashing, tokens, boundaries
```

- Both projects need Docker (Testcontainers PostgreSQL) and start the real merchant and licensing API hosts.
- They are part of the root `Intelibill.slnx` only, not of the two per-backend solutions.
- `scripts/dev/test-integration.sh` does not run them; it runs the merchant or licensing integration
  project. It does include `src/backend/shared` in its Wolverine codegen cache fingerprint, so a
  change here regenerates the merchant handlers on the next run.
- CI (`.github/workflows/deploy.yml`) runs every test project found under `tests/backend/shared`.
- Merchant unit tests in `tests/backend/intelibill/unit/Intelibill.Api.Unit.Tests` (`Logging/`,
  `Middleware/ExceptionHandlingMiddlewareTests.cs`) also exercise `Shared.Web` types.

`tests/backend/shared/IntegrationTestRuntime.cs` and `TestSales.cs` are not library tests: they are
source files linked (`<Compile Include … Link>`) into the merchant and licensing test projects.

## Rules for changing these libraries

- Every change affects both services. Build the root `Intelibill.slnx` and run both consumer
  regression projects; then run the targeted merchant and licensing suites for the touched seam.
- Keep the dependency rules in the architecture doc. They are asserted by
  `BuildBoundaryTests` and `SharedWeb_HasNoApplicationLayerDependency`: no shared project may
  reference a merchant or licensing project, and `Contracts` / `Security` take no ASP.NET Core or
  EF Core dependency.
- `Contracts/V1` types are the wire format between two separately deployed services. Do not change
  the shape, JSON names, paths or limits of an existing `V1` type in a way one side would reject.
- Never weaken the redaction, safe-error or assertion-verification behaviour to make a log or
  response more informative; see the invariants in the architecture doc.
- Adding a shared project means adding it to all three `.slnx` files and to the restore `COPY`
  lines of the `Dockerfile` and `Dockerfile.migrate` of both backends.
- `Contracts/V1/LicenseNoticesV1.cs` defines separate operational shop (`ShopLicenseNoticesV1`),
  commercial account (`AccountLicenseNoticesV1`), provider preview (`ProviderNotificationsPageV1`),
  paged contact (`BillingContactsPageV1`), and minimized recipient-authority wire DTOs (no raw phone
  numbers, no staff emails, no verification proof tokens, and bounded shop-name samples).

The V1 licence notice contract separates named billing-actor lookup from affiliation-bound shop roster lookup. See the [architecture guide](architecture_doc/architecture.md#licence-notice-v1-contracts-p07-01).

Provider notification detail carries an optional V1 email delivery record for authorized commercial operators.
It includes state, attempts, exact times and an optional local capture link.
For suppressed reminders it can include a `suppressionReasonCode` from the V1 vocabulary.
