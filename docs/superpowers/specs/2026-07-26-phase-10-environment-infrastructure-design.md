# Phase 10 Environment Infrastructure Design

**Date:** 2026-07-26

**Status:** Implemented and verified

**Branch:** `infra-setup`

## Purpose

Provision the Azure environment infrastructure needed to run the Intelibill API,
web proxy, and database migration bundle in both `dev` and `prod`. Phase 10 must
preserve the identities, database grants, Key Vault keys, and state ownership
already applied in Phases 1–9.

The deployment keeps the approved low-cost public-network launch exception:
PostgreSQL stays on its public endpoint, the broad Azure-services firewall rule
stays forbidden, and the server allowlists every outbound IPv4 address currently
advertised by the two Container Apps environments.

## Scope

Phase 10 creates and applies:

- one shared Log Analytics workspace;
- one Container Apps environment each for `dev` and `prod`;
- one API Container App, web Container App, and manual migration job per
  environment;
- Container Apps, Key Vault, and PostgreSQL diagnostic settings;
- PostgreSQL firewall rules derived from the Container Apps outbound address
  sets;
- resource-scoped deploy role assignments for each environment;
- an executable drift check for Container Apps outbound addresses versus
  PostgreSQL firewall rules;
- outputs needed by the deployment pipeline and later domain work.

Phase 10 does not create application integration credentials, deploy real
application images, run database migrations, configure custom domains, create
the keep-warm schedule, add a SignalR backplane, or raise the API replica cap.
Those remain later-phase work.

## Constraints Carried Forward

- Existing workload identities must be reused, never replaced. PostgreSQL
  principals are registered against their current object IDs.
- PostgreSQL remains one shared B1ms server with `intelibill_dev` and
  `intelibill_prod`.
- Runtime identities have DML only; migrator identities retain the schema
  privileges established in Phase 7.
- API replicas remain capped at one because rate limiting is non-atomic and
  SignalR has no backplane.
- The web container is the only public browser origin. It proxies `/api` and
  `/hubs` to the internal API.
- Images are pulled anonymously from public registries. There is no registry
  block and no `AcrPull` assignment.
- OpenTofu must never read a Key Vault secret value. Secret references use
  versionless URIs constructed from the known vault URI and secret name.
- Deploy pipelines own workload images. OpenTofu ignores image drift for the
  API, web app, and migration job.
- DNS remains deferred. Generated `azurecontainerapps.io` hostnames are the
  active origins.

## Architecture and State Ownership

### Shared state

`.tofu/envs/shared` continues to own PostgreSQL and gains:

- `intelibill-logs`, a shared Log Analytics workspace;
- PostgreSQL diagnostic settings;
- remote-state reads of the `dev` and `prod` environment outputs;
- one PostgreSQL firewall rule per advertised Container Apps outbound address.

Database firewall resources stay with the database-owning state. Environment
states publish addresses but never manage the shared server directly. This
avoids three OpenTofu states competing to own firewall rules on one resource.

The workspace is shared because the database is shared, there is one resource
group, and the selected design optimizes for launch cost and operational
simplicity. Every Container Apps log record carries its environment and app
name, so dev/prod remain queryable independently.

The workspace uses:

- region `centralindia`;
- SKU `PerGB2018`;
- 30-day retention;
- a configurable daily safety cap defaulting to `0.1` GB;
- local authentication disabled.

The cap is a budget fuse, not a normal volume-control mechanism. Reaching it
creates a monitoring gap, so the drift checker and verification commands must
not depend exclusively on ingested logs.

### Environment states

`.tofu/envs/dev` and `.tofu/envs/prod` retain ownership of their current
workload identities and Key Vaults. Each gains one call to a focused
environment-infrastructure module that owns:

- a workload-profile v2 Container Apps environment using its built-in
  `Consumption` profile;
- Azure Monitor diagnostic routing to the shared workspace;
- the API and web Container Apps;
- the manual migration job;
- resource-scoped deploy role assignments;
- environment workload outputs.

The module receives the existing identity objects from
`module.workload_identities`. It never looks identities up by a copied GUID and
never declares replacement identity resources.

The root discovers the existing `id-gha-deploy-<env>` identity and the
subscription-scoped `Intelibill Container App Deployer` role by deterministic
name. This avoids adding bootstrap-state credentials or copied principal IDs to
environment tfvars.

## Workload Design

### Bootstrap images

The API and web resources initially use version 31 of
`ghcr.io/mendhak/http-https-echo`, pinned to its multi-architecture index
digest. It runs as a non-root user, accepts configurable `HTTP_PORT` values, and
returns success for arbitrary paths, so the final ingress ports and health
probes can be correct before the application images exist in GHCR.

The migration job uses the same immutable bootstrap image but has a manual
trigger and is not executed during Phase 10. This deliberately prevents a
placeholder execution from being mistaken for a successful schema migration.
Phase 11 replaces the image with the migration-bundle image before starting the
job.

Every bootstrap image variable must match an `@sha256:<64 lowercase hex>`
reference. Tags and platform-specific manifest digests are rejected.

### API Container App

- Name: `intelibill-<env>-api`
- Revision mode: `Single`
- Identity: existing `id-app-<env>`
- Ingress: internal only, HTTPS, `transport = "auto"`, target port `8080`
- Scale: minimum `0`, maximum `1`, HTTP concurrency `50`
- Resources:
  - dev: `0.25` vCPU, `0.5Gi`
  - prod: `0.5` vCPU, `1Gi`
- Session affinity: disabled; one replica makes it unnecessary

Probes:

- startup: `GET /health/live`, port `8080`, with a longer failure budget;
- liveness: `GET /health/live`, port `8080`;
- readiness: `GET /health/ready`, port `8080`.

The liveness probe never checks PostgreSQL. The readiness probe does, so a
database or Entra-token failure removes the replica from service without
turning the outage into a restart loop.

The API receives:

```text
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_HTTP_PORTS=8080
AZURE_CLIENT_ID=<runtime identity client ID>
Database__Host=intelibill-pg-01.postgres.database.azure.com
Database__Port=5432
Database__Database=intelibill_dev | intelibill_prod
Database__Username=id-app-dev | id-app-prod
Database__UseEntraAuth=true
Database__MaxPoolSize=12
Jwt__SigningMode=KeyVault
Jwt__KeyVaultKeyId=<environment versionless jwt-signing key ID>
Jwt__Issuer=Intelibill-<env>
Jwt__Audience=Intelibill-<env>
App__BaseUrl=https://<web generated FQDN>
Proxy__Enabled=true
Proxy__ForwardLimit=2
Proxy__TrustAnyProxy=true
Observability__NewRelic__OtlpEndpoint=https://otlp.nr-data.net:4318
Observability__NewRelic__ServiceName=Intelibill.Api
Observability__NewRelic__Environment=<env>
```

`Database__Password`, `Jwt__Secret`, and CORS origins are absent.

The New Relic API key is optional infrastructure input during Phase 10 because
no integration secret exists yet. When the out-of-band secret
`new-relic-api-key` exists, setting its secret-name variable adds a versionless
Key Vault reference and maps it to
`Observability__NewRelic__ApiKey`. Deploying the real API image is blocked
operationally until that secret has been configured. A non-null input must be
one Azure Key Vault secret-name segment: 1–127 ASCII letters, digits, or
hyphens. Paths and version suffixes are rejected so URI construction remains
versionless.

### Web Container App

- Name: `intelibill-<env>-web`
- Revision mode: `Single`
- No managed identity
- Ingress: external, HTTPS, `transport = "auto"`, target port `4000`
- Scale: minimum `0`, maximum `1`, HTTP concurrency `50`
- Resources:
  - dev: `0.25` vCPU, `0.5Gi`
  - prod: `0.5` vCPU, `1Gi`

Startup, liveness, and readiness all probe `GET /` on port `4000`, because the
web server has no separate health route.

The web container receives:

```text
PORT=4000
API_ORIGIN=https://intelibill-<env>-api.internal.<environment default domain>
NODE_ENV=production
```

Using the internal FQDN keeps API traffic inside the Container Apps environment
and avoids a Terraform dependency cycle between the API and web resources.

### Migration Job

- Name: `intelibill-<env>-migrate`
- Identity: existing `id-migrator-<env>`
- Trigger: manual
- Parallelism: `1`
- Completion count: `1`
- Retry limit: `0`
- Replica timeout: `1800` seconds
- Resources: `0.5` vCPU, `1Gi`

The job receives the same database host, port, database, Entra-auth, and pool
settings as the API, except:

```text
AZURE_CLIENT_ID=<migrator identity client ID>
Database__Username=id-migrator-dev | id-migrator-prod
```

It receives no JWT or integration configuration. Phase 11 must replace the
bootstrap image with the migration-bundle image, start the job, wait for a
successful execution, and only then update the application images.

## Diagnostics

Container Apps environments use `logs_destination = "azure-monitor"` and an
Azure Monitor diagnostic setting targeting the shared workspace. The setting
exports:

- `ContainerAppConsoleLogs`;
- `ContainerAppSystemLogs`;
- `ContainerAppHTTPLogs`;
- `AllMetrics`.

HTTP logs are intentionally enabled because they expose ingress status,
latency, and WebSocket disconnect behavior without application changes.

PostgreSQL exports `PostgreSQLLogs` and `AllMetrics`. Query Store, table,
session, transaction, and PgBouncer diagnostic categories remain disabled
until a measured troubleshooting need justifies their additional ingestion.

Each Key Vault exports `AuditEvent` and `AllMetrics`. Policy evaluation details
remain disabled.

## Public Egress Allowlist and Drift Control

Azure does not guarantee stable outbound addresses for a Container Apps
environment without controlled egress. The selected launch exception manages
that risk as follows:

1. Each environment output publishes the set union of the API and web
   `outbound_ip_addresses`. The migration job shares the same environment
   egress pool.
2. Shared state reads both outputs and creates a PostgreSQL firewall rule for
   each IPv4 address with identical start and end values.
3. Rule names include the environment and sanitized address, so list ordering
   does not create artificial replacement churn.
4. Existing explicit `allowed_ip_rules` remain mergeable for a short-lived
   operator address, but validation continues to reject `0.0.0.0` and
   `255.255.255.255`.
5. Shared input `retained_container_apps_outbound_ips`, keyed by environment,
   can temporarily retain the previous exact addresses during a staged
   transition. It defaults to empty and accepts only individual IPv4
   addresses.
6. A drift-check script compares Azure's current advertised address union with
   the managed PostgreSQL rules. Every selected environment must advertise at
   least one canonical IPv4 address. The script exits nonzero for an empty
   selected environment, missing addresses, broad rules, or malformed ranges.
7. Outside a retained-address transition, every saved shared plan is passed to
   `guard-shared-egress-plan.sh` immediately before apply. The guard extracts
   the plan's final PostgreSQL firewall inventory from `tofu show -json` and
   gives it to the drift checker through `--firewall-file`. The checker then
   requires that final inventory to exactly match both live environment
   address sets, so a plan cannot silently remove one environment's allowlist.
   The normal guard deliberately accepts no environment narrowing, retained
   file, or advertised fixture override.
8. Every infrastructure plan and live verification runs the drift check.
   Phase 11 schedules it through the plan workflow so provider-side changes are
   reported even when no repository change occurs.
9. When addresses change, always recover old from two pre-refresh sources: the
   affected environment's persisted output and its exact environment-prefixed
   single-IP firewall rules. Require those sources to be equal, nonempty, and
   canonical. In the planned branch, require current live to equal old before
   the reviewed environment operation, then discover a distinct new set. In
   the unannounced branch, require current live to differ from old and use that
   single captured current set as new without overwriting or rediscovering old.
   Apply a saved `-refresh-only` plan for only the affected environment when
   needed so its new computed output is persisted before shared state plans.
   Derive both
   `retained_container_apps_outbound_ips` and the checker's `--retained-file`
   JSON from the same old set. Machine-guard the first shared saved plan to
   create exactly `new - old` and delete nothing; after apply, require the
   retained-aware checker plus a successful real readiness check. The
   readiness gate must first wake the scale-to-zero public web app, poll with a
   bounded deadline for a running explicit revision/replica, and then require a
   bounded internal `/health/ready` HTTP 200 from that replica. Explicitly clear
   retention, machine-guard the second shared saved plan to delete exactly
   `old - new`, apply, and run the bare combined checker. This avoids exchanging
   old and new rules in one propagation window.

The broad Azure-services rule remains prohibited. Entra authentication and
database grants remain independent controls; firewall reachability alone grants
no database access.

## Deploy Permission Narrowing

Each environment state creates three assignments of the existing custom
deployer role:

- dev deploy identity to dev API, dev web, and dev migration job;
- prod deploy identity to prod API, prod web, and prod migration job.

After all six assignments exist, the bootstrap configuration removes the two
resource-group-scoped deploy assignments and is reapplied. Verification must
show only resource scopes ending in the correct environment's
`containerApps/...` or `jobs/...`.

The custom role gains `Microsoft.App/jobs/write`. Its current definition can
start and inspect jobs but cannot replace the migration job image, which Phase
11 must do before execution. The role continues to withhold delete, secret
listing, and managed-identity assignment permissions.

The infrastructure apply identity remains group-scoped as already accepted by
decision §19. Phase 10 narrows only routine deployment.

## Apply Order

The initial live application is deliberately staged:

1. Record the current dev/prod workload identity object IDs.
2. Register `Microsoft.App` and wait for `Registered`.
3. Apply shared state to create Log Analytics and PostgreSQL diagnostics.
4. Apply dev state to create its environment, apps, job, diagnostics, and
   scoped deploy roles.
5. Re-plan and apply shared state to add dev outbound firewall rules.
6. Run the dev allowlist drift check.
7. Apply prod state.
8. Re-plan and apply shared state to add prod outbound firewall rules.
9. Run the combined allowlist drift check.
10. Reapply bootstrap to remove group-scoped deploy assignments.
11. Verify identity object IDs are unchanged and deployment scopes are narrow.

Every saved plan is applied directly. Apply output is never piped through a
command that can hide its exit code.

After the one-time staged bootstrap above, every ordinary shared apply uses a
combined final-inventory guard:

```bash
repository_root="$(pwd -P)"
shared_plan="${repository_root}/.tofu/envs/shared/shared.tfplan"

tofu -chdir=.tofu/envs/shared plan -out="${shared_plan}"
tofu -chdir=.tofu/envs/shared show "${shared_plan}"
.tofu/scripts/guard-shared-egress-plan.sh --plan-file "${shared_plan}"
tofu -chdir=.tofu/envs/shared apply "${shared_plan}"
.tofu/scripts/check-container-app-egress.sh
```

Run the guard immediately before apply. It checks both live environments and
the saved plan's final firewall values without changing Azure or state. The
retained-address procedure remains the only exception because its first and
second shared plans intentionally contain transitional inventories; those
plans use their stricter create-only and delete-only machine guards.

## Failure Handling

- Provider registration failure stops before any OpenTofu apply.
- An empty or unknown outbound address set stops firewall reconciliation; it
  never falls back to the Azure-services rule.
- A missing integration secret leaves the optional reference disabled. It is
  never replaced with a plaintext or placeholder secret in state.
- Key Vault RBAC remains owned by the existing module; Phase 10 adds no access
  policy and no duplicate runtime role.
- A failed migration job does not deploy application images. Retry remains
  explicit after inspection because the job's retry limit is zero.
- A database outage fails readiness, not liveness.
- A failed production apply leaves dev independently usable and leaves the
  broad deploy grant in place only until resource-scoped grants have been
  verified and bootstrap is safely reapplied.

## Testing and Verification

Implementation begins with OpenTofu module contract tests using mocked AzureRM
resources. Tests assert:

- exact resource names and environment separation;
- existing identity IDs are attached to the correct workload;
- required ports, probes, resource sizes, and replica caps;
- manual migration trigger and migrator username/client ID;
- API/web origin relationship and proxy forward limit `2`;
- absence of database passwords, HMAC JWT secrets, CORS origins, registry
  blocks, and `AcrPull`;
- digest-only bootstrap image validation;
- versionless Key Vault secret URI construction;
- Azure-compatible single-segment Key Vault secret-name validation;
- diagnostic categories and shared workspace routing;
- resource-scoped deploy assignments;
- narrow firewall rule construction from remote output sets.

Repository verification then runs:

```bash
tofu fmt -check -recursive .tofu
tofu -chdir=.tofu/bootstrap validate
tofu -chdir=.tofu/envs/shared validate
tofu -chdir=.tofu/envs/dev validate
tofu -chdir=.tofu/envs/prod validate
tofu -chdir=.tofu/modules/environment-infrastructure test
tofu -chdir=.tofu/envs/shared test
bash .tofu/scripts/tests/check-container-app-egress.test.sh
bash .tofu/scripts/tests/guard-shared-egress-plan.test.sh
```

Fresh saved plans for shared, dev, prod, and bootstrap must show no identity or
Key Vault replacement. Live verification checks:

- both Container Apps environments are provisioned;
- API ingress is internal and web ingress is external;
- workload images remain the pinned bootstrap images after a second plan;
- every advertised outbound IP has an exact PostgreSQL rule;
- no broad PostgreSQL firewall rule exists;
- deploy identities have only their three environment-specific resource
  scopes;
- workload identity object IDs match the values recorded before Phase 10;
- Log Analytics receives Container Apps system/console/HTTP records plus
  PostgreSQL and Key Vault diagnostics after propagation.

Application build and unit-test suites run after infrastructure verification to
confirm that no shared configuration or source assumptions were disturbed.

## Acceptance Criteria

Phase 10 is complete when:

- all declared resources exist in Azure and are state-owned;
- dev and prod workload identities are unchanged;
- both environments expose only their intended ingress;
- PostgreSQL accepts traffic only from the full advertised Container Apps
  egress union and any explicitly reviewed temporary operator rule;
- the drift checker passes;
- API, web, and migration resources hold the complete Phase 10 runtime
  contract;
- all deploy assignments are resource-scoped and both group-scoped assignments
  are absent;
- formatting, validation, contract tests, saved plans, and live verification
  pass with no unexplained drift.

## Implementation Verification

Phase 10 was applied on 2026-07-27 and independently reviewed at implementation
head `e8c11915`. Fresh post-apply verification established:

- both API ingresses are internal, both web ingresses are external, every
  generated FQDN is present, and all four apps remain capped at one replica;
- both migration jobs are manual, carry the intended migrator identities, and
  have zero executions;
- the derived live egress snapshots contain 181 addresses per environment
  (dev SHA-256
  `25cdaa25bcb51b120eca6563def49c030d3181dda4f437ca3e9df1f98a856648`,
  prod SHA-256
  `c400ecdfeaf34ce0959a1cdb194f577af37969594b64dff614727ae7b8cd0395`);
- all 362 managed PostgreSQL rules are exact single-IP rules, no broad rule
  exists, and the combined drift checker reports 362 expected and 362 managed;
- all six deploy assignments bind the correct environment deploy principal to
  its API, web, and migration-job resource, with no deploy assignment at
  resource-group scope;
- all four workload identity principal IDs match their pre-Phase-10 values,
  and the optional New Relic secret reference remains disabled;
- shared, dev, prod, and bootstrap idempotence plans all exit 0 with no
  changes;
- the exact diagnostic settings are active, and Log Analytics contains recent
  dev/prod Container Apps console, system, and HTTP evidence, PostgreSQL
  log/metric evidence, and dev/prod Key Vault audit/metric evidence. HTTP
  evidence arrived after about ten minutes of ingestion lag.

The address fingerprints are a derived, non-canonical snapshot; the live Azure
sets and `.tofu/scripts/check-container-app-egress.sh` remain authoritative for
operations. Future address changes must use the exact runbook in
`docs/phase-10-handoff.md`: validate persisted output and exact
environment-prefixed firewall rules as the same old set, then enter either the
planned branch (`current == old`, reviewed operation, discover distinct new) or
the unannounced branch (`current != old`, current becomes new once). Review and
apply the affected environment's saved `-refresh-only` plan when state has not
already persisted new, and build both shared tfvars and checker
`--retained-file` JSON from old. The first shared saved plan is machine-guarded
to create only `new - old` exact rules and delete none. After firewall
propagation, a retained-aware drift check and the Phase 11+ gate must wake the
public web app, find a running explicit revision/replica within a bounded
deadline, and obtain an internal `/health/ready` HTTP 200. The second saved plan
explicitly clears retention and is machine-guarded to delete only `old - new`
exact rules. A bare combined checker must pass after that second apply.
