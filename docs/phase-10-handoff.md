# Phase 10 handoff — environment infrastructure

**Written 2026-07-26 on branch `infra-setup` at commit `a6ea6d01`.** Everything below was verified against the live subscription on that date, not recalled from the runbook. Where this document and [infrastructure-implementation-guide.md](infrastructure-implementation-guide.md) disagree, this document is newer — the guide's Phase 8 and Phase 9 text has been amended, but its Phase 10 HCL examples predate the application as it now stands.

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
| **10 environment infrastructure** | **not started — this is you** |
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
```

### Workload identities (Phase 10 attaches these to the apps)

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

## 3. Blockers to clear before you write HCL

1. **`Microsoft.App` is not registered on this subscription.** Container Apps creation fails until it is. `Microsoft.OperationalInsights` is registered.
   ```bash
   az provider register -n Microsoft.App --wait
   ```
2. **PostgreSQL has zero firewall rules.** The Phase 7 temporary rule was removed, so nothing can currently reach the server. The Container Apps environment's outbound IPs must be allowlisted or every app start fails on connection. Do **not** enable the blanket "Allow Azure services" rule — [decision §8](infrastructure-decisions.md#8-one-shared-postgresql-server-two-databases) accepted public networking on the basis of a *narrow* allowlist.
3. **Two open decisions.** Launch replica cap (outstanding item 3) determines whether SignalR needs a backplane; business timezone and warm-hours (item 4) determine the Phase 13 cron. API max replicas is 1 until someone says otherwise — the rate limiter is still non-atomic and there is no SignalR backplane.

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

## 7. What Phase 10 has to build

Split as the guide says — **10A foundation, then 10B workloads** — except the vault half of 10A is already done.

**10A remaining:** Log Analytics workspace per environment (or one shared, with a retention cap), the Container Apps environment, PostgreSQL firewall rules for its outbound IPs, and diagnostic settings.

**10B:** the migration job, the API app, the web app, and the role assignments in guide §10.2.

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

- **8.4 telemetry instruments** — deliberately deferred by the owner. OpenTelemetry and the OTLP exporter are registered, but there are no metrics for database pool wait, PostgreSQL token refresh, cache failures, SignalR connections, migration version, replica restarts, or external-service latency. Natural to fold in during Phase 10, when a real OTLP endpoint exists.
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
