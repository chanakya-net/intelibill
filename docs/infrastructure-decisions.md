# Infrastructure Decisions

Rationale behind [infrastructure-architecture.md](infrastructure-architecture.md). Each decision records what was chosen, what was rejected, why, what it costs, and how to reverse it.

Read this when you disagree with something in the architecture document, or before changing it.

---

## 2026-07-25 code and pricing audit

The original decisions remain below as useful history, but the following amendments are authoritative. They were made after inspecting the ASP.NET Core, Angular SSR, Flutter, Docker, authentication, database, SignalR, cache, and observability code and checking current official prices.

| Original decision | Audited decision | Reason |
|---|---|---|
| §5 ACR over GHCR | **Public GHCR by default; ACR optional** | The repository itself is public. Public GHCR can be pulled anonymously and current public-package storage/bandwidth is free; the original proprietary-code and private-quota arguments do not apply. |
| §7 secret URI through a data source | **Out-of-band value, URI constructed without reading the data source** | A Key Vault secret data source exposes `value`, which is persisted in OpenTofu state even if marked sensitive. Use the known vault URI and secret name. |
| §8 one shared database server | **One server per environment** | Current East US B1ms + 32 GB is $16.09/month; two are $32.18. Independent recovery, maintenance, CPU credits, and access control justify the cost. |
| §9 unconditional B2s | **B1ms for a one-replica controlled launch; resize from measurements** | B2s + 32 GB is currently $53.32/month, not $28. The application cannot safely run five API replicas until its SignalR, OAuth state, and rate limiting are fixed. |
| §13 ten-minute keep-warm | **Timezone-correct interval shorter than five minutes, or min replica 1** | Container Apps schedules use UTC and the default scale-down cooldown is 300 seconds. The proposed cron was neither IST business hours nor frequent enough to keep a replica warm. `/health` does not yet exist. |
| §15 New Relic .NET agent | **Keep the existing OpenTelemetry/OTLP path** | The API already registers OpenTelemetry and an OTLP exporter. Adding an agent duplicates instrumentation and telemetry. |
| §17 migration from runner | **Container Apps migration job for private production** | The current design-time factory is not Entra-aware and a hosted runner cannot route to private PostgreSQL. A migration bundle job uses the dedicated migrator identity and the same VNet. |

Additional audited decisions:

- Cap the API at one replica until OAuth state is distributed, SignalR has authentication/shop grouping plus Azure SignalR or a backplane, and rate limiting is atomic.
- Use the web SSR container as the browser origin and proxy relative `/api` and `/hubs` paths. The current production frontend compiles `http://localhost:5277/api`.
- Remove automatic startup migrations; create the PostgreSQL cache table before runtime; standardise the API container port; add health endpoints/probes; configure production CORS and trusted forwarded headers.
- Prefer production PostgreSQL private access for real customer data. A public-firewall launch is a documented temporary exception, not the target.
- Split plan, infrastructure-apply, routine-deploy, runtime, and migrator privileges. State access requires Azure Storage blob **data-plane** roles.
- Rotate and purge the plaintext SMTP credential currently present in development settings.

The current cost scenarios and source links are maintained in [infrastructure-architecture.md §10](infrastructure-architecture.md#10-cost-model). At the audited rates, the lean controlled launch is about **$43.40/month**, scale-ready public networking about **$130/month**, and scale-ready private production about **$152–156/month**, before egress and external services.

---

## Summary

| # | Decision | Chose | Over |
|---|---|---|---|
| [1](#1-opentofu-as-the-provisioning-tool) | Provisioning tool | OpenTofu | Bicep, Pulumi, `azd`, portal |
| [2](#2-azure-container-apps-as-the-compute-platform) | Compute | Container Apps | App Service, AKS, VMs |
| [3](#3-github-oidc-federated-identity-instead-of-stored-credentials) | CI → Azure auth | OIDC federated identity | Service principal secret |
| [4](#4-opentofu-does-not-write-credentials-into-github) | Credential flow | Nothing written to GitHub | Tofu → GitHub secrets |
| [5](#5-azure-container-registry-over-github-container-registry) | Registry | Public GHCR; ACR optional | Mandatory ACR |
| [6](#6-entra-id-authentication-for-postgresql) | DB auth | Entra ID tokens | Stored password |
| [7](#7-key-vault-secrets-created-out-of-band) | Secret creation | Manual value; URI constructed without reading it | `random_password` / secret data source |
| [8](#8-one-shared-postgresql-server-two-databases) | DB topology | Server per environment | Shared server |
| [9](#9-b2s-rather-than-b1ms) | DB SKU | B1ms launch; measured resize | Premature B2s |
| [10](#10-explicit-npgsql-pool-sizing) | Pooling | Explicit `Maximum Pool Size` | Npgsql default |
| [11](#11-per-environment-directories-not-tofu-workspaces) | State layout | Directories | `tofu workspace` |
| [12](#12-build-once-promote-the-digest) | Release | Promote digest | Rebuild per env |
| [13](#13-scale-to-zero-with-an-in-azure-keep-warm-job) | Prod scaling | Correct UTC job `<5 min`, or min 1 | Broken 10-minute schedule |
| [14](#14-ignore_changes-on-the-container-image) | Tofu/CD split | `ignore_changes` on image | Tofu owns the tag |
| [15](#15-log-analytics-retained-new-relic-for-backend-apm-only) | Observability | Log Analytics + existing OTel export | Additional .NET agent |
| [16](#16-the-frontend-must-be-a-container) | Frontend hosting | Container | Static Web Apps |
| [17](#17-migrations-run-from-the-runner-behind-a-temporary-firewall-rule) | Migrations | Container Apps job for private prod | Startup / private runner |
| [18](#18-separate-runtime-and-migrator-database-identities) | DB principals | Split runtime/migrator | Single principal |

---

## 1. OpenTofu as the provisioning tool

**Context.** Infrastructure must be reproducible, reviewable in pull requests, and destroyable. Manual portal work fails all three.

**Options.**

- **Bicep** — Azure-native, no state file to store or secure, first-party support for new resource types on day one. Locked to Azure. Weaker module ecosystem, no `plan` output as legible as Tofu's.
- **Pulumi** — real languages, excellent for complex logic. Adds a runtime dependency and, on the hosted backend, a third-party service holding state.
- **Azure Developer CLI (`azd`)** — one command from zero to deployed. Excellent for demos, hits a ceiling quickly once configuration diverges from its templates.
- **OpenTofu** — mature `azurerm` provider, large module ecosystem, portable, open governance.

**Decision.** OpenTofu.

**Why.** The `plan`-as-PR-comment workflow is the single most valuable safety property here, and Tofu's plan output is the clearest of the options. Portability matters because nothing in this design is deeply Azure-idiomatic — Container Apps, managed Postgres, and a registry all have equivalents elsewhere. OpenTofu over Terraform specifically for the licence: Terraform's BUSL change makes long-term use in a commercial product a question worth not having.

**Costs.** A state file that must be stored, secured, and never corrupted. Provider lag behind new Azure features. A chicken-and-egg bootstrap (see the architecture doc, §10).

**Reversing.** Painful. Bicep would mean rewriting every module and importing existing resources. Decide now.

---

## 2. Azure Container Apps as the compute platform

**Context.** The repository already builds `src/backend/Dockerfile` and `src/frontend/Dockerfile`. The platform should consume those without re-architecture.

**Options.**

- **App Service (Linux containers)** — most mature, best documentation, deployment slots give real blue/green. No scale-to-zero, so the meter runs at 100% around the clock even at 3am.
- **AKS** — total control, and a control plane, node pools, upgrade cycles, and networking to operate. For two containers this is not a trade-off, it is a mistake.
- **Container Instances** — simplest, but no ingress, no scaling, no revision management. Not a production platform.
- **Container Apps** — managed Kubernetes underneath, KEDA autoscaling, scale to zero, built-in ingress with free managed certificates, revision-based rollout.

**Decision.** Container Apps.

**Why.** Scale-to-zero is worth roughly $18/month here, which is a large fraction of the total bill. Managed certificates remove the entire certificate-renewal problem. Revisions give rollback without a second environment.

**Costs.** Less mature than App Service; some `azurerm` resources (notably custom domains and managed certificates) are fiddly. No deployment slots — revisions with traffic splitting are the equivalent, and they work differently.

**Reversing.** Moderate. Both apps are OCI containers; App Service consumes the same images. The module boundary in `.tofu/modules/container-app/` is the seam.

---

## 3. GitHub OIDC federated identity instead of stored credentials

**Context.** GitHub Actions must authenticate to Azure to provision and deploy.

**Options.**

- **Service principal with client secret** — create an app registration, generate a secret, store it as `AZURE_CREDENTIALS` in GitHub. The conventional approach.
- **OIDC federated identity** — GitHub issues a short-lived signed token per job; Azure trusts it based on a claim about the repository, branch, or environment. No stored secret.

**Decision.** OIDC with **user-assigned managed identities**, federated to GitHub subjects.

**Why.** A stored client secret is a long-lived credential sitting in a system that is not Azure. It expires — usually during an incident. It leaks through workflow logs, forks, and compromised actions. It grants the same access regardless of which workflow presents it.

Federated identity fixes all of it. Tokens live minutes. Nothing durable exists to steal.

The property that matters most is **subject scoping**. The credential is bound to `repo:chanakya-net/intelibill:environment:prod`. GitHub only issues a token with that subject to a job that has actually entered the `prod` environment — meaning it passed the required reviewer. An attacker who can push workflow YAML still cannot obtain a prod token. With a stored secret, anyone who can run any workflow has full access.

User-assigned managed identity over app registration because a UAMI has **no client-secret field at all**. Nobody can weaken it later by adding one "temporarily".

**Costs.** More initial setup. The `Contributor`-is-not-enough problem (§below) is a common first-run failure. Local development still needs `az login`.

**Note.** Provisioning role assignments requires `Role Based Access Control Administrator` alongside `Contributor`. `Contributor` explicitly excludes writing role assignments, and creating the app's `AcrPull` grant *is* a role assignment. Nearly every first attempt fails here.

**Reversing.** Trivial but unwise.

---

## 4. OpenTofu does not write credentials into GitHub

**Context.** The original proposal was: OpenTofu provisions infrastructure, generates credentials, and writes them into GitHub repository secrets, where the deployment pipeline reads them.

**Decision.** Rejected. Nothing is written to GitHub, and no application secret passes through CI.

**Why.** The pattern seems tidy and creates three distinct problems.

*It needs a credential to manage credentials.* The `github` Tofu provider requires a token with admin scope on the repository. That token has to live somewhere, and it is more powerful than anything it writes. The bootstrap problem moves; it does not go away.

*It writes plaintext secrets into state.* Anything Tofu generates or reads is stored unencrypted in the state file. A generated database password would exist in Azure, in GitHub, and in the state blob. Three copies, three rotation paths, three ways to leak. State files routinely end up in local working directories and support tickets.

*It solves a problem that should not exist.* The pipeline needs Azure access — OIDC provides that with no secret (§3). The application needs a database credential — Entra ID means none exists (§6). The application needs a JWT signing key — Key Vault delivers it at runtime without CI ever seeing it (§7). Once each need is met properly, there is nothing left to copy into GitHub.

**What replaces it.** GitHub holds only *variables*: `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, and one client ID per identity per environment — `AZURE_CLIENT_ID_INFRA` and `AZURE_CLIENT_ID_DEPLOY`, plus a repository-scoped `AZURE_CLIENT_ID_PLAN`. All are non-secret identifiers, safe in logs. The identities are split rather than one per environment so that the deploy that runs on every merge is not as privileged as the gated infrastructure apply; see the implementation guide's Phase 3. The infra-to-deploy boundary carries OpenTofu **outputs** — resource names — not credentials.

**Costs.** More moving parts to understand up front. The payoff is that there is no rotation calendar and no secret inventory.

---

## 5. Azure Container Registry over GitHub Container Registry

> **Superseded by the 2026-07-25 audit.** The repository is public, so public GHCR is the lean default at $0 under current terms. Keep ACR Basic as a $5.07/month option if images later become private or Azure-local managed-identity pull is required.

**Context.** GHCR appears free, which would save the $5/month ACR Basic charge.

**Decision.** ACR Basic.

**Why.** GHCR would reintroduce exactly the credential problem decision §3 removes.

*Private images need a durable pull credential.* Container Apps pulls images on every cold start and scale-out, long after the workflow that pushed them has ended. `GITHUB_TOKEN` is job-scoped and expires with the job, so pulling from a private GHCR repository requires a long-lived PAT with `read:packages`, stored as a Container Apps secret and rotated before expiry. That is precisely the artefact this design exists to eliminate — and it buys $5.

*Public images leak the product.* Making packages public removes the auth requirement and the quota limits, but anyone can then pull the backend image and decompile the .NET assemblies with ILSpy. Business logic, RLS policy definitions, and JWT handling become readable. Not acceptable for a commercial product.

*The quota does not fit.* GitHub Free allows 500 MB of private package storage. The backend image is roughly 220 MB and the Bun SSR image 150–250 MB. A single push of both approaches the limit; every SHA-tagged build adds layers. Paid storage arrives within days, plus egress charges on every Azure-side pull — those are *not* the free Actions-to-Packages transfers, because the puller is Azure.

ACR Basic includes 10 GB and supports `AcrPull` via managed identity, so image pulls need no credential at all.

**Costs.** $5/month. Roughly 10% of the bill, for the only registry option that preserves the zero-credential property.

---

## 6. Entra ID authentication for PostgreSQL

**Context.** `DatabaseOptions` currently declares `Password` as `[Required]` and concatenates it into the connection string. In Azure that password would need to live in Key Vault and be delivered to the app.

**Options.**

- **Password in Key Vault** — conventional. One secret, referenced at runtime, rotated manually.
- **Entra ID authentication** — the app's managed identity authenticates directly; Npgsql presents a short-lived token as the password.

**Decision.** Entra ID.

**Why.** The strongest form of secret management is not storing the secret well — it is not having one. With Entra auth there is no database password to rotate, leak, commit, or discover in a support ticket. Access is revoked by removing a role assignment, which takes effect immediately, rather than by changing a password and redeploying everything that used it.

It also composes with the rest of the design: the same managed identity already pulls images and reads Key Vault, so there is one identity per app rather than one identity plus one credential.

**Costs.** Real application changes, not just configuration:

- Tokens expire roughly hourly, so the code must use `NpgsqlDataSourceBuilder.UsePeriodicPasswordProvider` with a refresh interval below expiry. A one-shot provider authenticates correctly and then fails every reconnection after the first hour — a failure that passes every test and surfaces in production an hour after deployment.
- `Password` must become optional in `DatabaseOptions`.
- Local development still uses password auth against `docker-compose`, so both paths must be supported and both must be tested.

**Reversing.** Easy. Add a password to Key Vault and set the config value.

---

## 7. Key Vault secrets created out of band

> **Corrected by the 2026-07-25 audit.** Manual secret creation remains correct, but do not use `data.azurerm_key_vault_secret`: its value is available to the provider and retained in state. Construct a versionless secret URI from the vault URI and secret name.

**Context.** The JWT signing secret and New Relic licence key must reach the running containers.

**Options.**

- **`random_password` + `azurerm_key_vault_secret`** — fully automated, reproducible.
- **Manual creation, referenced by `data` source** — Tofu stores only the URI.

**Decision.** Manual creation, referenced by URI.

**Why.** Tofu stores every managed resource attribute in state as plaintext, including generated passwords. A `random_password` for the JWT signing key would sit in the state blob in clear text — and the JWT signing key is the credential that lets an attacker mint valid tokens for any user in any shop. It is arguably the most sensitive value in the system.

Referencing by `data` source means state holds a vault URI, which is not a secret. The Container App resolves the value at runtime through its managed identity. Tofu never sees the plaintext.

**Costs.** Two manual steps at bootstrap, and a new environment is not reproducible by `apply` alone. Documented in the architecture doc, §10.

**Trade-off accepted.** Reproducibility is worth less than keeping the token-signing key out of a file that gets copied around.

---

## 8. One shared PostgreSQL server, two databases

> **Superseded by the 2026-07-25 audit.** Provision separate servers before the first production write.

**Context.** Two environments need PostgreSQL. Two B1ms servers cost roughly $32/month, against roughly $16 for one.

**Decision.** One shared server, one database per environment. **Flagged for review** — see below.

**Why.** Chosen to save approximately $16/month when the alternative was two B1ms servers.

**The saving did not survive.** Decision §9 sizes the shared server up to B2s to clear a connection-count ceiling, bringing it to roughly $28/month against roughly $32 for two separate B1ms servers. **The actual saving is now about $4/month.**

**What that $4 costs.**

- *Point-in-time restore is server-wide.* Restoring production to an earlier point also rewinds dev. Recovering one database alone means restoring to a new server, then `pg_dump`/`pg_restore` across — during an incident, under pressure.
- *Isolation is a `GRANT` away from failing.* PostgreSQL grants `CONNECT` to `PUBLIC` on every new database by default. Without the explicit `REVOKE CONNECT ... FROM PUBLIC`, the dev role can open the production database. One missing statement, no error, silent exposure.
- *Resource contention is shared.* A runaway dev query consumes IOPS and memory the production workload needs. Burstable SKUs make this worse: exhausting the CPU credit balance throttles everything on the server.
- *Maintenance windows are shared.* Server restarts hit both environments.

**Recommendation.** Revisit. At a $4/month delta, separate servers are the better trade — independent restore, independent blast radius, and no dependency on one `REVOKE` statement being correct. This decision is recorded as chosen but not endorsed.

**Reversing.** Cheap if done before production data exists: provision a second server, point dev at it. After that it is a migration. **Decide before the first production write.**

---

## 9. B2s rather than B1ms

> **Superseded by the 2026-07-25 audit.** Current East US list cost is $53.32/month including 32 GB, and the code must remain at one API replica until scale-out blockers are fixed. Start each environment at B1ms only for a controlled low-load launch and resize from observed CPU credits, connections, and latency.

**Context.** B1ms is the cheapest Flexible Server SKU at roughly $16/month including storage.

**Decision.** B2s, roughly $28/month.

**Why.** B1ms caps `max_connections` at **50**. That limit is fixed by the SKU and cannot be raised by configuration.

The binding constraint is that **each Container Apps replica maintains its own independent connection pool**. Connections are consumed per replica, not per application. Production scaling to 5 replicas with a 20-connection pool needs 100 connections — double what B1ms allows — plus dev, plus migrations, plus administrative sessions.

The failure mode is the worst kind: it appears only under scale-out, meaning the database starts refusing connections at precisely the moment traffic is high enough to need them. It will not reproduce in dev, where one replica never approaches the limit.

B2s (2 vCore, 4 GiB) allows 429 connections, giving roughly 3.4× headroom over the projected peak of ~125.

**Options rejected.**

- *Stay on B1ms, cap pools at 8 and max-replicas at 3.* Saves $12/month and permanently caps production throughput at ~24 database connections. Growing past it requires a server resize — the operation you least want to discover you need under load.
- *PgBouncer.* Azure's built-in connection pooling would multiplex many client connections onto few server ones. Availability on Burstable tiers has varied; verify before relying on it. Adds a component to reason about for a problem $12/month solves outright.

**Costs.** $12/month more than B1ms, and it reduces decision §8's saving to roughly $4 — which is why §8 is flagged.

---

## 10. Explicit Npgsql pool sizing

**Context.** `Infrastructure/DependencyInjection.cs` calls `UseNpgsql` with no pool configuration. Npgsql defaults `Maximum Pool Size` to **100**.

**Decision.** Set it explicitly: 20 for prod, 10 for dev.

**Why.** The default is wrong on any SKU here. On B1ms, one replica over-subscribes the server twofold. On B2s, five replicas at the default would demand 500 connections against 429.

More fundamentally, a pool of 100 per replica is not useful. Concurrency is bounded by the database's ability to execute queries, not by how many idle sockets the client holds. An oversized pool converts a queue inside the application — where it is visible, measurable, and bounded — into connection exhaustion at the database, where it takes down every other consumer including dev, migrations, and administrative access.

**Costs.** Under sustained load, requests wait for a pooled connection instead of opening a new one. That is the correct behaviour: backpressure surfaces in application metrics rather than as a server-wide failure.

**Note.** This is a genuine bug in the current configuration, independent of Azure. Worth fixing regardless of whether this infrastructure is built.

---

## 11. Per-environment directories, not `tofu workspace`

**Context.** Two environments need separate state.

**Options.**

- **`tofu workspace`** — one directory, one config, state selected by CLI command.
- **Separate directories** — `envs/dev/` and `envs/prod/` sharing `modules/`.

**Decision.** Separate directories.

**Why.** Workspaces hold the most consequential fact — which environment you are about to modify — in invisible CLI state. `tofu workspace select` is silent, persists between sessions, and is easy to forget after switching context. Running `apply` in the wrong workspace applies a dev plan to production, and nothing in the command you typed indicates the target.

With directories, the environment is in the path, the backend config, the state file name, and every line of plan output. `cd envs/prod` is visible in shell history and in CI logs. The failure requires actively navigating to production rather than merely forgetting where you already were.

Shared `modules/` keeps the duplication to per-environment variable values, which is exactly what should differ.

**Costs.** Two `backend.tf` files instead of one. Adding an environment means copying a directory. Both are cheap and explicit.

---

## 12. Build once, promote the digest

**Context.** Production must run what was validated in dev.

**Options.**

- **Rebuild per environment** — same commit, separate build per stage.
- **Build once, promote by digest** — one build, the identical image reference deployed onward.

**Decision.** Build once, promote by digest.

**Why.** Two builds of the same commit are not guaranteed to produce the same image. `mcr.microsoft.com/dotnet/aspnet:10.0` is a moving tag; `oven/bun:latest` more so. Transitive dependencies resolve at build time. `apt-get update` pulls whatever is current. Any of these can differ between a build at 09:00 and one at 11:00 from the same source.

If production rebuilds, dev validated a different artefact. The whole point of a dev stage disappears.

Deploying by **digest** rather than tag makes this concrete. `@sha256:...` names exact bytes. A tag can be overwritten; a digest cannot.

**Costs.** The pipeline must thread the digest between jobs. Rollback means redeploying a previous digest, so digests need to be discoverable — recorded in the workflow summary and recoverable from ACR.

---

## 13. Scale to zero with an in-Azure keep-warm job

> **Corrected by the 2026-07-25 audit.** Keep the in-Azure job, but use UTC schedules that represent the intended timezone and an interval shorter than the 300-second scale-down cooldown. Add real health endpoints first. Persistent SignalR clients also prevent scale-to-zero.

**Context.** Production always-on replicas cost roughly $18/month. Traffic is business-hours only.

**Decision.** `min_replicas = 0`, with a Container Apps Job pinging both apps every 10 minutes from 06:00–20:00 Mon–Sat.

**Why.** Container Apps bills idle replicas at roughly one-eighth the active vCPU rate and zero replicas at nothing. Keeping a replica warm only when users exist captures most of the saving without exposing users to cold starts.

**Why a Container Apps Job rather than a GitHub Actions schedule.** Actions cron is best-effort. Scheduled workflows routinely run 10–15 minutes late under platform load, and GitHub **automatically disables scheduled workflows after 60 days of repository inactivity**. A warm-up that exists specifically to hide cold starts cannot depend on a scheduler that silently stops. The Job runs inside the environment, costs pennies, and has no external dependency.

**Verified before choosing.** `GET /items/stream` was checked because a persistent SSE subscription would pin a replica open all day and eliminate the saving entirely. It returns `application/x-ndjson` — a finite bulk catalog response that completes and releases the replica. Had the frontend held an `EventSource` open, this decision would have been rejected.

**Costs.** Requests outside warm hours pay a cold start: roughly 2–4s for the API (image pull, .NET startup, EF model build) and 1–2s for SSR. Acceptable for an inventory application used during shop hours; revisit if usage spreads.

**Reversing.** One variable. Set `min_replicas = 1` and delete the job.

---

## 14. `ignore_changes` on the container image

**Context.** OpenTofu declares the Container App, including its image. The deploy pipeline updates that image on every release. Both want to own the same field.

**Decision.** Tofu creates the app with a placeholder image and declares `ignore_changes = [template[0].container[0].image]`.

**Why.** Without it the two systems fight. Deploy sets the image to `...@sha256:abc`. The next `tofu apply` — perhaps for an unrelated log-retention change — sees drift and reverts the app to the placeholder. Production silently rolls back to a quickstart image during a routine infrastructure change.

`ignore_changes` draws the ownership line explicitly: Tofu owns the app's shape (CPU, memory, scaling, ingress, identity, secrets), the deploy pipeline owns which image runs.

**Costs.** The image in the Tofu configuration is permanently fictional, which surprises readers. `tofu plan` will never show an image change, so image drift must be observed elsewhere. Mitigated by a comment at the `lifecycle` block.

**Alternative rejected.** Making Tofu own the image tag would mean every deployment is a `tofu apply` with a variable override, coupling release cadence to infrastructure state locking — a slow deploy that can be blocked by an unrelated infrastructure change.

---

## 15. Log Analytics retained, New Relic for backend APM only

> **Superseded by the 2026-07-25 audit.** The application already uses OpenTelemetry and OTLP. Keep that path; do not add the New Relic .NET agent.

**Context.** Container Apps can send logs to Log Analytics, to Azure Monitor, or nowhere. New Relic's free tier offers 100 GB/month.

**Decision.** Keep Log Analytics for both containers. Add the New Relic .NET agent to the API only.

**Why.** These solve different problems and the overlap is small.

Log Analytics gets container stdout/stderr with zero instrumentation — no agent, no licence key, no runtime dependency. Roughly $2/month. It answers "what did the process print before it died", which is what you need at 2am.

New Relic answers a question Log Analytics genuinely cannot: which database query made this request slow, and how do these 400 errors group. That is APM, and raw log search is a poor substitute.

**Note on cost.** New Relic was initially considered as a replacement to save the Log Analytics charge. Keeping both means it saves nothing — it is now purely an added capability. Worth being clear about: this buys distributed tracing, not a lower bill.

**Why the frontend gets no APM.** The SSR runtime is `oven/bun:latest`. The New Relic Node agent depends on `async_hooks` and `require` interception that Bun does not fully implement; the likely outcomes are silent non-instrumentation or a broken process. Not worth risking the frontend for. Logs still flow to Log Analytics, so crashes remain diagnosable. Revisit after a compatibility spike.

**Costs.** Roughly $2/month, a licence key in Key Vault, and a ~50 MB agent layer on the API image. Free-tier terms can change; because logs are in Log Analytics independently, losing New Relic degrades diagnostics rather than eliminating them.

---

## 16. The frontend must be a container

**Context.** Azure Static Web Apps is cheaper than Container Apps and includes a free CDN. Worth checking whether the Angular frontend qualifies.

**Decision.** It does not. Container.

**Why.** `src/frontend/angular.json` sets `outputMode: "server"` with `prerender: false`, and `src/frontend/Dockerfile` runs `bun run serve:ssr:INVENTORY` on port 4000. This is server-side rendering — a live process handling each request. Static Web Apps serves files from storage and has nowhere to run it.

Determined from configuration rather than assumed. Had `prerender` been true with `outputMode: "static"`, Static Web Apps would have been the better and cheaper choice.

**Consequence.** The frontend costs the same as the backend to run and participates in the same build, registry, scaling, and cold-start behaviour. Its `ngsw-config` service worker also means deployments must not break cached client assets — a versioning concern for the deploy pipeline, not for infrastructure.

**Reversing.** If SSR is ever dropped in favour of prerendering, revisit. The saving is real.

---

## 17. Migrations run from the runner behind a temporary firewall rule

> **Superseded for the production target by the 2026-07-25 audit.** Use a migration bundle in an in-environment Container Apps job for private PostgreSQL. A temporary runner firewall rule is only a public-network launch exception.

**Context.** EF Core migrations must apply before new application code starts.

**Options.**

- **From the GitHub runner** — `dotnet ef database update`, using the existing `ApplicationDbContextFactory`.
- **As a Container Apps Job** — runs inside Azure, reaches the database without firewall changes.
- **At application startup** — `Database.Migrate()` in `Program.cs`.

**Decision.** From the runner, adding and removing a firewall rule for the runner's egress IP.

**Why.** Startup migration is rejected outright: with `min_replicas` scaling, several replicas can start simultaneously and race on the same migration. EF's advisory locking mitigates but does not eliminate this, and a failed migration takes down every replica at once with no rollback path.

The runner approach keeps migrations visible in workflow logs, gated by the same reviewer as the deployment, and easy to run manually.

**The firewall detail matters.** Azure's "Allow public access from Azure services" does **not** cover GitHub-hosted runners — they are not Azure resources. GitHub's IP ranges are large and change, so a static allow-list is both permissive and fragile. Each run therefore adds a rule for its own detected egress IP, applies migrations, and removes the rule in an `always()` step so a failed migration does not leave the rule behind.

**Costs.** A brief window where one external IP can reach the server, gated by Entra authentication. Roughly 30 seconds of pipeline time. A failed cleanup leaves a stale rule — worth an occasional audit.

**Reversing.** The Container Apps Job alternative becomes necessary if the server moves to private networking, since the runner then has no route at all. Keep the migration step isolated to make that swap easy.

---

## 18. Separate runtime and migrator database identities

**Context.** The application needs to read and write data. Migrations need to alter schema. Both could use one principal.

**Decision.** Four principals — runtime and migrator, per environment.

**Why.** The runtime identity is the one exposed to the internet through request handling. If application code is ever coerced into executing unintended SQL, the blast radius is bounded by what that identity can do. A principal with `SELECT/INSERT/UPDATE/DELETE` can corrupt rows; a principal with DDL can drop tables, disable row-level security policies, or alter constraints.

The migrator identity is used only by a gated pipeline step, never by a running container, and never handles user input.

This is defence in depth rather than a response to a known flaw. It costs a few lines of `GRANT` and removes an entire class of escalation.

**Interaction with RLS.** Existing migrations use `FORCE ROW LEVEL SECURITY`, so policies apply even to the table owner. This matters specifically on Flexible Server, where there is no superuser and the migrator identity typically owns the tables it creates — under `ENABLE` alone, RLS would be silently bypassed for the owner. The existing choice of `FORCE` is correct and must be preserved in future migrations.

**Costs.** Two more identities per environment, and migrations must run under the right one. Ownership of newly created objects needs attention so the runtime identity retains access to tables the migrator creates — handled by `ALTER DEFAULT PRIVILEGES`.

---

## Decisions deliberately deferred

| Item | Why deferred | Revisit when |
|---|---|---|
| Frontend APM | Bun / New Relic agent compatibility unverified | After a compatibility spike |
| Multi-region / HA | Roughly doubles cost; no availability requirement stated | An uptime commitment exists |
| Private registry | Public repository makes public GHCR adequate today | Images or repository become private |
| Automated dev teardown | Separate dev server makes this useful but adds automation risk | Dev usage is low enough to justify it |
| Blue/green via revision traffic splitting | Container Apps supports it; adds pipeline complexity | Deployments start causing user-visible disruption |

---

## Open items

1. Choose whether the first production release may use the public PostgreSQL firewall exception or must start with the recommended private network. This changes the migration execution path and adds about $21.90–$25.55/month in Container Apps environment network resources.
2. Define the actual production business timezone and warm-hours service level before writing UTC cron expressions.
3. Decide whether the launch accepts one API replica or funds Azure SignalR/backplane work and the required application fixes.
4. The mobile base URL mechanism already exists through `String.fromEnvironment`; the release workflow still needs the production `--dart-define`.
5. Repository-level migration counts in older project guidance are stale; use the migration tree itself as the source of truth.
