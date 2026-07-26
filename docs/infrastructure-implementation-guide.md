# Infrastructure Implementation Guide

Step-by-step runbook for building the infrastructure described in [infrastructure-architecture.md](infrastructure-architecture.md). Rationale for every choice lives in [infrastructure-decisions.md](infrastructure-decisions.md).

Each step states what it does, why it exists, how to verify it worked, and how it typically fails. **Verify before moving on.** Several steps fail silently, and the symptom appears three phases later.

---

## Audited execution path (2026-07-25)

This section supersedes conflicting examples later in the original runbook. The platform design is viable, but the original sequence cannot be applied unchanged.

### Phase-by-phase validation

| Original phase | Verdict | Required correction | Provider cost at the step |
|---|---|---|---:|
| 1 Bootstrap | Valid with a missing role | Enable versioning/soft delete and grant blob **data-plane** access, not only management-plane access | `<$0.01/month` at this scale |
| 2 Remote state | Valid | Use Entra backend auth; verify a second process cannot acquire the state lock and that a prior blob version can be restored | included above |
| 3 Identities | Needs redesign | Split plan, gated infrastructure apply, routine deploy, runtime, and migrator. Do not give both environment identities Contributor over shared resources | no identity fee |
| 4 GitHub environments | Incomplete | Add plan identity variables, prod reviewer, concurrency controls, and deny fork plans from reading state | `$0` for this public repository |
| 5 Shared infrastructure | Incorrect as written | Prefer public GHCR; ~~use separate DB servers~~ **overridden: one shared server, see [decision §8](infrastructure-decisions.md#8-one-shared-postgresql-server-two-databases)**; add PostgreSQL `tenant_id`; ~~choose public-firewall exception or private production network~~ **locked: public** | DB `$16.09/month` lean; optional ACR `+$5.07` |
| 6 DNS | Operationally risky | **⏸ DEFERRED to ~2026-08-25.** When resumed: inventory/copy all DNS and DNSSEC records before delegation, or keep current authoritative DNS and add only app records | `$0` while deferred; Azure DNS `$0.50/month` + queries later |
| 7 Database bootstrap | Topology and network dependent | Bootstrap each server independently; create Entra principals, default privileges, cache table, and isolation/RLS tests. Run from a routed in-Azure job for private DB | no separate service fee |
| 8 Application changes | ✅ **Applied 2026-07-26** — see [status](#status--2026-07-26) | Complete every gate in [architecture §6](infrastructure-architecture.md#6-application-production-contract), including migrations, web routing, health, CORS/proxy, SignalR, OAuth state, cache, and secrets | engineering effort |
| 9 Secret values | Sequenced before the vault exists | First create vault/identity/RBAC; then add different per-env values. Never read values through an OpenTofu data source | Key Vault `<$0.10/month` expected |
| 10 Environment infrastructure | Incomplete HCL | Add ingress/target ports, complete configuration, versionless Key Vault URIs, probes, network path, web app, and migration job. API max replicas is 1 initially | Container Apps about `$10–34/month` prod at assumed hours |
| 11 Pipelines | Incomplete and unsafe | Build both images once, dev then prod sequentially; tests/scans; migration job; smoke tests; digest rollback. Routine deploy identity must not be a broad infra identity | Actions `$0` for standard public-repo runners |
| 12 Custom domains | **⏸ DEFERRED, blocked by Phase 6** | Manage binding/cert through IaC or explicitly manual commands, not both; verify `asuid`/CNAME and rollback | managed certificate `$0` |
| 13 Keep-warm | Does not work as stated | Cron is UTC, ten minutes exceeds the 300-second cooldown, Azure CLI image is oversized/mutable, and `/health` is absent | roughly `$0.03–$0.20/month` |
| 14 Verification | Missing production gates | Add tenant hub isolation, browser/API/hub routing, distributed OAuth, multi-replica behavior, cache privileges, backup restore, secrets/state scan, and cost alert checks | test/load usage only |

### Canonical order

Do not start environment provisioning until the application changes in step 7 have a reviewed implementation plan.

1. Choose the Azure subscription/region, public-versus-private PostgreSQL network, registry option, service hours/timezone, and launch replica cap. (Domain owner deferred with Phase 6.)
2. Bootstrap state storage; grant `Storage Blob Data Contributor` to apply and `Storage Blob Data Reader` to plan; migrate and test state.
3. Create separate, least-privilege OIDC identities. Pin GitHub actions and providers; reject forked state-reading plans.
4. Create GitHub `dev` and `prod` environments with required production review and concurrency locks.
5. Provision public GHCR integration or optional ACR and one shared PostgreSQL server with a database per environment. No VNet: the public-network path is locked in. **No DNS zone: deferred to ~2026-08-25**, so every subsequent phase runs against Container Apps `*.azurecontainerapps.io` hostnames.
6. Provision per-environment Key Vault, managed identities, Log Analytics, and minimum RBAC. Then add secret **values** out of band and reference versionless URIs without a secret data source.
7. Implement and test the application production contract:
   - remove `ApplyMigrationsAsync()` from API startup;
   - construct one periodically refreshed Entra-aware `NpgsqlDataSource` and use it for EF and PostgreSQL distributed cache;
   - create the cache table before runtime and set `CreateIfNotExists=false`;
   - add health endpoints/probes, production CORS, and trusted forwarded headers;
   - route relative `/api` and `/hubs` through the SSR web origin;
   - secure/group SignalR, distribute OAuth state, and make rate limiting atomic;
   - pin/run containers as non-root and standardise API port `8080`;
   - rotate the committed SMTP credential and scrub repository history.
8. Bootstrap DB principals and default privileges; verify dev cannot connect to prod, RLS survives pooling, and runtime cannot execute DDL.
9. Create a self-contained migration bundle/image and a Container Apps job using the migrator identity.
10. Build and test **both** images once, produce SBOM/scan results, push immutable digests, and record them.
11. Run dev migration; deploy dev; execute readiness, SSR/API, authentication, tenant isolation, SignalR, mobile URL, and external-service smoke tests.
12. Only after dev succeeds, pass the prod reviewer gate; run prod migration; deploy the same digests; smoke test and retain the prior revision/digests.
13. **(Domains/certificates deferred to ~2026-08-25.)** Enable alerts/budgets/log caps, run backup restore and rollback drills, then enable the corrected keep-warm schedule if required.

### Cost checkpoint

> **These figures are East US list prices and this deployment does not run there.** East US is offer-restricted for this subscription; the servers run in `centralindia`. Two further deviations lower the database line below every scenario here: one shared server instead of two, and a single B1ms. Re-price against Central India before using any total as a budget.

The audited East US monthly scenarios are:

- **Lean controlled launch:** about **$43.40**—two B1ms + 32 GB servers, public GHCR, API max one, mostly-idle business-hours production, light dev, DNS/Key Vault/state, and less than 5 GB logs.
- **Lean with ACR:** about **$48.50**.
- **Scale-ready with public DB networking:** about **$130**, including prod B2s, dev B1ms, and Azure SignalR Standard.
- **Scale-ready with private production network:** about **$152–156**.

These totals exclude tax, egress, domain registration, and third-party email/API services. CPU-active business hours add about $23.81/month versus the mostly-idle example; 10 GB total Log Analytics ingest adds $11.50. The detailed arithmetic, usage drivers, one-time labour range, and official sources are in [architecture §10](infrastructure-architecture.md#10-cost-model).

---

## Before you start

### Lock the open decisions

Five questions must be answered before Phase 3, and several are expensive to change afterwards.

| Question | Impact if deferred |
|---|---|
| ~~Public-firewall exception or private PostgreSQL?~~ | **Locked: public with a narrow firewall allowlist.** Keeps the lean scenario near $43/month instead of ~$152. Accepted because Entra-only auth means no password exists to guess; the compensating controls are in 5.2 |
| ~~Public GHCR or optional ACR?~~ | **Locked: public GHCR.** No registry RBAC, no `registry` block on the Container App |
| One launch replica or scale-ready? | Scale-out requires SignalR, OAuth-state, and rate-limiter application work |
| ~~Which domain, and is the registrar accessible?~~ | **⏸ Deferred to ~2026-08-25.** Phases 6 and 12 are parked; everything else runs on Container Apps hostnames |
| What timezone and warm-hours service level? | Container Apps cron is UTC and determines availability/cost |

### Access required

The person running the bootstrap needs, on the target subscription:

- **Owner**, or **Contributor + User Access Administrator**. Contributor alone is not enough — creating role assignments is denied to Contributor, and the bootstrap creates several.
- No Entra directory permissions are needed. The design uses user-assigned managed identities rather than app registrations, which are ordinary Azure resources. This is a deliberate benefit: in most organisations, creating app registrations requires a directory role that is harder to obtain than subscription access.

On GitHub: **admin** on `chanakya-net/intelibill`, to create environments and set reviewers.

### Tooling

```bash
brew install opentofu azure-cli gh
```

Verify, and confirm you are pointed at the right subscription — everything downstream inherits it:

```bash
tofu version && az version && gh auth status
az login
az account show --query "{name:name, id:id, tenant:tenantId}" -o table
```

Record the subscription ID and tenant ID. Both are needed in Phase 5.

### Confirm the region before Phase 1

**Do this first.** Azure restricts PostgreSQL Flexible Server provisioning per subscription and per region, independently of what the region "supports". On this subscription both `southindia` **and** `eastus` are restricted, and the failure does not appear until Phase 5 — after resource groups, identities, and state containers have all been created against the wrong location.

`az postgres flexible-server list-skus -l southindia` returns success on a restricted region. It reads a catalog, not your entitlement. **Do not use it as the check.** Use the subscription-scoped capabilities API:

```bash
SUB=$(az account show --query id -o tsv)
for LOC in centralindia southeastasia eastus southindia; do
  printf "%-16s " "$LOC"
  az rest --method get --url "https://management.azure.com/subscriptions/$SUB/providers/Microsoft.DBforPostgreSQL/locations/$LOC/capabilities?api-version=2024-08-01" \
    --query "value[0].reason" -o tsv 2>&1 | head -1
done
```

An empty `reason` means the region is open. Anything else is the restriction message, and the fix is a support request under "Service and subscription limits" — or a different region.

Confirm the same region also carries what later phases need, so this is discovered once rather than again in Phase 10:

```bash
for NS in Microsoft.App Microsoft.KeyVault Microsoft.OperationalInsights; do
  az provider show -n "$NS" --query "resourceTypes[?resourceType=='containerApps' || resourceType=='vaults' || resourceType=='workspaces'].locations[]" -o tsv
done
```

Resource group location is only metadata, so groups created in one region can hold resources in another — a wrong group location is survivable. A wrong **database** region is not: `prevent_destroy` on the server means moving it later is a manual state operation plus a data migration.

### Effort

| Phase | What | Time |
|---|---|---|
| 1–4 | Bootstrap, state, and GitHub wiring | 1 day |
| 5–7, 10 | IaC, identities, and networking | 2–4 days |
| 8 | **Application production changes** | 5–10 days |
| 11 | Pipelines and migration job | 2–4 days |
| 12–14 | Security, load, restore, and runbooks | 2–4 days |

Plan **12–23 engineer-days**, not one week. At an illustrative $50–$150/hour this is about $4,800–$27,600 one time; it is a labour assumption rather than a provider charge.

---

## Phase 1 — Bootstrap

**Goal.** Create the resource group and storage account that will hold OpenTofu state.

**Why it is manual.** The storage account holding the state cannot be described by the state it holds. This is the one unavoidable chicken-and-egg in the design; every later step is automated.

### 1.1 Create the bootstrap configuration

`.tofu/bootstrap/main.tf`, using **local state deliberately** — there is nowhere remote to put it yet:

```hcl
terraform {
  required_providers {
    azurerm = { source = "hashicorp/azurerm", version = "~> 4.0" }
  }
}

provider "azurerm" {
  features {}
  subscription_id = var.subscription_id

  # The state account disables shared keys, so every data-plane call
  # (blob availability poll, container ops) must use Entra ID instead.
  storage_use_azuread = true
}

resource "azurerm_resource_group" "shared" {
  name     = var.resource_group_name
  location = var.location
}

resource "azurerm_storage_account" "tfstate" {
  name                     = var.state_storage_account_name
  resource_group_name      = azurerm_resource_group.shared.name
  location                 = azurerm_resource_group.shared.location
  account_tier             = "Standard"
  account_replication_type = "LRS"
  min_tls_version          = "TLS1_2"

  # State files are the crown jewels: they describe every resource you own.
  shared_access_key_enabled       = false
  allow_nested_items_to_be_public = false

  blob_properties {
    versioning_enabled = true
    delete_retention_policy { days = 30 }
  }
}

resource "azurerm_storage_container" "tfstate" {
  name                  = "tfstate"
  storage_account_id    = azurerm_storage_account.tfstate.id
  container_access_type = "private"
}
```

`versioning_enabled` matters more than it looks. A corrupted or truncated state file is one of the few genuinely unrecoverable OpenTofu failures — versioning turns it into a restore.

`shared_access_key_enabled = false` forces Entra authentication to the state account. Access keys are long-lived shared secrets, which is the thing this whole design avoids.

`storage_use_azuread = true` on the provider is the other half of that switch. Without it the provider still reaches for an account key on data-plane calls — including the container create in this very file — and the apply fails with a key-retrieval error that reads as a permissions problem.

Phase 3 adds one state container per layer to this file. If you have already applied Phase 1, they arrive with the Phase 3 apply; nothing here needs re-running.

### 1.2 Apply

```bash
cd .tofu/bootstrap && tofu init && tofu apply
```

**Storage account names are globally unique across all of Azure**, 3–24 characters, lowercase alphanumeric only. Pick something unlikely, such as `intelibilltfstate01`.

### 1.3 Verify

```bash
az storage container show --name tfstate \
  --account-name <your-account> --auth-mode login -o table
```

**If it fails:** `AuthorizationPermissionMismatch` means you have control-plane rights on the account but not data-plane rights on its contents. They are separate in Azure. Grant yourself `Storage Blob Data Contributor` on the account and retry — propagation takes a minute or two.

---

## Phase 2 — Move state to Azure

**Goal.** Get state out of a local file and into the storage account.

**Why.** Local state cannot be shared with CI, has no locking, and disappears with the laptop. Two people applying against separate local state files will silently create duplicate infrastructure.

### 2.1 Add the backend

`.tofu/bootstrap/backend.tf`:

```hcl
terraform {
  backend "azurerm" {
    resource_group_name  = "intelibill-shared"
    storage_account_name = "<your-account>"
    container_name       = "tfstate"
    key                  = "bootstrap.tfstate"
    use_azuread_auth     = true
  }
}
```

`use_azuread_auth = true` pairs with `shared_access_key_enabled = false` from Phase 1. Without it, the backend tries key-based auth and fails.

### 2.2 Migrate

```bash
tofu init -migrate-state
```

Answer `yes`. Then confirm the local file is no longer authoritative:

```bash
tofu plan   # must report no changes
```

### 2.3 Verify and clean up

```bash
az storage blob list --container-name tfstate \
  --account-name <your-account> --auth-mode login -o table
```

Once the blob exists and `plan` is clean, delete `terraform.tfstate` and `terraform.tfstate.backup` locally. Confirm `.gitignore` covers `*.tfstate*` — committing state would publish every resource ID and any secret Tofu has read.

**If it fails:** a `plan` showing changes after migration usually means the backend `key` differs from what was migrated, so Tofu is reading an empty state and proposing to create everything. **Do not apply.** Fix the key and re-init.

---

## Phase 3 — Identities and federated credentials

**Goal.** Create the managed identities GitHub Actions will assume, with no stored secret.

**Why this shape.** See [decision §3](infrastructure-decisions.md#3-github-oidc-federated-identity-instead-of-stored-credentials). The critical property is that the federated subject encodes the GitHub Environment, so a workflow that has not passed the prod reviewer gate cannot obtain a prod token.

### 3.1 Resource groups and identities

Four identities in three families. The split is by **duty**, not by environment: the routine deploy that runs on every merge must not be as powerful as the gated infrastructure apply, which is the audit's finding in line 19.

> **Everything lives in one resource group** (`intelibill-shared`). That is a deliberate simplification for operability, and it costs the per-environment Azure boundary: creating resources requires write at group scope, and "create" cannot be scoped to resources that do not exist yet. Per-environment apply identities would therefore hold identical Contributor rights over the same group — a boundary in name only — so there is one apply identity, honestly scoped.

| Identity | Reached by | Holds |
|---|---|---|
| `plan` | any pull request | Reader on the group, blob data Reader on all state |
| `infra_apply` | `dev`, `prod`, or `shared` environment | Contributor + RBAC admin on the group, blob data Contributor on all state |
| `deploy[dev\|prod]` | `dev`/`prod` environment | update a Container App, start a job |

**What still separates dev from prod**, with the Azure layer gone:

- the **reviewer gate** on the `prod` and `shared` GitHub environments — the only thing standing between a merge and production;
- the **database grants** from Phase 7.4, now the sole barrier to production data;
- the **deploy identities**, once Phase 10.2 re-scopes them to individual Container App resources.

What no longer separates them: resource-group RBAC, and state containers. One apply identity needs write on all three containers, so a dev apply can technically reach prod state. The containers stay separate to stop a mistaken backend `key` landing in the wrong layer — an operability guard now, not a security one.

New file `.tofu/bootstrap/identities.tf` — Tofu merges every `.tf` in the directory, so `main.tf` stays limited to state bootstrap:

```hcl
locals {
  # Deploy stays per environment: it only updates existing resources, so Phase
  # 10.2 can scope each one to its own Container App.
  environments = toset(["dev", "prod"])

  # Every layer's apply reaches the same identity.
  apply_environments = toset(["dev", "prod", "shared"])

  state_layers = toset(["shared", "dev", "prod"])

  oidc_issuer   = "https://token.actions.githubusercontent.com"
  oidc_audience = ["api://AzureADTokenExchange"]
}

resource "azurerm_user_assigned_identity" "plan" {
  name                = "id-gha-plan"
  resource_group_name = azurerm_resource_group.shared.name
  location            = var.location
}

resource "azurerm_federated_identity_credential" "plan" {
  name      = "gha-plan"
  parent_id = azurerm_user_assigned_identity.plan.id
  audience  = local.oidc_audience
  issuer    = local.oidc_issuer
  subject   = "repo:${var.github_repository}:pull_request"
}

resource "azurerm_user_assigned_identity" "infra_apply" {
  name                = "id-gha-infra-apply"
  resource_group_name = azurerm_resource_group.shared.name
  location            = var.location
}

# One credential per environment, all resolving to the same identity: the gate
# differs per environment, the rights do not.
resource "azurerm_federated_identity_credential" "infra_apply" {
  for_each  = local.apply_environments
  name      = "gha-infra-apply-${each.key}"
  parent_id = azurerm_user_assigned_identity.infra_apply.id
  audience  = local.oidc_audience
  issuer    = local.oidc_issuer
  subject   = "repo:${var.github_repository}:environment:${each.key}"
}

resource "azurerm_user_assigned_identity" "deploy" {
  for_each            = local.environments
  name                = "id-gha-deploy-${each.key}"
  resource_group_name = azurerm_resource_group.shared.name
  location            = var.location
}

resource "azurerm_federated_identity_credential" "deploy" {
  for_each  = local.environments
  name      = "gha-deploy-${each.key}"
  parent_id = azurerm_user_assigned_identity.deploy[each.key].id
  audience  = local.oidc_audience
  issuer    = local.oidc_issuer
  subject   = "repo:${var.github_repository}:environment:${each.key}"
}
```

The `subject` string must match GitHub's claim **exactly** — no wildcards, no trailing slash. `environment:prod` only matches a job declaring `environment: prod`, which is what makes the reviewer gate load-bearing rather than decorative.

`infra_apply` and `deploy[env]` share the `dev` and `prod` subjects. That is legal: subjects are unique per identity, not globally, and the workflow selects between them by client ID. The gate they share is the environment; the difference between them is what each is allowed to do once through it — which is now the *only* difference the Azure layer enforces.

One identity carrying three credentials is not the same as three identities. A prod-gated run and a dev-gated run receive tokens for the same principal with the same rights; what differs is that the prod-gated run required an approval to start.

Do not add `resource_group_name` to `azurerm_federated_identity_credential` — the provider deprecated it and ignores it. Older examples still carry it.

Pull requests from forks must never reach the `plan` identity. A plan cannot mutate Azure, but it reads state, and state describes every resource you own — including prod, because a PR can touch any layer.

What actually stops a fork is that GitHub caps fork-PR workflows at read permissions, so `id-token: write` cannot be granted and no OIDC token can be minted. The client ID being public does not matter. Verify the surrounding posture anyway:

```bash
gh api repos/chanakya-net/intelibill/actions/permissions/workflow
gh api repos/chanakya-net/intelibill/actions/permissions/fork-pr-contributor-approval
grep -rl pull_request_target .github/
```

Want `default_workflow_permissions: read`, no `pull_request_target` hits, and an approval policy of `all_external_contributors` — the default `first_time_contributors` lets a returning outside contributor's fork PR run CI unreviewed:

```bash
gh api -X PUT repos/chanakya-net/intelibill/actions/permissions/fork-pr-contributor-approval \
  -f approval_policy=all_external_contributors
```

**One state container per layer.** Azure RBAC cannot scope blob data access below a container, so a single `tfstate` container would let the `dev` apply identity rewrite `prod` state — the same over-broad-grant failure the audit flagged for shared resources. Add to `main.tf`, alongside the bootstrap container from Phase 1:

```hcl
resource "azurerm_storage_container" "state" {
  for_each              = local.state_layers
  name                  = "tfstate-${each.key}"
  storage_account_id    = azurerm_storage_account.tfstate.id
  container_access_type = "private"
}
```

Bootstrap's own state stays in the original `tfstate` container, so this is purely additive — no backend migration.

`var.github_repository` is `owner/name`, validated in `variables.tf`. Hard-coding the repository into every subject string means a fork or rename silently produces credentials that match nothing.

### 3.2 Role assignments — the step that usually fails

The original example gave each environment identity `Contributor` over the shared resource group and permanent RBAC Administrator in its environment. With a single resource group there is nowhere narrower to put the apply identity, so the honest form is one grant, stated plainly, rather than several that look narrow and are not.

New file `.tofu/bootstrap/role-assignments.tf`:

```hcl
data "azurerm_subscription" "current" {}

# One resource group means this is the whole estate. Creating resources requires
# write at group scope, so this cannot be narrowed further without splitting the
# group back apart.
resource "azurerm_role_assignment" "infra_apply" {
  scope                = azurerm_resource_group.shared.id
  role_definition_name = "Contributor"
  principal_id         = azurerm_user_assigned_identity.infra_apply.principal_id
}

# Needed because the environment layers create their own role assignments
# (app identity to Key Vault, migrator to the job).
resource "azurerm_role_assignment" "infra_apply_rbac" {
  scope                = azurerm_resource_group.shared.id
  role_definition_name = "Role Based Access Control Administrator"
  principal_id         = azurerm_user_assigned_identity.infra_apply.principal_id
}

# One identity applies all three layers, so it needs write on all three
# containers. They stay separate to keep a mistaken backend `key` out of the
# wrong layer — an operability guard, not an isolation one.
resource "azurerm_role_assignment" "infra_apply_state" {
  for_each             = local.state_layers
  scope                = azurerm_storage_container.state[each.key].id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = azurerm_user_assigned_identity.infra_apply.principal_id
}

resource "azurerm_role_assignment" "plan_reader" {
  scope                = azurerm_resource_group.shared.id
  role_definition_name = "Reader"
  principal_id         = azurerm_user_assigned_identity.plan.principal_id
}

resource "azurerm_role_assignment" "plan_state" {
  for_each             = local.state_layers
  scope                = azurerm_storage_container.state[each.key].id
  role_definition_name = "Storage Blob Data Reader"
  principal_id         = azurerm_user_assigned_identity.plan.principal_id
}
```

Scope the blob assignments to the **container**, never the storage account. Account scope would additionally hand over bootstrap's own state container, which holds the identities themselves.

Use `.id` on the container, not `.resource_manager_id`, which the provider deprecated for removal in 5.0. Also avoid `for_each` over the whole `azurerm_storage_container.state` resource — that evaluates deprecated attributes and produces warnings; iterate `local.state_layers` instead.

`RBAC Administrator` at group scope, held by an identity that any merge can reach through the `dev` gate, is the sharpest edge in this design. It can grant itself anything within the group. Accepting it is the price of a single group plus environment layers that manage their own role assignments; the mitigation is the reviewer gate and reviewing `infra-apply.yml` changes as security-relevant.

**The deploy identity needs a custom role.** No built-in role covers "update this app's image and start its migration job" without also granting delete, secret listing, or environment-wide control:

```hcl
resource "azurerm_role_definition" "container_app_deployer" {
  name        = "Intelibill Container App Deployer"
  scope       = data.azurerm_subscription.current.id
  description = "Update Container Apps and start Container Apps jobs."

  permissions {
    actions = [
      "Microsoft.App/containerApps/read",
      "Microsoft.App/containerApps/write",
      "Microsoft.App/containerApps/revisions/read",
      "Microsoft.App/containerApps/revisions/activate/action",
      "Microsoft.App/containerApps/revisions/deactivate/action",
      "Microsoft.App/jobs/read",
      "Microsoft.App/jobs/start/action",
      "Microsoft.App/jobs/executions/read",
      "Microsoft.Resources/subscriptions/resourceGroups/read",
    ]
    not_actions = []
  }

  assignable_scopes = [data.azurerm_subscription.current.id]
}

# TEMPORARY SCOPE. Group scope means the dev deploy identity can currently
# update production's Container App, because both live in this one group.
# Phase 10.2 re-assigns each deploy identity at its own app resource and this
# block is removed.
resource "azurerm_role_assignment" "deploy" {
  for_each           = local.environments
  scope              = azurerm_resource_group.shared.id
  role_definition_id = azurerm_role_definition.container_app_deployer.role_definition_resource_id
  principal_id       = azurerm_user_assigned_identity.deploy[each.key].principal_id
}
```

Assigned at resource-group scope only because the apps do not exist yet. **This is the one grant that is deliberately temporary**: Phase 10.2 narrows it to individual app resources and deletes this block, which is what restores the dev/prod deploy boundary after the single-group change. Leaving it in place indefinitely means the two deploy identities are interchangeable.

`listSecrets` is withheld on purpose. The deploy job never needs the app's configured secrets, and being able to read them would defeat the point of Key Vault.

`Microsoft.ManagedIdentity/userAssignedIdentities/assign/action` is also withheld, and this is the one likely surprise in Phase 11. If `az containerapp update` fails with an assign-action error, the fix is to grant that action scoped to the single app UAMI resource — **not** at resource-group scope, which would let the deploy identity attach any identity in the group to an app it controls and escalate through it.

**Registry.** On public GHCR there are no registry role assignments at all: push uses the job-scoped `GITHUB_TOKEN`, pulls are anonymous, and the Container App needs no `registry` block. Only if ACR is chosen does `AcrPush` belong on the deploy identities and `AcrPull` on the app identity.

`Contributor` is explicitly denied `Microsoft.Authorization/roleAssignments/write`, while `Reader`/`Contributor` do not provide blob data access. Both errors can look like a generic `AuthorizationFailed`, so verify each scope explicitly.

The role name must be exactly `Role Based Access Control Administrator` — Tofu resolves `role_definition_name` by exact match and fails with a confusing "role not found" if it is abbreviated. If your organisation restricts that role, `User Access Administrator` is the broader equivalent.

### 3.3 Verify

Expose the identifiers Phase 4 needs from `.tofu/bootstrap/outputs.tf`: `plan_client_id`, `infra_apply_client_id`, the `deploy_client_ids` map, a `principal_ids` map keyed by purpose, `resource_group_name`, and `state_containers`.

```bash
tofu apply
tofu output -json | jq

for K in infra_apply deploy_prod deploy_dev plan; do
  echo "=== $K ==="
  az role assignment list \
    --assignee "$(tofu output -json principal_ids | jq -r ".$K")" \
    --all --query "[].{Role:roleDefinitionName, Scope:scope}" -o tsv
done
```

Expect exactly the documented duties:

- `infra_apply`: `Contributor` + RBAC admin on `intelibill-shared`, blob data Contributor on all three state containers. This is broad by construction — one group, one applier.
- `deploy_dev` / `deploy_prod`: the custom deployer role, and nothing else. **No `Contributor` anywhere, no blob data role at all.** These two negatives are the boundary that survived the single-group change, so check them rather than assuming.
- `plan`: `Reader` on the group and blob data Reader on all three containers. No write anywhere.

Note the deploy assignments are currently at **group** scope, which means `deploy_dev` can update production's Container App. Phase 10.2 narrows them to individual app resources once those exist; until then, treat the two deploy identities as equivalent in reach and rely on the reviewer gate.

The `--all` flag matters: without it the query is scoped to the current subscription context and can silently omit assignments, which reads as a missing grant.

---

## Phase 4 — GitHub environments and variables

**Goal.** Create the `dev`, `prod`, and `shared` environments, set the reviewer gates, and publish the non-secret identifiers.

**Why.** The reviewer gate is not decoration. It is the mechanism that makes the prod federated credential unreachable without approval — see [decision §3](infrastructure-decisions.md#3-github-oidc-federated-identity-instead-of-stored-credentials).

### 4.1 Create environments

Three, not two. `shared` gates the layer that owns the state account, the DNS zone, and the database servers, so those changes need their own approval instead of riding along with a production app deploy:

```bash
gh api -X PUT repos/chanakya-net/intelibill/environments/dev
gh api -X PUT repos/chanakya-net/intelibill/environments/prod
gh api -X PUT repos/chanakya-net/intelibill/environments/shared
```

### 4.2 Add the reviewer gate to prod and shared

Your GitHub user ID:

```bash
gh api user --jq .id
```

Then, for both gated environments:

```bash
for ENV in prod shared; do
  gh api -X PUT "repos/chanakya-net/intelibill/environments/$ENV" \
    -F "reviewers[][type]=User" \
    -F "reviewers[][id]=<your-user-id>" \
    -F "deployment_branch_policy[protected_branches]=true" \
    -F "deployment_branch_policy[custom_branch_policies]=false"
done
```

`protected_branches: true` is as important as the reviewer. Without it, a workflow on any branch can request the prod environment. With it, only protected branches can.

**Protect `main` before running this.** With `protected_branches: true` and no protected branches in the repository, *no* branch qualifies and nothing can ever request the environment. The failure surfaces much later, at the first deploy, and reads like an OIDC or environment-name problem:

```bash
gh api repos/chanakya-net/intelibill/branches/main/protection   # 404 = not protected
gh api -X PUT repos/chanakya-net/intelibill/branches/main/protection --input - <<'JSON'
{"required_status_checks":null,"enforce_admins":false,"required_pull_request_reviews":null,"restrictions":null}
JSON
```

That is the minimum protection record needed to satisfy the branch policy. Tighten it with required checks and reviews separately — as a branch-protection decision, not an infrastructure one.

Two more fields on the environment, both about who can skip the gate:

- `can_admins_bypass` defaults to `true`, which lets a repository admin deploy without approval and makes the reviewer advisory. Set it to `false`.
- `prevent_self_review` should stay `false` on a single-maintainer repository. With one reviewer who is also the person deploying, enabling it means nobody can ever approve and the gate deadlocks.

`dev` stays ungated, which is the point of having it.

### 4.3 Set variables — not secrets

`AZURE_CLIENT_ID_INFRA` is now the **same value in all three environments** — one apply identity — while `AZURE_CLIENT_ID_DEPLOY` differs per environment. The plan identity is repository-scoped: pull request plans do not enter an environment, so an environment variable would be invisible to them.

```bash
SUB=$(az account show --query id -o tsv)
TENANT=$(az account show --query tenantId -o tsv)
cd .tofu/bootstrap

INFRA=$(tofu output -raw infra_apply_client_id)
DEPLOY=$(tofu output -json deploy_client_ids)

for ENV in dev prod shared; do
  gh variable set AZURE_SUBSCRIPTION_ID --env "$ENV" --body "$SUB"
  gh variable set AZURE_TENANT_ID       --env "$ENV" --body "$TENANT"
  gh variable set AZURE_CLIENT_ID_INFRA --env "$ENV" --body "$INFRA"
done

# Deploy exists for the two application environments only; the shared layer has
# nothing to deploy.
for ENV in dev prod; do
  gh variable set AZURE_CLIENT_ID_DEPLOY --env "$ENV" --body "$(jq -r ".$ENV" <<<"$DEPLOY")"
done

# Plan runs on pull requests, outside every environment.
gh variable set AZURE_SUBSCRIPTION_ID --body "$SUB"
gh variable set AZURE_TENANT_ID       --body "$TENANT"
gh variable set AZURE_CLIENT_ID_PLAN  --body "$(tofu output -raw plan_client_id)"
```

Keeping `AZURE_CLIENT_ID_INFRA` as a per-environment variable, even though the value repeats, means splitting the identity back apart later is a variable change rather than a workflow change.

**Variables, not secrets.** These are identifiers, not credentials. Storing them as secrets masks them in logs, which makes every OIDC failure significantly harder to debug — you will see `***` where you need to compare a client ID.

**Do not publish a bare `AZURE_CLIENT_ID`.** A workflow that picks up the wrong identity from an ambiguous name is the failure this naming exists to prevent: the deploy job would silently run with infrastructure rights and nothing would look broken until it did something irreversible.

### 4.4 Verify

```bash
gh variable list --env prod
gh variable list --env shared
gh variable list
gh api repos/chanakya-net/intelibill/environments/prod   --jq '{rules:.protection_rules, bypass:.can_admins_bypass, branches:.deployment_branch_policy}'
gh api repos/chanakya-net/intelibill/environments/shared --jq '{rules:.protection_rules, bypass:.can_admins_bypass, branches:.deployment_branch_policy}'
```

Both gated environments must show a `required_reviewers` rule, `can_admins_bypass: false`, and a non-null `deployment_branch_policy`. An empty `protection_rules` array means the gate silently did not apply, and every federated credential behind it is then reachable from any run.

### 4.5 Prove OIDC works before building on it

Do not skip this. A throwaway workflow now saves hours later:

```yaml
name: OIDC smoke test
on: workflow_dispatch
permissions:
  id-token: write
  contents: read
jobs:
  infra:
    runs-on: ubuntu-latest
    environment: dev
    steps:
      - uses: azure/login@v2
        with:
          client-id: ${{ vars.AZURE_CLIENT_ID_INFRA }}
          tenant-id: ${{ vars.AZURE_TENANT_ID }}
          subscription-id: ${{ vars.AZURE_SUBSCRIPTION_ID }}
      # A group-scoped identity cannot list at subscription scope. Show the one
      # group it owns instead, or a working setup looks like a failure.
      - run: az group show -n intelibill-shared -o table
      - run: |
          az storage blob list --container-name tfstate-dev \
            --account-name <your-account> --auth-mode login -o table

  deploy_is_confined:
    runs-on: ubuntu-latest
    environment: dev
    steps:
      - uses: azure/login@v2
        with:
          client-id: ${{ vars.AZURE_CLIENT_ID_DEPLOY }}
          tenant-id: ${{ vars.AZURE_TENANT_ID }}
          subscription-id: ${{ vars.AZURE_SUBSCRIPTION_ID }}
      # This must FAIL. The deploy identity holds no blob data rights, and with
      # the resource-group boundary gone this is the main separation left to test.
      - run: |
          if az storage blob list --container-name tfstate-dev \
               --account-name <your-account> --auth-mode login -o none 2>/dev/null; then
            echo "::error::deploy identity can read state; the split is not holding"
            exit 1
          fi
      # Must also FAIL: deploy updates existing resources, it never creates them.
      - run: |
          if az group create -n intelibill-smoke-should-fail -l centralindia -o none 2>/dev/null; then
            echo "::error::deploy identity created a resource group"
            az group delete -n intelibill-smoke-should-fail --yes --no-wait || true
            exit 1
          fi
```

**Do not assert that the apply identity cannot read another environment's state.** With one apply identity that assertion is false by design, and a smoke test encoding the old topology fails for the wrong reason — which teaches you to ignore it. If you split the identity back apart, add the assertion back at the same time.

Run the same pair against `prod` once the reviewer gate is in place, and confirm the run pauses for approval before the login step executes. That pause is now the primary dev/prod boundary at the Azure layer, so if it does not pause, stop and fix the gate before anything else.

**If it fails:** `AADSTS70021: No matching federated identity record found` means the subject string does not match. Print the actual claim by decoding the token payload, and compare character by character against the `subject` in Phase 3.1 — including the environment segment, which is where a `shared`-versus-`prod` mix-up shows up. `Missing id-token permission` means the `permissions` block is absent — it is required per workflow *or* per job, and a job-level block overrides the workflow-level one entirely. `AuthorizationPermissionMismatch` on the blob step is data-plane, not control-plane: check the container-scoped assignment from Phase 3.2.

---

## Phase 5 — Modules and shared infrastructure

**Goal.** Write the reusable modules and provision one shared PostgreSQL server holding a database per environment. The DNS zone is deferred (Phase 6). No container registry — public GHCR needs no Azure resource.

### 5.1 Layout

```
.tofu/
  bootstrap/                        # state account + the six OIDC identities
  modules/{database,dns,container-app}/
  envs/{shared,dev,prod}/
```

No `registry` module: this repository publishes to public GHCR. No `github-identity` module either — the identities live in `bootstrap`, because the thing that grants access to a state container cannot be provisioned by a layer that needs that container to run.

Per-environment directories, not workspaces — [decision §11](infrastructure-decisions.md#11-per-environment-directories-not-tofu-workspace).

Each `envs/*/backend.tf` points at **its own container** from Phase 3 — `tfstate-shared`, `tfstate-dev`, `tfstate-prod` — with a `key` inside it:

```hcl
terraform {
  backend "azurerm" {
    resource_group_name  = "intelibill-shared"
    storage_account_name = "<your-account>"
    container_name       = "tfstate-dev"
    key                  = "dev.tfstate"
    use_azuread_auth     = true
  }
}
```

Separate containers are what make the isolation real: each apply identity holds blob data Contributor on one container only, so a wrong `key` can no longer reach another environment's state. **Reusing a container *and* key across environments makes one environment overwrite the other's state.** Recovery is possible via blob versioning (Phase 1), which is why versioning was enabled.

`use_azuread_auth = true` on every one of them, for the same reason as Phase 2.1.

### 5.2 PostgreSQL servers

**One server in the shared resource group, one database per environment.** Chosen for cost: a single B1ms is roughly half the bill of a server per environment. The audit recommended separate servers and this overrides it — see [decision §8](infrastructure-decisions.md#8-one-shared-postgresql-server-two-databases) for what is being traded away.

Read that trade honestly, because it changes where your isolation lives:

| | Separate servers | One shared server |
|---|---|---|
| dev reaching prod data | impossible, no route | prevented only by `REVOKE CONNECT` |
| Point-in-time restore | per environment | server-wide; recovering one database means restore-to-new-server then dump/restore |
| CPU credits, IOPS, memory | independent | shared; a dev migration spends production's burst credits |
| Maintenance restarts | independent | hit both environments |
| Backup retention | 7 dev / 14 prod | one value for both |

The practical consequence: **Phase 7.4 is no longer defence in depth, it is the boundary**, and Phase 7.5 is the only thing that proves the boundary exists. Re-run 7.5 after every grant change.

The server lives in `intelibill-shared` along with everything else. Note what that means with a single apply identity: an apply triggered through the `dev` gate holds Contributor over the database server too. The `shared` reviewer gate protects the *shared layer's state*, not the server resource, so `prevent_destroy` and the grants in Phase 7.4 are doing more work here than the RBAC is.

`modules/database` is instantiated once from `envs/shared`:

```hcl
module "database" {
  source = "../../modules/database"

  resource_group_name   = data.azurerm_resource_group.shared.name
  location              = var.location
  tenant_id             = var.tenant_id
  databases             = var.databases # ["intelibill_dev", "intelibill_prod"]
  postgres_sku          = var.postgres_sku
  storage_mb            = var.storage_mb
  backup_retention_days = var.backup_retention_days # 14: production governs both
  allowed_ip_rules      = var.allowed_ip_rules
}
```

Database names are distinct because they share a server, so the application's database name becomes per-environment configuration rather than the constant `intelibill`. Storage and the firewall rule list are likewise shared: an allowlisted address reaches both databases, and 32 GB is the total across both.

The shared resource group is read with a `data` block, not declared. Bootstrap owns it; two layers owning one resource is how a `tofu destroy` in the wrong directory takes out the state account.

The module body:

```hcl
resource "azurerm_postgresql_flexible_server" "main" {
  name                = "intelibill-pg-${var.name_serial}"
  resource_group_name = var.resource_group_name
  location            = var.location
  version             = var.postgres_version # "17"

  # Controlled-launch default. Resize after measuring CPU credits, connection
  # usage and latency; do not use this as an HA promise. Both environments share
  # this compute, so dev load spends production's burst credits.
  sku_name   = var.postgres_sku # default "B_Standard_B1ms"
  storage_mb = var.storage_mb   # shared by both databases

  backup_retention_days        = var.backup_retention_days # per server, not per database
  geo_redundant_backup_enabled = false

  # Public-network launch exception: reachability is controlled entirely by the
  # firewall rules, and there is no password to brute-force.
  public_network_access_enabled = true

  authentication {
    active_directory_auth_enabled = true
    password_auth_enabled         = false
    tenant_id                     = var.tenant_id
  }

  lifecycle {
    prevent_destroy = true
    ignore_changes  = [storage_mb, zone]
  }
}

resource "azurerm_postgresql_flexible_server_database" "app" {
  for_each  = var.databases # ["intelibill_dev", "intelibill_prod"]
  name      = each.value
  server_id = azurerm_postgresql_flexible_server.main.id
  collation = "en_US.utf8"
  charset   = "utf8"
  lifecycle { prevent_destroy = true }
}
```

`tenant_id` is required when Active Directory authentication is enabled. One B1ms + 32 GB is about $16.09/month at East US list price — the reference figure, not this deployment's, since **East US is offer-restricted for this subscription and the servers run in `centralindia`**. Check Central India rates before treating any total here as budget.

API `max_replicas` remains 1, with an explicit 10–15 connection pool, until the code-grounded scale blockers are closed. That pool ceiling matters more on a shared server: both environments draw connections from one server's limit, and B1ms is a low limit to begin with.

Resizing to B2s (about $53.32/month) erases most of the single-server saving — at that point two B1ms servers cost less than one B2s, and the isolation argument for splitting returns. Revisit [decision §8](infrastructure-decisions.md#8-one-shared-postgresql-server-two-databases) if you resize.

`password_auth_enabled = false` means no password exists to leak. It also means **you cannot connect with a password even for emergency access** — Entra is the only path. Confirm at least two people can authenticate before relying on this.

`prevent_destroy` on the server and prod database blocks `tofu destroy` from taking production data. It has stopped more incidents than any other single line here.

`ignore_changes = [storage_mb, zone]` prevents storage auto-grow and Azure's availability-zone placement from showing as permanent drift, which otherwise trains you to ignore plan output on the layer that owns your data.

**Why the name carries a serial.** Flexible Server names are globally unique DNS labels, and Azure pins a name to the region of its **first creation attempt — including a failed one**. A create that fails with `LocationIsOfferRestricted` still burns the name against that dead region, and every retry elsewhere then fails with a misleading 409:

```
InvalidResourceLocation: The resource 'intelibill-dev-pg' already exists in
location 'southindia' ... A resource with the same name cannot be created in
location 'centralindia'.
```

The resource does **not** exist — `az postgres flexible-server list` comes back empty and `show` returns `ResourceNotFound`. There is nothing to delete, and `tofu state list` showing nothing is not evidence that ARM is clean. Azure publishes no window for the reservation to lapse, so `var.name_serial` exists to step over a burned name:

```hcl
name = "intelibill-${var.env}-pg-${var.name_serial}"   # "01"
```

Increment it rather than waiting. This also recurs on any failed recreate later, which is why it is a variable and not a hardcoded suffix.

**The public path's actual security boundary is the firewall rule list.** It starts empty, and the module refuses the two rules that quietly undo it:

```hcl
variable "allowed_ip_rules" {
  type = map(object({ start_ip = string, end_ip = string }))
  default = {}

  validation {
    condition     = alltrue([for r in values(var.allowed_ip_rules) : r.start_ip != "0.0.0.0"])
    error_message = "0.0.0.0 as a start IP is either the Allow-Azure-services rule or an open-to-the-world rule. Neither is permitted."
  }

  validation {
    condition     = alltrue([for r in values(var.allowed_ip_rules) : r.end_ip != "255.255.255.255"])
    error_message = "255.255.255.255 opens the server to the entire internet."
  }
}

resource "azurerm_postgresql_flexible_server_firewall_rule" "allowed" {
  for_each         = var.allowed_ip_rules
  name             = each.key
  server_id        = azurerm_postgresql_flexible_server.main.id
  start_ip_address = each.value.start_ip
  end_ip_address   = each.value.end_ip
}
```

`0.0.0.0`–`0.0.0.0` is Azure's "Allow public access from any Azure service" rule. It reads as narrow and is not: it admits every Azure tenant, including other people's subscriptions. Rejecting it in a validation block rather than a comment is deliberate — comments do not fail plans.

On a public server the following are what keep the exception defensible, not optional polish:

```hcl
resource "azurerm_postgresql_flexible_server_configuration" "require_tls" {
  name      = "require_secure_transport"
  server_id = azurerm_postgresql_flexible_server.main.id
  value     = "ON"
}
# plus ssl_min_protocol_version = TLSv1.2,
#      connection_throttle.enable = on,
#      log_connections = on, log_disconnections = on
```

`connection_throttle.enable` slows repeated failed authentication from one host — the main remaining nuisance once passwords are disabled. The connection logs are what let you tell a scanner from an outage.

**What the public choice costs you later.** Migrations can run from a hosted GitHub runner with a temporary single-IP rule (Phase 11.4), which is simpler than the private path's in-Azure job. In exchange, the Container Apps outbound IPs must be allowlisted after Phase 10 and re-checked whenever the environment is recreated, and the server is exposed to internet-wide scanning traffic for as long as any rule exists. Moving to private access later means a VNet, private DNS zones, and VNet-integrated Container Apps — roughly $110/month more, and a migration path that cannot use hosted runners.

### 5.3 Registry and DNS

**No registry resource.** This repository publishes to public GHCR: the workflow pushes with its ephemeral `GITHUB_TOKEN`, and Container Apps pulls the public digest anonymously. If private images are ever required, add a registry module with `admin_enabled = false` — leaving that on creates a working long-lived username/password that someone will eventually use instead of managed identity — plus `AcrPush` on the deploy identities and `AcrPull` on each app identity.

DNS lives in `modules/dns` and is **skipped entirely** — deferred to roughly 2026-08-25, see Phase 6. `var.domain_name` stays `null`, so `count` evaluates to 0 and no zone is created:

```hcl
module "dns" {
  source = "../../modules/dns"
  count  = var.domain_name == null ? 0 : 1

  domain_name         = var.domain_name
  resource_group_name = data.azurerm_resource_group.shared.name
}
```

`count` on a null domain, rather than a placeholder zone name. Phase 6 delegation against the wrong zone is an outage, not a typo, and a zone created "to be renamed later" is exactly what gets delegated by accident. The zone also carries `prevent_destroy`, because destroying it while it is authoritative takes the domain down.

Before delegating an existing zone, export and reproduce all address, mail, TXT, CAA, and DNSSEC/DS records and prepare a registrar rollback. Otherwise keep authoritative DNS at the existing provider and create the Container Apps validation/application records there.

### 5.4 Apply and verify

```bash
cd .tofu/envs/shared && tofu init && tofu plan && tofu apply
```

Expect **8 resources** with no domain set: one server, two databases, and five configuration settings. Provisioning takes 5–10 minutes.

```bash
az postgres flexible-server show -g intelibill-shared -n intelibill-pg-01 \
  --query "{state:state, loc:location, ver:version, sku:sku.name, public:network.publicNetworkAccess, pwAuth:authConfig.passwordAuth, adAuth:authConfig.activeDirectoryAuth, backupDays:backup.backupRetentionDays}" -o tsv

az postgres flexible-server db list -g intelibill-shared -s intelibill-pg-01 \
  --query "[].name" -o tsv
```

Want `Ready`, the region you chose, `passwordAuth: Disabled`, `activeDirectoryAuth: Enabled`, and both `intelibill_dev` and `intelibill_prod` present. `passwordAuth: Enabled` on a public server is the one combination this whole design exists to avoid — stop and fix it there.

Then prove the public exception is actually narrow. On this path it is the check that matters:

```bash
az postgres flexible-server firewall-rule list \
  -g intelibill-shared -s intelibill-pg-01 -o table
```

Note `-s` for the server here, not `-n` — `firewall-rule` subcommands take `--server-name`, and `-n` is the rule's own name.

**Immediately after apply this list must be empty.** Empty means reachable by nobody, which is the correct resting state: Phase 7 adds your own IP temporarily and removes it in 7.6, and Phase 10 adds the Container Apps outbound IPs. A rule you cannot name the purpose of is a finding, not a leftover. On a shared server every rule admits traffic to **both** databases, so the list should stay shorter than you would tolerate on a dev-only server.

---

## Phase 6 — DNS delegation

> ## ⏸ DEFERRED — not before 2026-08-25
>
> **Decided 2026-07-26: skipped for now, revisit in roughly 30 days.** No domain has been chosen and registrar access is unconfirmed, so `var.domain_name` stays `null` and `modules/dns` is not instantiated. There is no zone, and therefore nothing to delegate.
>
> **Nothing else is blocked by this.** Phases 7, 8, 10, and 11 do not need DNS: Container Apps issue a working `*.azurecontainerapps.io` FQDN, and the API, web app, migrations, and smoke tests all run against it. Only **Phase 12 (custom domains)** depends on this phase, and it is deferred alongside.
>
> **When you pick it up, re-read rather than resume.** Nameserver propagation takes hours and the original reason for doing this early — having it settled before certificates are needed — no longer applies. Re-verify the current authoritative records first; a zone export taken 30 days ago will be stale.

**Goal.** Make Azure DNS authoritative for the domain.

**Why now.** Nameserver propagation takes hours. Starting it early means it has completed by the time Phase 14 needs it for certificate validation.

### 6.1 Get the nameservers

```bash
cd .tofu/envs/shared && tofu output -json dns_name_servers
```

### 6.2 Repoint at the registrar

**Manual, external, and potentially outage-causing.** Before replacing nameservers, export the current zone, recreate and verify every A/AAAA/CNAME/MX/TXT/CAA record, record DNSSEC/DS state, lower TTLs in advance, and capture the old nameservers for rollback. Missing mail-verification or DNSSEC records can break email or the entire domain without affecting the app-specific checks.

Azure DNS is optional. The safer path for an existing domain is often to leave nameservers at the current provider and create only the Container Apps validation/CNAME records there.

### 6.3 Verify

```bash
dig NS <your-domain> +short
```

Expect the Azure nameservers. Propagation is typically 1–4 hours, occasionally 48. **Do not proceed to Phase 14 until this resolves** — managed certificate issuance will fail without it.

---

## Phase 7 — Database bootstrap

**Goal.** On the one shared server, create the runtime/migrator principals for both environments, restrict each database to its own principals, establish default privileges, and create infrastructure-owned objects such as `cache_entries`.

**This is the most consequential phase in the runbook now.** With a single server there is no topological separation: all four principals live on the same server, and `intelibill_dev` and `intelibill_prod` are one `\c` apart. `REVOKE CONNECT ... FROM PUBLIC` is not defence in depth here — it *is* the boundary. PostgreSQL grants `CONNECT` to `PUBLIC` on every new database by default, so a database you forget to revoke is a database every principal on the server can open, with no error and no log line to tell you.

One upside of the shared server: the cross-environment denial test in 7.5 becomes a single `psql` attempt on one host instead of a network-level test, so there is no excuse for skipping it.

**Prerequisites, both easy to get wrong.**

*The workload identities must already exist.* 7.3 registers database principals named after `id-app-<env>` and `id-migrator-<env>`, and the principal is bound to the identity's object ID. If those identities are created (or recreated) after this phase, their object IDs change and the grants silently stop matching — the API then fails to authenticate with nothing wrong in the SQL. They are therefore provisioned early, in `.tofu/envs/{dev,prod}`, which Phase 10 later grows into the full environment layer:

```bash
tofu -chdir=.tofu/envs/dev  apply    # id-app-dev,  id-migrator-dev
tofu -chdir=.tofu/envs/prod apply    # id-app-prod, id-migrator-prod
```

*`psql` is required and is not part of the Azure CLI.* `az postgres flexible-server execute` is not a substitute — it expects administrator username and password, and password authentication is disabled on this server.

```bash
brew install libpq
export PATH="$(brew --prefix libpq)/bin:$PATH"   # keg-only; not linked by default
psql --version
```

**The whole phase is scripted** in [`scripts/db/phase7-bootstrap.sql`](../scripts/db/phase7-bootstrap.sql), which takes the object IDs as psql variables rather than hardcoding them. The sections below explain what it does and why; run the script rather than retyping the statements.

### 7.1 Make yourself an Entra admin

```hcl
resource "azurerm_postgresql_flexible_server_active_directory_administrator" "me" {
  server_name         = azurerm_postgresql_flexible_server.main.name
  resource_group_name = var.resource_group_name
  tenant_id           = var.tenant_id
  object_id           = var.admin_object_id
  principal_name      = var.admin_upn
  principal_type      = "User"
}
```

One administrator for one server, covering both databases. Managed through IaC rather than by hand so it survives a rebuild — with `password_auth_enabled = false`, losing the only administrator locks everyone out of the server permanently, including out of fixing it.

Verify it landed. Note the subcommand is `microsoft-entra-admin`; `ad-admin` does not exist and returns a "misspelled or not recognized" error that reads like a broken CLI:

```bash
az postgres flexible-server microsoft-entra-admin list \
  -g intelibill-shared -s intelibill-pg-01 -o table
```

Temporarily allow your own IP:

```bash
MY_IP=$(curl -s https://api.ipify.org)
az postgres flexible-server firewall-rule create \
  -g intelibill-shared -s intelibill-pg-01 \
  -n temp-admin --start-ip-address "$MY_IP" --end-ip-address "$MY_IP"
```

This rule exposes the server holding **production** data, not just dev. Delete it in 7.6 in the same sitting — not tomorrow.

### 7.2 Connect with a token

```bash
export PGPASSWORD=$(az account get-access-token \
  --resource https://ossrdbms-aad.database.windows.net \
  --query accessToken -o tsv)

psql "host=intelibill-pg-01.postgres.database.azure.com \
      user=<your-upn> dbname=postgres sslmode=require"
```

The token **is** the password. It expires in roughly an hour; if `psql` starts refusing mid-session, re-run the export.

The `--resource` value must be exactly `https://ossrdbms-aad.database.windows.net`. A token for the management plane (the default audience) authenticates fine against ARM and is rejected by PostgreSQL, which surfaces as a password failure rather than an audience error.

**Getting the firewall rule right is harder than it looks, and failure is silent.** Azure PostgreSQL *drops* non-allowlisted packets instead of rejecting them, so a wrong rule looks identical to an unreachable server: `Operation timed out`, never "your IP is not allowed".

`MY_IP=$(curl -s https://api.ipify.org)` — the usual idiom, and the one this guide used — is unreliable behind a corporate proxy or VPN:

- the address an HTTPS echo service reports is the egress used for **HTTPS**, which on a split-tunnel VPN is a different path from raw TCP on 5432;
- corporate NAT pools rotate the source address across a range, so a `/32` derived from one request may not match the next;
- on this network the three commonest echo services returned, in order: `202.38.180.72`, nothing at all (blocked), and — from the Azure activity log — `202.38.180.69`. The actual egress for port 5432 was `202.38.180.73`. Four different answers.

Two reliable ways to get the address that will actually be used:

```bash
# 1. Egress for raw TCP on the port you care about (portquiz answers on any port).
curl -s http://portquiz.net:5432/ | grep -oE '([0-9]{1,3}\.){3}[0-9]{1,3}'

# 2. What Azure itself recorded for your recent ARM calls.
az monitor activity-log list --offset 2h --max-events 40 \
  --query "[?httpRequest.clientIpAddress!=null].httpRequest.clientIpAddress" -o tsv | sort -u
```

Before blaming the rule, confirm the port is open to the destination at all. If the first succeeds and the second times out, the block is your network filtering the destination, and no firewall rule will fix it:

```bash
nc -vz -w 10 portquiz.net 5432
nc -vz -w 10 intelibill-pg-01.postgres.database.azure.com 5432
```

**Corporate networks commonly deny outbound 5432 to cloud destinations.** If that is your situation, do not widen the Azure rule chasing it. Run the bootstrap from somewhere with a clean path instead:

- **Azure Cloud Shell** — runs inside Azure, has `psql`, and uses your own Entra identity, which is already the server administrator. Get its egress with `curl -s ifconfig.me` there and allowlist that.
- **A non-corporate network** (phone hotspot), which usually just works.

Both still need a temporary rule, and both still need 7.6 to remove it.

### 7.3 Create principals for the managed identities

Flexible Server uses a helper function rather than `CREATE ROLE`. Managed identities need the OID variant, with `'service'` as the type. All four principals are created once, on the one server.

**Pass the principal (object) ID, not the client ID.** Both are GUIDs on the same identity, and the wrong one creates a principal that can never authenticate — with no error at creation time. Read them from `tofu output`, never by eye:

```bash
tofu -chdir=.tofu/envs/dev output -json identities | jq -r '.app.principal_id, .migrator.principal_id'
```

```sql
-- Connected to dbname=postgres on intelibill-pg-01.
SELECT * FROM pgaadauth_create_principal_with_oid(
  'id-app-dev',       '<principal_id>', 'service', false, false);
SELECT * FROM pgaadauth_create_principal_with_oid(
  'id-migrator-dev',  '<principal_id>', 'service', false, false);
SELECT * FROM pgaadauth_create_principal_with_oid(
  'id-app-prod',      '<principal_id>', 'service', false, false);
SELECT * FROM pgaadauth_create_principal_with_oid(
  'id-migrator-prod', '<principal_id>', 'service', false, false);
```

The two `false, false` arguments are `isAdmin` and `isMfa`. Keep `isAdmin` false for every one of these: an admin principal on a shared server can reach both databases regardless of any `CONNECT` grant, which defeats 7.4 entirely.

The principal **name must exactly match the managed identity's name** — it is what the identity presents when authenticating. Get the OIDs from `tofu output`.

### 7.4 The isolation grants

Revoke the default `PUBLIC` grants on **both** databases, then grant `CONNECT` only to that database's own principals. Missing either `REVOKE` leaves that database open to every principal on the server.

**Revoke `TEMPORARY` as well as `CONNECT`.** Both are granted to `PUBLIC` by default, and revoking only `CONNECT` leaves `=T/azure_pg_admin` in `datacl` — verified on this server. It is not exploitable on its own, since temp tables still require a connection, but any role later granted `CONNECT` silently inherits temp-table rights nobody intended:

```sql
REVOKE CONNECT   ON DATABASE intelibill_dev  FROM PUBLIC;
REVOKE CONNECT   ON DATABASE intelibill_prod FROM PUBLIC;
REVOKE TEMPORARY ON DATABASE intelibill_dev  FROM PUBLIC;
REVOKE TEMPORARY ON DATABASE intelibill_prod FROM PUBLIC;

GRANT CONNECT ON DATABASE intelibill_dev
  TO "id-app-dev", "id-migrator-dev";
GRANT CONNECT ON DATABASE intelibill_prod
  TO "id-app-prod", "id-migrator-prod";
```

Because both databases share a server, also make the cross-environment denial explicit rather than merely implied by absence. This is redundant if the `REVOKE` above worked, and it is exactly the belt you want when it silently did not:

```sql
REVOKE ALL ON DATABASE intelibill_prod FROM "id-app-dev", "id-migrator-dev";
REVOKE ALL ON DATABASE intelibill_dev  FROM "id-app-prod", "id-migrator-prod";
```

Then, connected to each database in turn — `<env>` being `dev` while in `intelibill_dev`, `prod` while in `intelibill_prod`:

```sql
\c intelibill_dev

-- PostgreSQL 15+ no longer grants CREATE on public to PUBLIC, but it still
-- grants USAGE. Revoking first means the grants below are the only source of
-- access, rather than additions on top of an inherited default.
REVOKE ALL ON SCHEMA public FROM PUBLIC;

GRANT USAGE ON SCHEMA public TO "id-app-dev";
GRANT CREATE, USAGE ON SCHEMA public TO "id-migrator-dev";

-- The migrator owns tables it creates. Without default privileges the
-- runtime identity has no access to anything created by a later migration.
ALTER DEFAULT PRIVILEGES FOR ROLE "id-migrator-dev" IN SCHEMA public
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO "id-app-dev";
ALTER DEFAULT PRIVILEGES FOR ROLE "id-migrator-dev" IN SCHEMA public
  GRANT USAGE, SELECT ON SEQUENCES TO "id-app-dev";
```

Repeat the block verbatim in `intelibill_prod` with the `-prod` principals. Crossing the names here — granting `id-app-dev` privileges inside `intelibill_prod` — produces a working system with no visible symptom, which is why 7.5 checks it rather than trusting the edit.

`ALTER DEFAULT PRIVILEGES` is scoped to the role *and* the database you are connected to, so it genuinely must be run once per database. Skipping it fails late: everything works until a migration adds a table, then the application gets `permission denied` on that table only.

Create the PostgreSQL distributed-cache schema/table through a reviewed migration or this migrator bootstrap. The application production configuration must use `CreateIfNotExists=false`, so the runtime identity never needs `CREATE`.

### 7.5 Verify isolation — do not skip

On a shared server this is the test that stands in for the network boundary you gave up. Do not skip it, and re-run it after any grant change.

```sql
SELECT datname, datacl FROM pg_database
WHERE datname IN ('intelibill_dev', 'intelibill_prod');
```

An entry with **no role name before `=`** is `PUBLIC`. Read the privilege letters, not just the presence of an entry: `c` is CONNECT, `T` is TEMPORARY, `C` is CREATE. With both revokes applied, `PUBLIC` should have no entry at all:

```
{azure_pg_admin=CTc/azure_pg_admin,"\"id-app-dev\"=c/azure_pg_admin","\"id-migrator-dev\"=c/azure_pg_admin"}
```

That is the verified-good shape: `azure_pg_admin` (Azure's own admin role) holds everything, each identity holds `c` on its own database, and there is no bare `=` entry. Seeing `=T/azure_pg_admin` means `CONNECT` was revoked but `TEMPORARY` was not. Seeing `=Tc/` means the `CONNECT` revoke did not apply at all — stop and fix that before any production data exists.

Then check all four combinations at once. The two `true`s are the easy half; the two `false`s are the point:

```sql
SELECT
  has_database_privilege('id-app-dev',  'intelibill_dev',  'CONNECT') AS dev_to_dev,
  has_database_privilege('id-app-prod', 'intelibill_prod', 'CONNECT') AS prod_to_prod,
  has_database_privilege('id-app-dev',  'intelibill_prod', 'CONNECT') AS dev_to_PROD,
  has_database_privilege('id-app-prod', 'intelibill_dev',  'CONNECT') AS prod_to_dev;
-- expect: t, t, f, f
```

`dev_to_PROD` returning `true` means your dev workload can read production data. That is the failure mode of the single-server choice, and this query is the only thing that surfaces it.

Repeat for the migrator principals — they are the ones with `CREATE`, so a crossed grant there is worse.

Confirm the privileges inside each database landed on the right roles:

```sql
\c intelibill_prod
SELECT grantee, privilege_type FROM information_schema.role_table_grants
WHERE grantee LIKE 'id-%' GROUP BY grantee, privilege_type ORDER BY grantee;
-- no grantee ending in -dev may appear here
```

Then confirm the runtime/migrator split actually holds at the schema level, which is what stops the API from ever running DDL:

```sql
\c intelibill_dev
SELECT
  has_schema_privilege('id-app-dev','public','USAGE')       AS app_usage_t,
  has_schema_privilege('id-app-dev','public','CREATE')      AS app_create_f,
  has_schema_privilege('id-migrator-dev','public','CREATE') AS mig_create_t,
  has_schema_privilege('id-app-prod','public','USAGE')      AS crossenv_usage_f;
-- expect: t, f, t, f
```

`app_create_f` returning `true` means the runtime identity can create and alter tables, which defeats [decision §18](infrastructure-decisions.md#18-separate-runtime-and-migrator-database-identities) — the API would hold permanent DDL rights over its own schema.

Still outstanding after this phase, both needing a running application rather than `psql`: that the runtime cannot disable RLS, and that RLS survives connection pooling (the `active_shop_id` session variable must be set per request, not per connection — a pooled connection carrying a previous request's value is a cross-tenant leak). Cover these in Phase 8's verification.

### 7.6 Remove the temporary rule

```bash
az postgres flexible-server firewall-rule delete \
  -g intelibill-shared -s intelibill-pg-01 -n temp-admin --yes

# Confirm it is gone, and that nothing else accumulated.
az postgres flexible-server firewall-rule list \
  -g intelibill-shared -s intelibill-pg-01 -o table
```

---

## Phase 8 — Application code changes

**Goal.** Make the complete codebase satisfy the production contract, not only authenticate to PostgreSQL.

**Why it blocks everything.** Infrastructure without these changes yields an application that cannot connect. This phase can run in parallel with Phases 5–7.

Before provisioning apps, also complete these source-level gates:

- delete the unconditional `await app.Services.ApplyMigrationsAsync()` from API startup;
- add `/health/live` and `/health/ready` and map Container Apps probes;
- add exact environment CORS origins and trusted forwarded headers;
- replace the compiled production `http://localhost:5277/api` and empty hub origin with relative routes proxied by SSR to `API_ORIGIN`;
- require auth on `ProductHub`, group broadcasts by active shop, and configure bearer query tokens only on hub routes;
- replace in-memory OAuth state; make rate limiting atomic; use Azure SignalR/backplane before API scale-out;
- create the distributed cache table with the migration identity and disable runtime table creation;
- standardise API port `8080`, pin both Docker runtimes, match the frontend Node engine, test the Bun/Node SSR command, and run both containers as non-root;
- rotate/remove/scrub the committed SMTP credential before deployment.

### Status — 2026-07-26

Applied on `infra-setup`. Every gate above is done except the four recorded as deferred below.

| Gate | Where |
|---|---|
| Startup migrations removed; schema owned by the migration job | `Program.cs`; local setup is `dotnet ef database update` |
| Entra token auth, one shared `NpgsqlDataSource`, `MaxPoolSize` 12, `SslMode.Require` | `NpgsqlDataSourceFactory`, `DatabaseOptions`, `DatabaseOptionsValidator` |
| `cache_entries` created by migration, `CreateIfNotExists=false` | `20260726120000_AddDistributedCacheTable`; DDL copied from the caching library, verified identical against a fresh database |
| `/health/live` and `/health/ready`, both excluded from HTTPS redirection | `EdgeExtensions`, `PostgresHealthCheck` |
| Forwarded headers and exact CORS origins, both configuration-driven | `EdgeExtensions`, `ProxyOptions`, `CorsOptions` |
| Relative `/api` and `/hubs`, proxied by SSR to `API_ORIGIN` | `environment.prod.ts`, `server.ts` |
| `ProductHub` authenticated and grouped by shop; hub-path-only query bearer token | `ProductHub`, `SignalRProductHubNotifier`, `Program.cs` |
| OAuth state in the distributed cache | `DistributedExternalOAuthStateStore` |
| Port `8080`, digest-pinned bases, non-root, Node 24 | both `Dockerfile`s, `docker-compose.yml` |
| Mobile release `API_BASE_URL` required | `tool/build-release.sh`, `AppConfig` |

Two corrections to the gate list above, both found by testing rather than reading:

- **The SSR server cannot run on Bun.** Proxying a WebSocket upgrade means hijacking the raw socket, and under Bun the handshake returns nothing, so every SignalR connection through the proxy fails. Node answers `101` on the same bundle. The runtime image is Node; Bun still installs and builds.
- **`Proxy:ForwardLimit` is 2 in deployed environments, not 1.** Ingress and the SSR proxy are two hops.

Deferred, each for a stated reason:

- **Azure SignalR or a backplane** — needed only above one API replica, which is outstanding decision 3.
- **Atomic rate limiting** — the limiter is still a distributed-cache read/modify/write. Deprioritised by the owner.
- **Key Vault and observability values** — Phase 9 and Phase 10, not source changes.
- **SMTP credential rotation** — the audit called it committed; it is not. `appsettings.Development.json` is gitignored and the value appears nowhere in history (`git log --all -S`), so there is no history to scrub. Rotating it is still worth doing, since it sat in plaintext locally, but that is an account action rather than a code change.

### 8.1 Make `Password` optional

`src/backend/Intelibill.Infrastructure/Options/DatabaseOptions.cs` currently marks `Password` as `[Required]` and concatenates it into the connection string. Under Entra auth there is no password. Drop `[Required]`, and add the pool setting:

```csharp
public string? Password { get; init; }
public int MaxPoolSize { get; init; } = 20;
public bool UseEntraAuth { get; init; }

public string ToConnectionString()
{
    var builder = new NpgsqlConnectionStringBuilder
    {
        Host = Host,
        Port = Port,
        Database = Database,
        Username = Username,
        MaxPoolSize = MaxPoolSize,
        SslMode = UseEntraAuth ? SslMode.Require : SslMode.Prefer,
    };

    if (!UseEntraAuth)
    {
        builder.Password = Password;
    }

    return builder.ConnectionString;
}
```

Using `NpgsqlConnectionStringBuilder` rather than string interpolation also fixes a latent bug: a password containing `;` or `=` would corrupt the current interpolated string.

Default to 10–15 for a one-replica B1ms launch, not 20. Pool budget is per replica and must leave room for migrations, administration, cache operations, and recovery.

### 8.2 Token-based authentication

In `Infrastructure/DependencyInjection.cs`, build one data source and use that same instance for EF **and** PostgreSQL distributed cache:

```csharp
if (dbOptions.UseEntraAuth)
{
    var credential = new DefaultAzureCredential();
    var dataSourceBuilder = new NpgsqlDataSourceBuilder(dbOptions.ToConnectionString());

    // Tokens expire in ~60 minutes. A one-shot provider authenticates
    // successfully and then fails EVERY reconnection after the first hour.
    dataSourceBuilder.UsePeriodicPasswordProvider(
        async (_, ct) =>
        {
            var token = await credential.GetTokenAsync(
                new TokenRequestContext(
                    ["https://ossrdbms-aad.database.windows.net/.default"]),
                ct);
            return token.Token;
        },
        successRefreshInterval: TimeSpan.FromMinutes(50),
        failureRefreshInterval: TimeSpan.FromSeconds(5));

    var dataSource = dataSourceBuilder.Build();
    services.AddSingleton(dataSource);
    services.AddDbContext<ApplicationDbContext>(
        options => options.UseNpgsql(dataSource));
    services.AddDistributedPostgresCache(options =>
    {
        options.DataSource = dataSource;
        options.SchemaName = "public";
        options.TableName = "cache_entries";
        options.CreateIfNotExists = false;
    });
}
```

**`UsePeriodicPasswordProvider`, not a one-shot.** This is the single most dangerous mistake in the guide: it passes every test, deploys cleanly, works perfectly for an hour, then fails in production once the first token expires and every subsequent connection is rejected.

`successRefreshInterval` must be comfortably below the ~60 minute expiry. 50 minutes leaves margin for clock skew and slow token endpoints.

Add packages to `Directory.Packages.props` (versions there, versionless in the `.csproj` — see `CLAUDE.md`):

```xml
<PackageVersion Include="Azure.Identity" Version="1.13.1" />
```

### 8.3 Keep local development working

`docker-compose.yml` uses password auth. Both paths must work, so `UseEntraAuth` stays `false` in `appsettings.Development.json` and `true` only in deployed configuration. **Test both** — it is easy to fix Azure and break every developer's machine.

### 8.4 Observability

Do **not** add the New Relic .NET agent. `Program.cs` already registers OpenTelemetry and the OTLP exporter, and Serilog can export through the same endpoint. Supply the per-environment endpoint/service/environment/API-key settings from ordinary configuration and Key Vault. Adding the agent would double-instrument requests and can duplicate telemetry charges.

Add health and operational telemetry for database pool wait, PostgreSQL token refresh, cache failures, SignalR connections, migration version, replica restart/OOM, and external service latency.

### 8.5 Verify

```bash
dotnet build src/backend/Intelibill.slnx
dotnet test src/backend/Intelibill.slnx
docker compose up -d && docker compose logs backend
```

Also build/test the Bun frontend and Flutter app, then smoke-test the built containers rather than only the local dev processes. Keep the API alive beyond an Entra-token refresh, force an OAuth callback onto another replica/restart, verify anonymous and cross-shop hub access fail, and test browser/API/hub routing through the real production origin. Local `docker compose` must still work.

---

## Phase 9 — Key Vault secret values

**Goal.** After the environment vault, identity, RBAC, soft delete, and purge protection exist, put the JWT signing key and every enabled integration secret into each environment's vault.

**Why manual.** Generating them with `random_password` would write the plaintext into Tofu state — [decision §7](infrastructure-decisions.md#7-key-vault-secrets-created-out-of-band). The JWT signing key is the credential that mints valid tokens for any user in any shop.

```bash
for ENV in dev prod; do
  az keyvault secret set --vault-name "intelibill-${ENV}-kv" \
    --name jwt-secret --value "$(openssl rand -base64 48)"
  az keyvault secret set --vault-name "intelibill-${ENV}-kv" \
    --name newrelic-licence-key --value "<licence key>"
done
```

Use **different** JWT secrets per environment. A shared secret means a dev-issued token is valid in production.

Do not use a secret `data` source either: the provider reads its `value`, and OpenTofu retains data-source attributes in state. Construct a versionless URI without reading the secret:

```hcl
key_vault_secret_id = "${azurerm_key_vault.main.vault_uri}secrets/jwt-secret"
```

Container Apps resolves the value under the runtime managed identity. Include SMTP, enabled OAuth providers, product lookup, HSN service, and OTLP/New Relic credentials only when those integrations are enabled. CI never reads them.

---

## Phase 10 — Environment infrastructure

**Goal.** Provision Key Vault, Log Analytics, Container Apps Environment, migration job, and both apps per environment.

Split this phase:

1. **10A foundation:** vault, identities, RBAC, logging, network/environment. Apply 10A, then perform Phase 9 secret entry.
2. **10B workloads:** migration job and web/API apps referencing already-existing secret URIs.

This removes the original impossible sequence where Phase 9 attempted to write secrets into vaults not created until Phase 10.

### 10.1 Container App shape

```hcl
resource "azurerm_container_app" "api" {
  name                         = "intelibill-${var.env}-api"
  container_app_environment_id = azurerm_container_app_environment.main.id
  resource_group_name          = var.rg_name
  revision_mode                = "Single"

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.app.id]
  }

  registry {
    server   = var.acr_login_server
    identity = azurerm_user_assigned_identity.app.id
  }

  secret {
    name                = "jwt-secret"
    key_vault_secret_id = "${azurerm_key_vault.main.vault_uri}secrets/jwt-secret"
    identity            = azurerm_user_assigned_identity.app.id
  }

  ingress {
    external_enabled = false # browser reaches API through the web proxy
    target_port      = 8080
    transport        = "auto"
  }

  template {
    min_replicas = 0
    max_replicas = 1 # raise only after distributed-state/SignalR gates pass

    container {
      name = "api"
      # Placeholder. The deploy pipeline owns this from here on.
      image  = var.bootstrap_image_by_digest
      cpu    = var.cpu
      memory = var.memory

      env {
        name  = "ASPNETCORE_HTTP_PORTS"
        value = "8080"
      }
      env {
        name  = "Database__UseEntraAuth"
        value = "true"
      }
      env {
        name        = "Jwt__Secret"
        secret_name = "jwt-secret"
      }

      # Add startup/readiness/liveness probes for the implemented endpoints.
    }

    http_scale_rule {
      name                = "http"
      concurrent_requests = 50
    }
  }

  lifecycle {
    # Without this, every apply reverts the running app to the placeholder,
    # silently rolling production back during unrelated infra changes.
    ignore_changes = [template[0].container[0].image]
  }
}
```

`ignore_changes` is load-bearing — [decision §14](infrastructure-decisions.md#14-ignore_changes-on-the-container-image).

`revision_mode = "Single"` means each deployment fully replaces the previous revision. Switch to `Multiple` later if you want traffic-split rollouts.

The example is not a complete configuration inventory. Add database host/port/name/user/pool/SSL, JWT issuer/audience/expiries, application base URL, CORS/trusted proxy, external integrations, OTLP observability, and web `API_ORIGIN`. Add a separate public web app on port 4000 with health probe and reverse-proxy support for `/api` and `/hubs`. Public GHCR does not need the `registry` block; keep it only for optional ACR.

### 10.2 Role assignments for the app identity

```hcl
resource "azurerm_role_assignment" "kv_secrets" {
  scope                = azurerm_key_vault.main.id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_user_assigned_identity.app.principal_id
}

# ACR only. On public GHCR, pulls are anonymous and this block does not exist —
# it is also the cross-resource-group assignment that broke the original Phase 3.
resource "azurerm_role_assignment" "acr_pull" {
  count                = var.acr_id == null ? 0 : 1
  scope                = var.acr_id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_user_assigned_identity.app.principal_id
}
```

The runtime `app` identity and the migrator identity are created **here**, in the environment layer — not in bootstrap. Bootstrap holds only the identities GitHub federates into.

These are the assignments that fail if Phase 3.2's RBAC role was skipped: the apply identity is creating a role assignment, which `Contributor` alone cannot do.

**Narrow the deploy identity here — this is the step that restores the boundary the single resource group removed.** Bootstrap assigns the deployer role at group scope because no Container App exists yet. Now one does, so re-assign at resource scope and drop the group-scoped grant:

```hcl
resource "azurerm_role_assignment" "deploy_app" {
  scope              = azurerm_container_app.api.id
  role_definition_id = var.container_app_deployer_role_id
  principal_id       = var.deploy_principal_id
}

resource "azurerm_role_assignment" "deploy_web" {
  scope              = azurerm_container_app.web.id
  role_definition_id = var.container_app_deployer_role_id
  principal_id       = var.deploy_principal_id
}

resource "azurerm_role_assignment" "deploy_job" {
  scope              = azurerm_container_app_job.migrate.id
  role_definition_id = var.container_app_deployer_role_id
  principal_id       = var.deploy_principal_id
}
```

Then remove `azurerm_role_assignment.deploy` from `bootstrap/role-assignments.tf` and apply bootstrap again. Until you do, `deploy_dev` can still update production's Container App, because both apps live in the one group.

Verify the narrowing actually took, rather than adding assignments and leaving the broad one in place:

```bash
az role assignment list \
  --assignee "$(cd .tofu/bootstrap && tofu output -json principal_ids | jq -r .deploy_dev)" \
  --all --query "[].{Role:roleDefinitionName, Scope:scope}" -o tsv
```

Every scope must end in a specific `containerApps/...` or `jobs/...` resource. A scope ending at `/resourceGroups/intelibill-shared` means the group-scoped assignment is still there and the boundary is not restored.

### 10.3 Apply dev first

```bash
cd .tofu/envs/dev && tofu init && tofu plan && tofu apply
```

Read the plan properly. Then prod:

```bash
cd ../prod && tofu init && tofu plan && tofu apply
```

### 10.4 Verify

```bash
az containerapp show -g intelibill-shared -n intelibill-dev-api \
  --query "{fqdn:properties.configuration.ingress.fqdn, image:properties.template.containers[0].image}"
```

The image should still be the quickstart placeholder — that is correct at this stage. The FQDN should serve the Azure quickstart page.

---

## Phase 11 — Pipelines

**Goal.** Automate plan, apply, and deploy.

### 11.1 `infra-plan.yml`

Triggers on trusted same-repository pull requests touching `.tofu/**`. Logs in with the repository-scoped `vars.AZURE_CLIENT_ID_PLAN` — resource Reader plus state blob data Reader — and posts the plan as a PR comment. It declares **no** `environment:`, which is why that variable is repository-scoped rather than environment-scoped. It cannot mutate Azure, but a malicious plan can still exfiltrate readable state, so reject fork PRs, never use `pull_request_target`, and pin actions/providers.

### 11.2 `infra-apply.yml`

Triggers on push to `main` touching `.tofu/**`. One job per layer, sequential: `shared` behind `environment: shared`, then `dev` behind `environment: dev`, then `prod` behind `environment: prod`. Every job uses that environment's `vars.AZURE_CLIENT_ID_INFRA`.

Each job runs in its own layer directory, so it picks up that layer's backend container and can only reach its own state. A job that needs to touch another layer's resources is telling you the resource is in the wrong layer.

### 11.3 `deploy.yml`

Uses `vars.AZURE_CLIENT_ID_DEPLOY`, never `AZURE_CLIENT_ID_INFRA` — the audit's "routine deploy identity must not be a broad infra identity" is enforced by which variable this workflow names.

Build/test/scan the backend and frontend, push both once, and capture **two digests**, not only the backend tag. The example below is ACR-specific; substitute `ghcr.io/chanakya-net/intelibill/...` and `docker/login-action` with `GITHUB_TOKEN` for the default public-GHCR path.

```yaml
- name: Build and push
  id: build
  run: |
    az acr login --name intelibillacr
    docker buildx build \
      -f src/backend/Dockerfile \
      -t intelibillacr.azurecr.io/api:${{ github.sha }} \
      --push --metadata-file meta.json .
    echo "digest=$(jq -r '."containerimage.digest"' meta.json)" >> "$GITHUB_OUTPUT"
```

Then deploy by digest:

```yaml
- run: |
    az containerapp update \
      -g intelibill-shared -n intelibill-dev-api \
      --image intelibillacr.azurecr.io/api@${{ steps.build.outputs.digest }}
```

Production consumes the **same API and web digest outputs**—never a rebuild. The job dependency must be `build → dev migration → dev deploy → dev smoke → production reviewer → prod migration → prod deploy → prod smoke`. Do not fork dev and prod in parallel from the build. Record SBOM/scan results and deployed digests; add a concurrency group so two production releases cannot overlap.

### 11.4 Migrations

For the recommended private production network, publish `dotnet ef migrations bundle` or a dedicated migrator image, configure it with the migrator UAMI, and start/wait for the Container Apps job:

```yaml
- name: Run migration job
  run: |
    az containerapp job start \
      --name "intelibill-${ENVIRONMENT}-migrate" \
      --resource-group "intelibill-${ENVIRONMENT}"
    # Poll the returned execution name and fail unless its terminal status is Succeeded.
```

The bundle/job uses an Entra-aware runtime configuration, and the running API never calls `Database.MigrateAsync()`. Use expand/contract migrations because the old revision is still serving until the job succeeds.

This is the locked path for this deployment: **public network, hosted runner, temporary single-IP rule.**

```yaml
- name: Add firewall rule
  run: |
    IP=$(curl -s https://api.ipify.org)
    az postgres flexible-server firewall-rule create \
      -g intelibill-shared -s intelibill-pg-01 \
      -n "gha-${{ github.run_id }}" \
      --start-ip-address "$IP" --end-ip-address "$IP"

- name: Apply migrations
  run: dotnet ef database update \
        --project src/backend/Intelibill.Infrastructure \
        --startup-project src/backend/Intelibill.Api

- name: Remove firewall rule
  if: always()
  run: |
    az postgres flexible-server firewall-rule delete \
      -g intelibill-shared -s intelibill-pg-01 \
      -n "gha-${{ github.run_id }}" --yes
```

`if: always()` is essential. Without it a failed migration leaves the rule behind permanently, and rules accumulate silently.

**On a shared server this rule opens the host holding production data, even when migrating dev.** The runner can reach `intelibill_prod`'s port for the length of the job; only the `CONNECT` grants from 7.4 stop it from getting further. That is another reason the migration job must target its database explicitly by name and never fall back to a default.

"Allow Azure services" does **not** cover GitHub runners — they are not Azure resources. Hence the per-run rule.

### 11.5 Verify

Run `deploy.yml` manually against dev. Confirm the app now runs your image rather than the placeholder, and that the firewall rule is gone afterwards:

```bash
az postgres flexible-server firewall-rule list \
  -g intelibill-shared -s intelibill-pg-01 -o table
```

---

## Phase 12 — Custom domains

> ## ⏸ DEFERRED — blocked by Phase 6, not before 2026-08-25
>
> Custom domains need the DNS zone from Phase 6, which is deferred. Until then the applications are reached on their Container Apps `*.azurecontainerapps.io` FQDNs, which are fully functional and TLS-terminated with a managed certificate.
>
> **What to remember when you return here:** the mobile app's `API_BASE_URL` and the web app's `API_ORIGIN` will be pointing at the `azurecontainerapps.io` hostnames. Moving to a custom domain means updating both, plus CORS origins and any OAuth redirect URIs registered with Google and Facebook — the redirect URIs are the ones that break silently in production.

**Goal.** Bind real hostnames with managed certificates.

**Prerequisite.** Phase 6 must have propagated. Verify with `dig NS <domain> +short` first.

Add the DNS records, then bind:

```bash
az containerapp hostname add \
  -g intelibill-shared -n intelibill-prod-api --hostname api.<domain>

az containerapp hostname bind \
  -g intelibill-shared -n intelibill-prod-api \
  --hostname api.<domain> --validation-method CNAME
```

**Expect the first apply to fail.** Certificate issuance requires the `asuid.` TXT and CNAME records to already resolve, and creating both in a single apply races DNS propagation. Wait a few minutes and re-run. This is a known rough edge, not a misconfiguration.

Verify:

```bash
curl -sSI https://api.<domain>/ | head -1
```

---

## Phase 13 — Keep-warm job

**Goal.** Avoid cold starts during business hours.

Do not deploy this phase until `/health/ready` and the web health endpoint exist. Container Apps cron uses UTC and its default scale-down cooldown is 300 seconds. A ten-minute ping allows the replica to scale to zero between pings.

For an explicitly chosen 06:00–20:00 Monday–Saturday **IST** window, the UTC window is 00:30–14:30. Because of the half-hour boundary, represent it with multiple scheduled-job resources (or a module expanded over these expressions), all using the same template:

```hcl
resource "azurerm_container_app_job" "warm" {
  for_each = {
    start  = "30-58/4 0 * * 1-6"
    middle = "*/4 1-13 * * 1-6"
    end    = "0-28/4 14 * * 1-6"
    close  = "30 14 * * 1-6"
  }

  name                         = "intelibill-prod-warm-${each.key}"
  container_app_environment_id = azurerm_container_app_environment.main.id
  resource_group_name          = var.rg_name
  replica_timeout_in_seconds   = 30

  schedule_trigger_config {
    cron_expression = each.value
  }

  template {
    container {
      name   = "warm"
      image  = "curlimages/curl@sha256:<reviewed-pinned-digest>"
      cpu    = 0.25
      memory = "0.5Gi"
      command = ["/bin/sh", "-ec"]
      # While Phase 6/12 are deferred, var.app_host is the Container Apps FQDN
      # rather than a custom domain. Do not hardcode either one.
      args = ["curl -fsS --max-time 10 https://${var.app_host}/health && curl -fsS --max-time 10 https://${var.app_host}/api/health/ready"]
    }
  }
}
```

Replace the placeholder with a reviewed current digest. If the intended timezone/window differs, recalculate and test the UTC expressions. A simpler alternative is `min_replicas=1`; it costs more but is operationally clearer. Persistent SignalR connections also keep the API active, so verify actual replica/billing behavior rather than assuming the job is the only wake source.

---

## Phase 14 — Final verification

Work through all of these before calling it done.

| # | Check | How | Expected |
|---|---|---|---|
| 1 | No Azure secrets in GitHub | `gh secret list` | No `AZURE_*` entries |
| 2 | Prod gate enforced | Open a PR, watch `deploy.yml` | Pauses for approval |
| 3 | Dev cannot reach prod DB | `has_database_privilege('id-app-dev','intelibill','CONNECT')` | `false` |
| 4 | Entra auth survives token expiry | Leave dev idle 70min, then request | Succeeds |
| 5 | Pool size applied | Query `pg_stat_activity` under load | ≤ 20 per replica |
| 6 | RLS still enforced | Existing integration tests | Pass |
| 7 | Image is a digest | `az containerapp show ... --query image` | Contains `@sha256:` |
| 8 | Tofu does not revert the image | `tofu plan` after a deploy | No image change |
| 9 | Network is least privilege | private route test or `firewall-rule list` | Only intended app/job path |
| 10 | Scale to zero works | Idle 30min outside warm hours | Replicas at 0 |
| 11 | Cold start acceptable | Time first request when cold | 2–4s |
| 12 | Logs arriving | `az containerapp logs show` | Application output |
| 13 | OTel reporting | OTLP/New Relic dashboard | One trace per request, no duplicate agent spans |
| 14 | Certificates valid | ⏸ *deferred with Phase 12.* Until then: `curl -sSI https://<app>.azurecontainerapps.io` | `200`, valid TLS on the Azure-managed cert |
| 15 | Rollback works | Deploy a previous digest | Prior version serving |
| 16 | Startup has no DDL | runtime role + cold start | Starts; cannot create/alter/drop |
| 17 | Cache is ready | runtime cache operation | Works with `CreateIfNotExists=false` |
| 18 | Browser routing | production web origin | SSR, `/api`, and `/hubs` work; no localhost |
| 19 | Hub tenant isolation | anonymous/cross-shop clients | denied; only own-shop messages |
| 20 | OAuth is distributed | restart/re-route callback | callback succeeds exactly once |
| 21 | Scale-out behavior | two API replicas | delivery/limits correct, or max remains 1 |
| 22 | Secret/state scan | repository history + Tofu state | no SMTP/JWT/API secret values |
| 23 | Restore drill | restore to isolated target | documented RPO/RTO met |
| 24 | Cost controls | budgets/log caps/quotas | alerts verified |

**Check 4 is the one people skip.** It is the only way to catch a one-shot password provider, and that bug does not surface until an hour after deployment.

---

## Failure quick reference

| Symptom | Cause | Fix |
|---|---|---|
| `AADSTS70021` | Federated subject mismatch | Compare Phase 3.1 subject to the token claim, exactly |
| `AuthorizationFailed` on role assignment | Contributor cannot write role assignments | Add RBAC Administrator (Phase 3.2) |
| `AuthorizationPermissionMismatch` on state | Control-plane rights without data-plane | Grant `Storage Blob Data Contributor` |
| Auth works, fails after ~1 hour | One-shot password provider | `UsePeriodicPasswordProvider` (Phase 8.2) |
| `too many connections` under load | Pool default of 100 per replica | Set `MaxPoolSize` (Phase 8.1) |
| App reverts to quickstart image | Missing `ignore_changes` | Phase 10.1 |
| Certificate issuance fails | DNS not propagated | Wait, re-run |
| `permission denied for table X` | Missing `ALTER DEFAULT PRIVILEGES` | Phase 7.4 |
| `plan` proposes creating everything | Wrong backend `key` or container | Fix it, re-init. **Do not apply** |
| `az containerapp update` fails on identity assignment | Deploy role withholds `assign/action` | Grant it scoped to the app UAMI only (Phase 3.2) |
| Deploy job can do more than deploy | Workflow named `AZURE_CLIENT_ID_INFRA` | Use `AZURE_CLIENT_ID_DEPLOY` (Phase 4.3) |
| Environment apply blocked on a shared resource | Resource is in the wrong layer | Move it to `shared`; do not widen the grant |
| Dev role reads prod data | `REVOKE CONNECT` not applied | Phase 7.4, verify with 7.5 |
| `LocationIsOfferRestricted` on the server | Subscription is barred from that region | Check the capabilities API, not `list-skus`; pick an open region |
| `tofu apply` "succeeded" but nothing exists | Exit code came from a pipe, not tofu | Never pipe apply into `tail`; capture the exit code |
| `prevent_destroy` did not prevent a destroy | The resource was removed from configuration, so the lifecycle rule went with it | `prevent_destroy` guards a targeted destroy, not deleting the code. Review plans for `will be destroyed` |
| `unrecognized arguments: -n` on `firewall-rule list` | `-n` is not accepted there | Use `-s <server>`; there is no rule argument on `list` |
| `unrecognized arguments: --rule-name` | That flag does not exist on `firewall-rule` | `-s <server> -n <rule>`. `--rule-name` appears in much published guidance and is wrong |
| PostgreSQL connection times out with a matching firewall rule | The source IP is not what an IP-echo service reported, or the network filters the destination | Get the egress IP for port 5432 specifically; see 7.2 |
| Dev workload reads production data | Missing `REVOKE CONNECT` on the shared server | Phase 7.4, then prove all four combinations with 7.5 |

---

## Outstanding

1. ~~Choose whether production starts on the recommended private PostgreSQL network or an explicitly time-bounded public-firewall exception.~~ **Resolved: public with a narrow allowlist** (Phase 5.2). Revisit if the exposure or a compliance requirement changes; the move costs roughly $110/month and breaks hosted-runner migrations.
2. **⏸ Domain and registrar access — deferred 2026-07-26, revisit around 2026-08-25.** Phases 6 and 12 are parked; nothing else depends on them. Applications run on Container Apps `*.azurecontainerapps.io` hostnames until then. When resumed, remember the mobile `API_BASE_URL`, web `API_ORIGIN`, CORS origins, and OAuth redirect URIs all move with the hostname.
3. Confirm launch is one API replica, or fund the SignalR/distributed-state/rate-limit changes plus Azure SignalR/backplane.
4. Confirm the actual business timezone and warm-hours availability target.
5. ~~Add the existing Flutter `API_BASE_URL` `--dart-define` to release builds.~~ **Done (Phase 8).** `tool/build-release.sh` requires it and rejects a non-https value; `AppConfig` repeats both checks at startup so an APK built another way fails visibly instead of appearing offline.
6. Treat the migration tree, not stale repository summaries, as the migration source of truth.
