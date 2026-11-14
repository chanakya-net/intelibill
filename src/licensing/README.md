# InteliBill Licensing API

Build the licensing solution from the repository root with
`dotnet build src/licensing/Intelibill.Licensing.slnx -warnaserror`.
The root `Intelibill.slnx` builds merchant and licensing projects together.

## Runtime configuration

Provision a separate PostgreSQL 17 database and a dedicated login for the API.
Supply `LicensingDatabase__Host`, `LicensingDatabase__Database`,
`LicensingDatabase__Username` and `LicensingDatabase__Password`. Optional
settings under the same prefix include `Port` (5432), `MaxPoolSize` (12)
and `TimeoutSeconds` (5 for connection acquisition and 5 for commands by
default). Run the API with `dotnet run --project src/licensing/Intelibill.Licensing.Api`.

Revoke public connection and temporary-object privileges on the licensing
database and grant `CONNECT` to its own roles. The runtime login needs schema
`USAGE` and grants for the data it uses. Keep ownership and DDL privileges
with the migrator. Keep merchant access and licensing access in separate
roles and restrict each role to its own service database. Use explicit data
grants for the runtime login and reserve PostgreSQL predefined roles for
administrative connections.

| Anonymous probe | Response |
| --- | --- |
| `GET /health/live` | HTTP 200 with `{"service":"intelibill-licensing","status":"live"}` |
| `GET /health/ready` | HTTP 200 with `{"service":"intelibill-licensing","status":"ready"}` when the licensing database is reachable under a restricted role |
| `GET /health/ready` when the check fails | HTTP 503 with `{"service":"intelibill-licensing","status":"unavailable"}` |

Both probes send `Cache-Control: no-store`. Readiness checks the runtime
connection and its role privileges. API startup works during a licensing
database outage when settings are valid.

## Errors and logging

Unhandled failures answer the same `application/problem+json`
`{type,title,status,code,traceId}` envelope as the merchant API
(`Internal.Unexpected` for anything unplanned), from
`src/shared/backend/Intelibill.Shared.Web`. Every log sink sits behind the
shared redactor and spans lose exception text before export. Optional
`Observability__Logging__MinimumLevel` (Information),
`Observability__Logging__EnablePiiMasking` (true) and
`Observability__Logging__AsyncBufferSize` (10000) tune logging.

## Migrations

Provision the database and roles before running the migration job. Set the
corresponding `LicensingMigrationDatabase__*` environment variables on the
job and run the EF command from the repository root once schema migrations
exist.

```sh
dotnet ef database update \
  --project src/licensing/Intelibill.Licensing.Infrastructure \
  --startup-project src/licensing/Intelibill.Licensing.Api \
  --context LicensingDbContext
```

The initial EF model is empty. The API uses its runtime settings for both EF
and readiness. The migration job owns all schema changes. Issue #835 supplies
Compose orchestration, provisioning, migration and role-grant jobs.

## Security primitives

Both APIs register the bcrypt password hasher and opaque token primitives from
`Intelibill.Shared.Security`. Opaque token creation returns the V1 carrier
from `Intelibill.Shared.Contracts`. Each application owns credential storage,
token issuance policy, sessions and authorization. The licensing API currently
exposes its health probes. Fixture-based regression tests verify that merchant
authentication retains its existing behavior and refuses credentials from a
separate licensing issuer.
