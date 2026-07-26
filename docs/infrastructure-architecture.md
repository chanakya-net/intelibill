# Infrastructure Architecture

Azure infrastructure for intelibill, provisioned with OpenTofu and deployed through GitHub Actions.

**Status:** design reviewed against the application source on 2026-07-25. Nothing in this document is built yet. The earlier design was not production-deployable as written; this revision makes the required corrections explicit.

For the rationale and superseded choices, see [infrastructure-decisions.md](infrastructure-decisions.md). For executable sequencing, see [infrastructure-implementation-guide.md](infrastructure-implementation-guide.md).

---

## 1. Production-readiness verdict

The platform choices are suitable, but the application and the original deployment sequence need changes before production:

1. The API currently runs EF migrations during every startup. A deployment must instead run one dedicated migration job before changing application revisions.
2. The current Npgsql registrations require a password. Entra authentication must be wired into the EF data source, distributed PostgreSQL cache, and design-time/migration path.
3. The production web bundle calls `http://localhost:5277/api`, so a deployed browser cannot reach the API. Use relative `/api` and `/hubs` URLs behind an SSR reverse proxy, or load an external runtime configuration before Angular bootstraps.
4. The backend container does not bind the documented port `5277`; `EXPOSE` does not configure ASP.NET Core. Standardise on port `8080` or set `ASPNETCORE_HTTP_PORTS=5277`.
5. The API has no health-check endpoint, while the keep-warm job calls `/health`. Add `/health/live` and `/health/ready`, plus Container Apps probes.
6. Production CORS allows only localhost origins. Configure exact production origins and trusted forwarded headers.
7. `ProductHub` is unauthenticated and broadcasts cross-shop data to every client. `ShopUpdatesHub` is authorised, but JWT bearer authentication does not read SignalR's `access_token` query value. Fix both before exposing either hub.
8. Multiple API replicas are unsafe today: OAuth state is held in process memory, SignalR has no cross-replica service/backplane, and rate limiting is non-atomic. Cap the API at one replica until those are corrected.
9. `PostgresCache` is configured to create its table at runtime, but the proposed runtime database role is DML-only. Create the cache table in a migration/bootstrap step and set `CreateIfNotExists=false` in production.
10. A plaintext SMTP credential is present in the development settings file. Rotate it, remove it from the repository, and scrub it from history before treating the repository or environment as secure.

Provisioning Container Apps before these gates are closed produces containers that fail startup, cannot reach the database, cannot serve the frontend correctly, or expose tenant data.

---

## 2. Recommended target

Use two environments, each with an Angular SSR web container and an ASP.NET Core API container on Azure Container Apps.

- Keep OpenTofu state in an Azure Storage account using Entra data-plane authentication.
- Because the repository is public, use public GHCR by default: GitHub documents public packages as free and anonymous pulls require no stored credential. ACR Basic remains an optional Azure-local private registry for about $5.07/month.
- ~~Use a PostgreSQL Flexible Server per environment.~~ **Overridden on cost:** one shared server with a database per environment, accepting that development load, maintenance windows, point-in-time recovery, and CPU credits are shared, and that dev/prod separation rests on in-database grants — [decision §8](infrastructure-decisions.md#8-one-shared-postgresql-server-two-databases).
- For a controlled lean launch, use one B1ms for the shared server and cap the API at one replica. Note that both environments then draw from a single server's connection limit. Moving to B2s erases the sharing saving, at which point separate servers are the better trade.
- ~~For real customer data, prefer a production Container Apps environment integrated with a custom VNet and PostgreSQL private access.~~ **Locked to public access** with a narrow firewall allowlist, on cost. This is the lower-cost launch exception rather than the preferred end state; it is defensible here only because Entra-only authentication means no database password exists to guess.
- Use the application's existing OpenTelemetry pipeline to export traces/logs; do not install the New Relic .NET agent as a second instrumentation stack.

```
GitHub Actions (OIDC; pinned actions)
        │
        ├── OpenTofu state ── Azure Storage
        ├── images by digest ── public GHCR (default) or ACR
        └── deploy identity
                │
       ┌────────┴────────┐
       │                 │
   dev environment   prod environment
       │                 │
   web ──► api       web ──► api
            │                 │
        dev PostgreSQL    prod PostgreSQL
                              │
                      private endpoint/VNet
```

The web container should be the browser-facing origin and proxy `/api` and `/hubs` to the API. That preserves build-once promotion: the compiled Angular application uses relative URLs while each environment supplies `API_ORIGIN` to the SSR process.

---

## 3. Resource inventory and sizing

### Shared resources

| Resource | Configuration | Purpose |
|---|---|---|
| Storage account | Standard LRS, blob versioning and soft delete | OpenTofu state |
| Public DNS zone | Optional if authoritative DNS stays at the current provider | Application records |
| Registry | Public GHCR by default; ACR Basic optional | Backend and frontend images |

Do not move the whole authoritative DNS zone without first copying and validating every A, AAAA, CNAME, MX, TXT, CAA, and DNSSEC/DS record. Container Apps certificates do not require Azure DNS; leaving the zone at the existing provider is valid.

### Per environment

| Resource | Purpose |
|---|---|
| PostgreSQL Flexible Server | Environment-isolated application database |
| Key Vault | JWT key and enabled integration secrets |
| Log Analytics workspace | Container stdout/stderr and platform logs |
| Container Apps environment | Compute, ingress, networking, and logging boundary |
| Container App: web | Angular SSR and reverse proxy, port `4000` |
| Container App: API | ASP.NET Core, standardised port `8080` |
| Container Apps migration job | One-shot EF migration bundle under migrator identity |
| Production keep-warm job | Optional business-hours availability |
| User-assigned identities | Runtime, migrator, and gated deployment roles |

### Initial limits

| Setting | dev | lean prod | scale-ready prod |
|---|---:|---:|---:|
| API CPU / memory | 0.25 / 0.5 GiB | 0.5 / 1 GiB | load-tested value |
| web CPU / memory | 0.25 / 0.5 GiB | 0.5 / 1 GiB | load-tested value |
| API max replicas | 1 | **1 until code gates close** | 2+ with SignalR service/backplane |
| web max replicas | 1 | 1 initially | load tested |
| Npgsql pool | 5–10 | 10–15 | derive from DB connections / max replicas |
| PostgreSQL | B1ms, 32 GB | B1ms, 32 GB | B2s or General Purpose |

Large spreadsheet/PDF-style exports are assembled in memory. Load-test them before choosing 0.5–1 GiB limits; a small replica can be killed by an otherwise valid export.

---

## 4. Identity, state, and supply chain

Use separate identities for separate duties:

| Identity | Minimum scope |
|---|---|
| plan | Reader on every planned resource plus `Storage Blob Data Reader` on its state container |
| infrastructure apply | Contributor + RBAC Administrator on the single resource group. One group means one blast radius: "create" cannot be scoped below group level, so this cannot be narrowed without splitting the group — [decision §19](infrastructure-decisions.md#19-one-resource-group-for-everything) |
| deploy | Push image if using ACR, update Container Apps, start migration job; no general resource creation |
| application runtime | Key Vault secret read; optional registry pull; database DML only |
| migrator | Database DDL/DML for its environment only |

`Reader` and `Contributor` are management-plane roles and do not grant access to OpenTofu blobs. The state backend identities need `Storage Blob Data Reader` or `Storage Blob Data Contributor`.

Do not grant routine environment deployment identities `Contributor` over the shared resource group. The previous model also failed because an environment apply tried to create `AcrPull` on the shared ACR without role-assignment rights at that scope. Prefer creating shared role assignments in the shared layer, or grant a narrowly scoped, gated infrastructure identity temporarily.

PR plans can execute code supplied by a pull request. A state-reading plan must therefore run only for trusted same-repository pull requests, never on forks and never through `pull_request_target`. Pin actions by commit SHA, pin OpenTofu providers, generate an SBOM, scan images, and deploy immutable digests.

For the current public repository:

- Standard GitHub-hosted Actions usage is free.
- Public GHCR images can be pulled anonymously and public package storage/bandwidth is currently free.
- If private images are later required, use ACR with managed-identity pull, or a private GHCR package with an explicitly managed pull credential.

---

## 5. PostgreSQL

### Authentication

PostgreSQL Flexible Server Entra authentication requires `tenant_id` in the Terraform resource's `authentication` block. The server and each application/migrator identity must then be created as PostgreSQL Entra principals.

The code must construct a single `NpgsqlDataSource` using `UsePeriodicPasswordProvider` for scope `https://ossrdbms-aad.database.windows.net/.default` and reuse it for:

- `AddDbContext`;
- distributed PostgreSQL cache (`PostgresCacheOptions.DataSource` or its data-source-builder configuration);
- migration bundle/job execution.

The current plain `UseNpgsql(connectionString)` registrations and `[Required] Password` option do not do this. `ApplicationDbContextFactory` also needs an Entra-aware path or must be bypassed by a self-contained migration bundle configured at runtime.

### Runtime and migration privileges

- The API identity receives `CONNECT`, schema `USAGE`, and table/sequence DML only.
- The migrator receives DDL and establishes default privileges for runtime access to future objects.
- Application startup must not call `Database.MigrateAsync()`.
- Build one EF migration bundle/image, run it once, wait for success, then deploy the new revisions.
- Migrations must follow expand/contract compatibility because the previous application revision keeps serving while the migration runs.

The existing RLS migrations use `ENABLE` and `FORCE ROW LEVEL SECURITY`, which is correct. The current `PostgresSessionContextInterceptor`, however, sets `app.current_user_id` and `app.active_shop_id` when a connection opens—not on every command as the earlier document claimed. Verify pooled-connection reset behavior in an integration test and prefer transaction-local context if practical.

### Networking

With public PostgreSQL networking, the default firewall permits neither the API nor GitHub runners. A temporary runner rule only solves migrations; it does not give Container Apps a stable path. Either:

1. **Recommended:** put production Container Apps in a custom VNet, use PostgreSQL private access/private DNS, and run migrations as an in-environment Container Apps job; or
2. **Lean launch exception:** allowlist stable Container Apps outbound IPs and the temporary runner/job IP, monitor changes, and do not enable the broad “Allow Azure services” rule.

### Backup and recovery

Use at least 14 days of production point-in-time retention where the workload warrants it, enable geo-redundancy only after defining the recovery objective, and perform a restore drill. Separate servers make environment recovery independent. Backup storage up to the provisioned database storage is included; excess backup storage is usage-billed.

---

## 6. Application production contract

| Area | Required change |
|---|---|
| Startup | Remove unconditional `ApplyMigrationsAsync`; migration job owns schema |
| Database | Optional password locally; periodic Entra tokens in all Npgsql consumers; explicit pool size and SSL |
| Cache | Create `cache_entries` during migration/bootstrap; `CreateIfNotExists=false` in production |
| OAuth | Replace `InMemoryExternalOAuthStateStore` with distributed PostgreSQL/cache storage |
| Web/API routing | Relative `/api` and `/hubs`, SSR reverse proxy using per-environment `API_ORIGIN` |
| CORS/proxy | Exact production origins; trusted forwarded headers before HTTPS redirect/rate limiting |
| Health | `/health/live` and `/health/ready`; web live endpoint; startup/readiness/liveness probes |
| SignalR auth | Require authentication on `ProductHub`; group by active shop; read query-string bearer token only on hub paths |
| SignalR scale | Azure SignalR Service or tested Redis backplane; session affinity for transports that require it |
| Rate limiting | Atomic shared limiter, not distributed-cache read/modify/write; use trusted forwarded client IP |
| Containers | Pin base images/digests, match frontend Node engine, verify Bun runtime can execute the Node SSR command, and run as non-root |
| Mobile | Pass `--dart-define=API_BASE_URL=https://api.<domain>/api` in release builds |
| External services | Supply production SMTP, OAuth, product-lookup, HSN, and OTLP values; configure timeouts/retries and egress |
| Secrets | Rotate the committed SMTP credential and all possibly exposed test keys; remove/scrub history |

The product has no required durable local filesystem state. PostgreSQL and the distributed cache are the stateful services. Wolverine is currently an in-process dispatcher; it is not a durable broker/outbox and should not be treated as one.

Required production configuration includes:

- `Database__Host`, `Database__Port`, `Database__Database`, `Database__Username`, Entra mode, pool size, and SSL;
- `Jwt__Secret`, issuer, audience, and expiry settings;
- `App__BaseUrl`, exact CORS origins, and trusted proxy/network settings;
- enabled email and external-auth credentials/callback URLs;
- product lookup and HSN service URLs/keys;
- `Observability__Environment`, service name, endpoint, and `Observability__NewRelic__ApiKey`;
- SSR `API_ORIGIN`.

---

## 7. Pipeline and deployment order

Use this dependency order:

1. Bootstrap the shared resource group and remote-state storage locally.
2. Grant state blob data-plane roles; migrate state and verify locking/versioning.
3. Create narrowly scoped plan/apply/deploy identities and OIDC federated credentials.
4. Configure GitHub `dev` and `prod` environments, required reviewers, concurrency locks, and non-secret IDs.
5. Provision registry choice, DNS records, and one shared PostgreSQL server with a database per environment ([decision §8](infrastructure-decisions.md#8-one-shared-postgresql-server-two-databases)). The public-network path is locked in, so there is no VNet or private DNS to provision.
6. Create environment Key Vaults and runtime/migrator identities with RBAC and purge protection.
7. Add secret values out of band. Do not read their values through an OpenTofu data source: sensitive data sources are still persisted in state. Construct versionless URIs as `${vault_uri}secrets/<name>` and let Container Apps resolve them.
8. Bootstrap database principals, revoke inappropriate public access, create the cache table, and test cross-environment denial and RLS.
9. Build backend and frontend once; run unit/integration tests, scan, sign if required, push by digest, and record both digests.
10. Run the dev migration job; deploy both dev digests; run health, login, data-isolation, SSR, SignalR, and external-integration smoke tests.
11. Only after dev succeeds, enter the production reviewer gate; run the production migration job; deploy the same digests; smoke test; retain the previous digests for rollback.
12. Bind domains/certificates through one declared owner (OpenTofu/`azapi` or an explicitly documented manual step), not a mixture of IaC and untracked CLI mutations.
13. Enable keep-warm, dashboards, alerts, budgets, backup/restore checks, and incident/rollback runbooks.

Dev and prod promotion must be sequential, not parallel branches from the build. A production job must depend on successful dev migration, deployment, and smoke test.

---

## 8. Runtime behavior

Container Apps scheduled-job cron expressions use UTC. A single `*/10 6-20 * * 1-6` expression means 06:00–20:50 UTC, not 06:00–20:00 India Standard Time. It also pings less frequently than the default 300-second scale-down interval, so a replica can return to zero between pings.

If warm availability is required:

- use a timezone-correct set of UTC schedules;
- ping at less than five-minute intervals (for example, every four minutes), or set `min_replicas=1` for the required window through an operational schedule;
- call real readiness endpoints;
- use a small, digest-pinned curl image;
- remember that persistent SignalR connections themselves keep replicas active.

Use sticky sessions for SignalR transports that need affinity. Sticky sessions alone do not distribute hub broadcasts; Azure SignalR Service or a backplane is still required for more than one API replica.

---

## 9. Observability and operations

The API already registers OpenTelemetry and an OTLP exporter. Keep that instrumentation and map its API key from Key Vault. Adding the New Relic .NET agent would duplicate spans/metrics and add another runtime mechanism.

- Send platform/container logs to Log Analytics with a 30-day retention/cap.
- Export application telemetry through the existing OpenTelemetry pipeline.
- Add alerts for readiness failures, 5xx rate, latency, replica restarts/OOM, PostgreSQL connections/CPU credits/storage, migration failures, and certificate/secret expiry.
- Scrub JWTs, authorization headers, secrets, email credentials, and sensitive entity fields.
- Record deployed digests and migration version in deployment metadata.

New Relic's current free tier covers 100 GB/month and one full user. Log Analytics has a 5 GB/month Azure Monitor ingestion allowance per billing account, after which East US ingestion is currently $2.30/GB. Treat both as usage caps, not guarantees of a permanently free service.

---

## 10. Cost model

Prices below are public pay-as-you-go list prices checked 2026-07-25 for East US, using 730 hours/month and USD. Taxes, negotiated discounts, egress, external email/API services, and domain registration are excluded.

Exact East US meter rates were checked through the [official Azure Retail Prices API](https://learn.microsoft.com/en-us/rest/api/cost-management/retail-prices/azure-retail-prices). The linked product pages below describe grants and billing rules that are not represented by a single meter.

### Unit prices and assumptions

| Step/resource | One-time | Recurring / usage driver |
|---|---:|---:|
| State storage, Hot LRS | none | `<$0.01/month` for a small state file; storage and operations vary |
| Public GHCR | none | `$0` for this public repository under current public-package terms |
| ACR Basic, optional | none | `$0.1666/day` = **$5.07/month**; 10 GB included, then storage/egress |
| PostgreSQL B1ms + 32 GB | bootstrap only | `0.017 × 730 + 32 × 0.115` = **$16.09/month each** |
| PostgreSQL B2s + 32 GB | bootstrap only | `0.068 × 730 + 32 × 0.115` = **$53.32/month** |
| PostgreSQL General Purpose 2 vCore + 32 GB | bootstrap only | `0.178 × 730 + 32 × 0.115` = **$133.62/month** before HA |
| Container Apps, two prod apps warm ~365 h/month | deploy | **$10.21/month mostly idle** after the monthly grants; up to about **$34.02** if CPU is billed active throughout those hours |
| Dev Container Apps, ~20 h/month | deploy | about **$0–$0.32/month**, depending on whether the subscription grant is already consumed |
| Keep-warm job, 4-minute interval, ~2 s/run | deploy | about **$0.03–$0.20/month** |
| Requests | none | first 2 million/month included; then **$0.40/million** |
| Public DNS zone | delegation work | **$0.50/month** plus roughly **$0.40/million queries** |
| Key Vault Standard | secret entry | **$0.03/10,000 operations**; expected `<$0.10/month` at this scale |
| Log Analytics | dashboards/alerts | first 5 GB/month per billing account allowance; **$2.30/GB** above it in East US; 30 days retention included |
| New Relic | dashboards | `$0` up to current 100 GB/one-user allowance; **$0.40/GB** original-data ingest beyond it |
| Azure SignalR Standard S1, scale-out scenario | integration | `$1.61/day` = **$48.96/month**, plus messages |
| Production custom-VNet fixed resources | network setup | approximately **$21.90–$25.55/month** for Standard load balancer/public IP resources, plus data |

Container Apps example: two 0.5-vCPU/1-GiB replicas for 365 hours use 1,314,000 vCPU-seconds and 2,628,000 GiB-seconds. After the 180,000 vCPU-second and 360,000 GiB-second grants, idle CPU is about $3.40 and memory about $6.80. Actual billing depends on request activity and grant sharing across the subscription.

### Monthly scenarios

| Scenario | Included assumptions | Monthly total |
|---|---|---:|
| **Lean controlled launch** | separate dev/prod B1ms; public GHCR; API max 1; business-hours mostly-idle prod; light dev; DNS/KV/state; `<5 GB` logs | **about $43.40 + egress/external services** |
| **As actually built** | **one shared** B1ms (−$16.09 against the row above); public GHCR; public DB access; API max 1; no DNS zone yet | **about $27 at East US rates — re-price for `centralindia`** |
| Lean launch with ACR | lean scenario plus ACR Basic | **about $48.50 + variable usage** |
| **Scale-ready, public DB access** | prod B2s + dev B1ms; Azure SignalR Standard; business-hours containers; public GHCR | **about $130/month + variable usage** |
| **Scale-ready, private production network** | scale-ready scenario plus production custom VNet/private DNS fixed resources | **about $152–156/month + variable usage** |

Add approximately $23.81/month if both production apps are CPU-active rather than idle for the assumed business hours. Add $11.50/month when Log Analytics reaches 10 GB total ingest (5 GB billable). General Purpose or database high availability can dominate the total; price that separately after measuring and defining an availability objective.

Provider charges have no setup fee, but implementation has material labour. A defensible planning range is **12–23 engineer-days**: 2–4 hours for state bootstrap, 2–4 days for identity/network/IaC, 5–10 days for the application production fixes, 2–4 days for pipelines, and 2–4 days for security/load/restore tests and runbooks. At an assumed $50–$150/hour, that is roughly **$4,800–$27,600 one time**. This is an effort assumption, not a provider price.

### Pricing sources

- [Azure Container Apps pricing](https://azure.microsoft.com/en-us/pricing/details/container-apps/)
- [Azure Database for PostgreSQL Flexible Server pricing](https://azure.microsoft.com/en-us/pricing/details/postgresql/flexible-server/)
- [Azure Container Registry pricing](https://azure.microsoft.com/en-us/pricing/details/container-registry/)
- [Azure SignalR Service pricing](https://azure.microsoft.com/en-us/pricing/details/signalr-service/)
- [Azure Load Balancer pricing](https://azure.microsoft.com/en-us/pricing/details/load-balancer/)
- [Azure IP address pricing](https://azure.microsoft.com/en-us/pricing/details/ip-addresses/)
- [Azure Blob Storage pricing](https://azure.microsoft.com/en-us/pricing/details/storage/blobs/)
- [Azure Key Vault pricing](https://azure.microsoft.com/en-us/pricing/details/key-vault/)
- [Azure DNS pricing](https://azure.microsoft.com/en-us/pricing/details/dns/)
- [Azure Monitor pricing](https://azure.microsoft.com/en-us/pricing/details/monitor/)
- [GitHub Packages billing](https://docs.github.com/en/billing/concepts/product-billing/github-packages)
- [GitHub Actions billing](https://docs.github.com/en/billing/concepts/product-billing/github-actions)
- [New Relic pricing](https://newrelic.com/pricing)

---

## 11. Operational risks and acceptance gates

| Risk | Acceptance gate |
|---|---|
| Tenant data leak through `ProductHub` | Anonymous and cross-shop connections fail in integration tests |
| Startup migration race / runtime DDL | Startup does not migrate; one migration job succeeds before revision update |
| Private/public database unreachable | Network test runs from the actual API and migration-job identities |
| Entra token expires | Connection recycle test remains successful beyond token refresh |
| Cache table creation denied | Table exists before runtime; runtime role has DML only |
| Browser uses localhost API | Production SSR/browser smoke test exercises API and both hubs |
| Replica-local OAuth state | Callback succeeds after forced replica restart/redistribution |
| SignalR split broadcasts | Multi-replica test proves delivery through service/backplane, or API max stays 1 |
| Rate-limit race/proxy spoof | Concurrent multi-replica and forwarded-header tests pass |
| Secrets in source/state | exposed values rotated; history and OpenTofu state scans are clean |
| DNS/email outage | full DNS inventory and rollback captured before delegation |
| Database recovery failure | timed restore drill meets the documented RPO/RTO |
| Cost surprise | budgets, log caps, egress/API quotas, and monthly review are enabled |

---

## 12. Technical references

- [PostgreSQL Flexible Server private networking](https://learn.microsoft.com/en-us/azure/postgresql/flexible-server/concepts-networking-private)
- [Container Apps custom virtual networks](https://learn.microsoft.com/en-us/azure/container-apps/custom-virtual-networks)
- [Container Apps scale rules and cooldown](https://learn.microsoft.com/en-us/azure/container-apps/scale-app)
- [Container Apps scheduled jobs (UTC cron)](https://learn.microsoft.com/en-us/azure/container-apps/jobs)
- [Container Apps Key Vault references](https://learn.microsoft.com/en-us/azure/container-apps/manage-secrets)
- [Scale ASP.NET Core SignalR](https://learn.microsoft.com/en-us/aspnet/core/signalr/scale)
- [AzureRM PostgreSQL Flexible Server resource](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/postgresql_flexible_server)
- [GitHub Container Registry](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-container-registry)
- [backend-architecture.md](backend-architecture.md)
- [frontend-architecture.md](frontend-architecture.md)
