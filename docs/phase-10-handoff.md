# Phase 10 handoff — environment infrastructure

**Updated 2026-07-27 on branch `infra-setup`.** Phase 10 was applied and
independently reviewed at implementation head `e8c11915`; the documentation
commit that records this evidence necessarily follows that head. Everything
below was verified against the live subscription, not recalled from the
runbook. Where this document and
[infrastructure-implementation-guide.md](infrastructure-implementation-guide.md)
disagree, this document is newer.

Read first: [infrastructure-decisions.md](infrastructure-decisions.md) §7, §8, §18, §19, §20, §21 — those are the constraints you cannot design around without reopening a decision.

---

## 1. Where the work stands

| Phase | State |
|---|---|
| 1–4 bootstrap, state, OIDC identities, GitHub environments | done |
| 5 shared infrastructure (PostgreSQL, no DNS) | done |
| 6 DNS delegation | **⏸ deferred to ~2026-08-25.** Everything runs on `*.azurecontainerapps.io` |
| 7 database bootstrap (principals, grants, isolation) | done and verified |
| 8 application production contract | done **except 8.4 telemetry instruments** |
| 9 Key Vault | vaults and signing keys applied; integration secrets outstanding |
| **10 environment infrastructure** | **done, applied, and independently verified 2026-07-27** |
| 11 pipelines | not started |
| 12 domains | ⏸ blocked by phase 6 |
| 13–14 keep-warm, verification | not started |

---

## 2. What exists in Azure right now

Subscription `cef6a1af-9d98-437f-b99c-ad6d24e5631c`, tenant `e5208e76-dd12-47f0-9541-c9b45afaffe6`, one resource group **`intelibill-shared`**, region **`centralindia`**.

> `southindia` and `eastus` are both offer-restricted for this subscription for PostgreSQL. Check with the subscription-scoped capabilities API, not `list-skus`, which reads a catalog rather than your entitlement.

```
intelibill-pg-01      PostgreSQL 17 Flexible, Standard_B1ms, 32 GB, public access,
                      14-day backups, zone 1
                      databases: intelibill_dev, intelibill_prod
intelibill-dev-kv     RBAC, soft delete 7 days, purge protection OFF
intelibill-prod-kv    RBAC, soft delete 90 days, purge protection ON (irreversible)
                      both hold key `jwt-signing` (RSA 2048, sign/verify, rotation policy)
intelibilltfstate01   OpenTofu state, containers tfstate-{shared,dev,prod}
intelibill-logs       Log Analytics, PerGB2018, 30-day retention, 0.1 GB/day cap
intelibill-dev-env    Container Apps environment (Consumption)
  intelibill-dev-api  internal ingress; generated internal FQDN
  intelibill-dev-web  external ingress; generated public FQDN
  intelibill-dev-migrate
                      manual migration job; never executed
intelibill-prod-env   Container Apps environment (Consumption)
  intelibill-prod-api internal ingress; generated internal FQDN
  intelibill-prod-web external ingress; generated public FQDN
  intelibill-prod-migrate
                      manual migration job; never executed
```

### Workload identities (attached without replacement)

| Environment | Role | Name | Client ID | Principal ID |
|---|---|---|---|---|
| dev | app | `id-app-dev` | `7c33ca76-6977-4e45-9c42-fda8cd5b2aab` | `639aa307-3394-4da0-a5bf-bcecf7a36632` |
| dev | migrator | `id-migrator-dev` | `51dece82-a93c-466b-8a16-6eaca361db28` | `d4463264-a136-467a-af6d-e174d99dab26` |
| prod | app | `id-app-prod` | `a5f8f605-3069-4a73-afdd-8972eb847602` | `122b2c9e-7ec7-4b84-9d80-5267092eb0a7` |
| prod | migrator | `id-migrator-prod` | `b5c2de5b-0852-44fc-8a36-4a7404c479bd` | `051aad50-f111-4c7d-8ba6-75630cba1b64` |

Read them from `tofu -chdir=.tofu/envs/<env> output -json identities` rather than copying — but **do not recreate them**. The PostgreSQL principals from Phase 7 are named after these identities and registered by object ID; a replacement identity gets a new object ID and every grant silently stops matching.

### GitHub-facing identities (bootstrap layer)

`plan`, `infra_apply`, `deploy_dev`, `deploy_prod`. Environments `dev`, `prod`, `shared` exist, each carrying `AZURE_CLIENT_ID_INFRA`, `AZURE_SUBSCRIPTION_ID`, `AZURE_TENANT_ID`; `dev` and `prod` also carry `AZURE_CLIENT_ID_DEPLOY`.

### Database grants already in place (Phase 7)

Per environment: `CONNECT` for its own app and migrator only, `REVOKE CONNECT/TEMPORARY … FROM PUBLIC`, schema `USAGE` for the app, `CREATE` for the migrator, and `ALTER DEFAULT PRIVILEGES` so the app gets DML on whatever the migrator creates later. The cross-environment `CONNECT` matrix returns `t,t,f,f` — **those two false results are the entire dev/prod data boundary**, since both databases live on one server.

---

## 3. Phase 10 completion evidence

### Live workload behavior

| Environment | API | Web | Migration job |
|---|---|---|---|
| dev | `intelibill-dev-api`; internal; `intelibill-dev-api.internal.jollypond-9e71a2fb.centralindia.azurecontainerapps.io` | `intelibill-dev-web`; external; `intelibill-dev-web.jollypond-9e71a2fb.centralindia.azurecontainerapps.io` | `intelibill-dev-migrate`; manual; migrator principal `d4463264-a136-467a-af6d-e174d99dab26`; 0 executions |
| prod | `intelibill-prod-api`; internal; `intelibill-prod-api.internal.politebush-ac4f5ec3.centralindia.azurecontainerapps.io` | `intelibill-prod-web`; external; `intelibill-prod-web.politebush-ac4f5ec3.centralindia.azurecontainerapps.io` | `intelibill-prod-migrate`; manual; migrator principal `051aad50-f111-4c7d-8ba6-75630cba1b64`; 0 executions |

API and web resources are configured for minimum 0 and maximum 1 replica.
Azure's live representation omits `minReplicas` when it is the zero default;
all four live resources explicitly reported `maxReplicas = 1`. The API remains
internal and the web app remains the only public browser origin.

Both environments still use the immutable bootstrap image. Phase 11 must
replace the migration-job image before starting it, wait for a successful
execution, and only then deploy the real API and web images. Phase 10 did not
start either job.

### Public-network exception and current derived address snapshot

The public PostgreSQL exception remains intentionally narrow. Live verification
found 362 managed firewall rules: 181 named `container-apps-dev-*` and 181
named `container-apps-prod-*`. Every rule has identical start and end IPv4
addresses; there are zero broad, ranged, operator, or otherwise unmanaged
rules. In particular, the Azure-services `0.0.0.0` exception is absent.

The following snapshot is **derived and non-canonical**. Azure may change
Container Apps outbound addresses; OpenTofu state and this document are not a
source of truth for future reachability.

| Environment | Derived unique advertised addresses | SHA-256 of sorted addresses, one address plus newline per record |
|---|---:|---|
| dev | 181 | `25cdaa25bcb51b120eca6563def49c030d3181dda4f437ca3e9df1f98a856648` |
| prod | 181 | `c400ecdfeaf34ce0959a1cdb194f577af37969594b64dff614727ae7b8cd0395` |

Retrieve and fingerprint the live API, web, and job union:

```bash
for environment_name in dev prod; do
  api_addresses="$(
    az containerapp show --resource-group intelibill-shared \
      --name "intelibill-${environment_name}-api" \
      --query properties.outboundIpAddresses -o json
  )"
  web_addresses="$(
    az containerapp show --resource-group intelibill-shared \
      --name "intelibill-${environment_name}-web" \
      --query properties.outboundIpAddresses -o json
  )"
  job_addresses="$(
    az containerapp job show --resource-group intelibill-shared \
      --name "intelibill-${environment_name}-migrate" \
      --query properties.outboundIpAddresses -o json
  )"
  sorted_addresses="$(
    jq -n \
      --argjson api "${api_addresses}" \
      --argjson web "${web_addresses}" \
      --argjson job "${job_addresses}" \
      '[$api[], $web[], $job[]] | unique | sort'
  )"
  printf '%s count=%s sha256=%s\n' \
    "${environment_name}" \
    "$(jq 'length' <<<"${sorted_addresses}")" \
    "$(jq -r '.[]' <<<"${sorted_addresses}" | shasum -a 256 | awk '{print $1}')"
done

.tofu/scripts/check-container-app-egress.sh
```

The final checker output was:

```text
Egress allowlist verified: 362 expected address(es), 362 managed rule(s).
```

For any future address change, use the retained-address two-apply procedure:

1. Put the previous exact addresses for the affected environment in shared
   input `retained_container_apps_outbound_ips`. Review and apply shared state
   so newly advertised rules are added while old rules remain. Wait for
   firewall propagation, a successful readiness check, and a green drift
   check.
2. Clear the retained set. Review a second shared plan whose only removals are
   stale exact rules, apply it, and rerun the drift checker.

Never swap old and new rules in one propagation window, summarize addresses
into a range, or fall back to the broad Azure-services exception.

### Deploy scopes and identity preservation

The custom `Intelibill Container App Deployer` role has exactly these six
routine deployment assignments:

| Deploy principal | Exact resource scopes |
|---|---|
| dev `4475d63e-2970-455f-935d-f4de25a0d7d4` | `.../Microsoft.App/containerApps/intelibill-dev-api`; `.../Microsoft.App/containerApps/intelibill-dev-web`; `.../Microsoft.App/jobs/intelibill-dev-migrate` |
| prod `be616680-7dd9-450a-85e7-cf52f28e05a4` | `.../Microsoft.App/containerApps/intelibill-prod-api`; `.../Microsoft.App/containerApps/intelibill-prod-web`; `.../Microsoft.App/jobs/intelibill-prod-migrate` |

There is no deploy assignment whose scope equals resource group
`intelibill-shared`. The four app/migrator principal IDs in the identity table
above match the pre-Phase-10 baseline exactly.

The optional `new-relic-api-key` reference remains disabled in both
environments (`observability_secret_configured = false`). No integration
secret value was read or persisted.

### Idempotence and monitoring

Fresh `tofu plan -detailed-exitcode` runs for shared, dev, prod, and bootstrap
all exited 0 with `No changes`. Diagnostic settings route exactly:

- Container Apps: `ContainerAppConsoleLogs`, `ContainerAppSystemLogs`,
  `ContainerAppHTTPLogs`, and `AllMetrics`;
- PostgreSQL: `PostgreSQLLogs` and `AllMetrics`;
- each Key Vault: `AuditEvent` and `AllMetrics`.

After read-only public web and Key Vault metadata probes, the shared workspace
contained recent console, system, and HTTP records for both Container Apps
environments; PostgreSQL log and metric records; and audit and metric records
for both Key Vaults. HTTP records arrived after about ten minutes of ingestion
lag. Both migration-job execution counts were queried again afterward and
remained zero.

### Separate repository-tooling limitation

Graphify AST extraction succeeds, but HTML visualization generation exceeds
the size limit for the 14,305-node graph. This is a repository visualization
limitation, not an Azure, OpenTofu, drift, idempotence, or logging failure.

---

## 4. The application contract Phase 10 must satisfy

This is the part the guide's Phase 10 examples predate. All of it is verified working locally.

### API container

| Setting | Value | Why |
|---|---|---|
| Port | `8080` | `ASPNETCORE_HTTP_PORTS=8080` is baked into the image; `EXPOSE 8080` |
| User | non-root `app` (uid 1654) | shipped by the .NET base image |
| Liveness probe | `GET /health/live` | must **not** include the database — a DB outage failing liveness restarts every replica and turns an outage into a crash loop |
| Readiness probe | `GET /health/ready` | runs `SELECT 1` through the app's own `NpgsqlDataSource`, so under Entra it also proves the identity can still get a token |
| Startup probe | `/health/live` | same endpoint, longer failure budget |

Both health paths are excluded from HTTPS redirection in the app, so plain-HTTP probes answer 200 rather than 307.

Environment variables the API needs:

```
ASPNETCORE_ENVIRONMENT      Production
ASPNETCORE_HTTP_PORTS       8080
AZURE_CLIENT_ID             <app identity client id>   # DefaultAzureCredential
Database__Host              intelibill-pg-01.postgres.database.azure.com
Database__Port              5432
Database__Database          intelibill_dev | intelibill_prod
Database__Username          id-app-dev | id-app-prod    # MUST equal the identity name
Database__UseEntraAuth      true
Database__MaxPoolSize       12                          # per replica; B1ms is small
# Database__Password        MUST BE ABSENT — startup fails if set alongside UseEntraAuth
Jwt__SigningMode            KeyVault
Jwt__KeyVaultKeyId          https://intelibill-<env>-kv.vault.azure.net/keys/jwt-signing
# Jwt__Secret               MUST BE ABSENT — same validator, same reason
Jwt__Issuer / Jwt__Audience <per environment>
App__BaseUrl                <web app origin>
Cors__AllowedOrigins__0     (usually none — the web app proxies same-origin)
Proxy__Enabled              true
Proxy__ForwardLimit         2                           # ingress + web proxy, not 1
Proxy__TrustAnyProxy        true                        # ingress IPs are neither stable nor published
Observability__*            endpoint, service name, environment, OTLP key
```

`Database__Username` must be exactly the managed identity's name — that string is what the identity presents when authenticating, and the Phase 7 principal was created under it.

### Web container

```
PORT          4000
API_ORIGIN    https://<api app fqdn>      # the proxy target
NODE_ENV      production
```

Runs as non-root `node` (uid 1000), serves static files, answers deep links with `index.html`, and proxies `/api` and `/hubs` including WebSocket upgrades. **It must run on Node, not Bun** — under Bun the WebSocket upgrade handshake returns nothing and every SignalR connection fails. There is no server-side rendering any more ([decision §20](infrastructure-decisions.md#20-no-server-side-rendering)); the process exists for the proxy, which is what keeps the browser same-origin and the CORS list empty.

There is no `/health` endpoint on the web container. Probe `/` — it returns the app shell.

### Migration job

Application startup no longer migrates. Schema belongs to a job that runs the migrator identity, before the new revision is deployed:

- `AZURE_CLIENT_ID` = migrator identity client id, `Database__Username` = `id-migrator-<env>`, `Database__UseEntraAuth=true`.
- The design-time factory is already Entra-aware, so an `efbundle` authenticates the same way the app does — no connection string can express a rotating token, which is why this works at all.
- Migrations include `20260726120000_AddDistributedCacheTable`, which creates the `cache_entries` table the distributed cache needs. Runtime has `CreateIfNotExists=false` and no `CREATE` on the schema, so **the job must run before the app starts or every cache write fails**.
- Expand/contract only: the previous revision keeps serving while the job runs.

---

## 5. Key Vault references

Vaults are RBAC-authorised. The app identity already holds **Key Vault Secrets User** and **Key Vault Crypto User** on its own environment's vault — granted by the `key-vault` module, so do not add access policies.

Build versionless secret references and let Container Apps resolve them under the runtime identity:

```hcl
key_vault_secret_id = "${module.key_vault.vault_uri}secrets/<name>"
```

Never use a Key Vault secret `data` source: the provider reads `value` and OpenTofu persists data-source attributes in state ([decision §7](infrastructure-decisions.md#7-key-vault-secrets-created-out-of-band)).

The JWT signing key is **not** a secret reference — it is a key the app reaches directly through `Jwt__KeyVaultKeyId`. Nothing about it needs to pass through Container Apps secrets.

---

## 6. Terraform layout and how to run it

```
.tofu/bootstrap/      state storage, GitHub OIDC identities, role assignments
.tofu/envs/shared/    PostgreSQL, DNS (deferred)
.tofu/envs/dev/       workload identities + key vault   ← Phase 10 grows this
.tofu/envs/prod/      same
.tofu/modules/        database, dns, workload-identities, key-vault
```

Each layer has its own state container. `terraform.tfvars` is gitignored; copy from `terraform.tfvars.example` and set `subscription_id`, `location`, and `secret_officer_object_ids` (your own object ID from `az ad signed-in-user show --query id -o tsv`).

```bash
cd .tofu/envs/dev
tofu init
tofu plan -out=tfplan
tofu apply tfplan          # never pipe apply into tail — the exit code comes from the pipe
```

Conventions worth matching: modules take a `env` variable validated to `dev|prod`; outputs carry names, IDs, and URIs but never credentials; comments explain *why*, particularly where a choice looks odd.

**Role assignments need propagation time.** The `key-vault` module uses a `time_sleep` of 60 s between granting a data-plane role and using it, because the first apply otherwise fails with a `Forbidden` that disappears on retry — the kind of failure people learn to retry past instead of read. Do the same for any new data-plane grant.

---

## 7. What Phase 10 built

Phase 10 kept the guide's **10A foundation, then 10B workloads** split, with
the previously completed Key Vault resources reused.

**10A completed:** one shared, capped Log Analytics workspace; one Container
Apps environment per application environment; exact PostgreSQL rules for live
outbound addresses; and the approved diagnostic settings.

**10B completed:** one manual migration job, API app, and web app per
environment, plus all six resource-scoped role assignments.

Points the guide gets right and are worth honouring:

- `ignore_changes` on the container image, so the deploy pipeline owns the tag and Tofu does not revert it to the quickstart image ([decision §14](infrastructure-decisions.md#14-ignore_changes-on-the-container-image)).
- **Narrow the deploy identities to individual app resources in 10.2, then remove the group-scoped `azurerm_role_assignment.deploy` from `bootstrap/role-assignments.tf` and re-apply bootstrap.** Until that happens `deploy_dev` can update production's Container App, because one resource group holds everything. This is the single most important security step in Phase 10 — [decision §19](infrastructure-decisions.md#19-one-resource-group-for-everything) accepted the flattened boundary specifically on the promise that this step restores it.
- Images come from public GHCR, so there is no `registry` block and no `AcrPull` assignment.

---

## 8. Things that will bite you

Each of these was found by testing, not by reading:

- **`Proxy__ForwardLimit` is 2.** Ingress and the web proxy are two hops. One makes the API see the web container as the client.
- **The web server must be Node.** Bun silently fails WebSocket upgrade proxying — the handshake returns nothing, no error.
- **Digest-pin base images with the multi-arch *index* digest**, not a platform-specific one. A linux/amd64 digest builds fine in CI and dies under qemu on an arm64 laptop.
- **`az postgres flexible-server ad-admin` does not exist** in CLI 2.88. It is `microsoft-entra-admin`.
- **`az keyvault key create --kty oct`** is rejected on a Standard vault, and `oct-HSM` needs a Managed HSM. This is why JWT signing is RS256 rather than a shared secret.
- **The local Docker VM has 975 MiB**, which the Angular build OOMs. CI is fine; local `docker compose --profile full up` is not.
- **A flaky integration test** — `CreatePurchaseOrderDraft_ConcurrentCreates_…` fails occasionally under parallel load and passes alone. Pre-existing, unrelated to infrastructure.

---

## 9. Outstanding work not in Phase 10

- **8.4 telemetry instruments** — deliberately deferred by the owner. OpenTelemetry and the OTLP exporter are registered, but there are no metrics for database pool wait, PostgreSQL token refresh, cache failures, SignalR connections, migration version, replica restarts, or external-service latency. The Phase 10 workspace now exists; adding the application instruments remains later work.
- **Atomic rate limiting** — still a distributed-cache read/modify/write. Deprioritised by the owner.
- **Azure SignalR or a backplane** — needed only above one API replica.
- **SMTP credential rotation** — an account action; the value is not in git history.
- **Integration secrets** — nothing is enabled yet; add the telemetry key when it exists.

---

## 10. Verifying you have not broken anything

```bash
dotnet build src/backend/Intelibill.slnx
dotnet test tests/backend/unit/Intelibill.Api.Unit.Tests          # 402
dotnet test tests/backend/unit/Intelibill.Application.Unit.Tests  # 931
dotnet test tests/backend/unit/Intelibill.Domain.Unit.Tests       # 201
dotnet test tests/backend/integration/Intelibill.Integration.Tests # 341, needs Docker
cd src/frontend && bun run build && bun run test                   # 1464
cd src/mobile/android/intelibill_mobile && flutter analyze && flutter test  # 1364
```

The four-way database isolation check from guide 7.5 is the one to re-run after touching anything about identities or grants: it must return `t,t,f,f`.
