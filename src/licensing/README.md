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

The API never migrates. The `licensing-migrate` image
(`src/licensing/Dockerfile.migrate`) is an EF migration bundle for
`LicensingDbContext`; it reads `LicensingMigrationDatabase__Host`, `__Port`,
`__Database`, `__Username` and `__Password` (all but `Port` required) and runs
as the identity that owns the licensing database. Provision the database and
roles before it runs and grant the runtime login afterwards.

The first migration is `InitialAdminIdentityAndAudit`. Run the explicit
migration from the repository root with:

```sh
dotnet ef database update \
  --project src/licensing/Intelibill.Licensing.Infrastructure \
  --startup-project src/licensing/Intelibill.Licensing.Api \
  --context LicensingDbContext
```

Locally, `docker compose --profile full up -d --build` does all of this:
`db-bootstrap` prepares the database and roles, `licensing-migrate` runs the
bundle, `licensing-runtime-role` grants the runtime login and checks it holds
no schema or role authority, and only then does `licensing-api` start. See
[docs/licensing-rollout.md](../../docs/licensing-rollout.md).

## Security primitives

Both APIs register the bcrypt password hasher and opaque token primitives from
`Intelibill.Shared.Security`. Opaque token creation returns the V1 carrier
from `Intelibill.Shared.Contracts`. Each application owns credential storage,
token issuance policy, sessions and authorization. Fixture-based regression tests verify
merchant and licensing authority independently.

## First provider owner

After the explicit migration, an operator with the separate migrator login can
attest an independently verified email and prepare one 30-minute setup proof.
Use a non-secret attestation reference. The command emits the proof once on
stdout, so capture it in a protected file and deliver it out of band.

```sh
umask 077
dotnet run --project src/licensing/Intelibill.Licensing.Api -- \
  bootstrap-admin prepare --verified-email "$VERIFIED_EMAIL" \
  --verification-reference "$ATTESTATION_REFERENCE" --admin-origin "$ADMIN_ORIGIN" \
  > "$PROTECTED_PROOF_FILE"
```

The admin origin must be HTTPS. The local fixture permits HTTP loopback in
Development or Testing. Run the command with `DOTNET_ENVIRONMENT=Development`
for a loopback fixture. Terminate HTTPS at a trusted ingress, preserve the
external Host header through the admin proxy, and keep the API upstream private.
For multiple API replicas, configure a shared, access-controlled ASP.NET Core
Data Protection key ring for the bootstrap CSRF tokens.

Open `/auth/bootstrap` on the prepared admin origin and enter the proof and a
chosen password. A successful request permanently consumes the proof and creates
one verified owner awaiting MFA. Setup uses a separate 15-minute enrollment
cookie. If it is lost or expires, the initial owner can resume at
`/auth/bootstrap/enrollment` using the verified email and chosen password while
MFA enrollment remains pending. Resuming revokes the preceding enrollment
credential. Both credentials are stored only as digests and the password is
stored as a bcrypt hash.

The enrollment cookie grants pending enrollment access through
`BootstrapEnrollment`. Provider business endpoints use
`ProviderBusinessAccess` and require independent business authentication.
P01-06 ends at pending enrollment.
