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
| 5 Shared infrastructure | Incorrect as written | Prefer public GHCR; use separate DB servers; add PostgreSQL `tenant_id`; choose public-firewall exception or private production network | DBs `$32.18/month` lean; optional ACR `+$5.07` |
| 6 DNS | Operationally risky | Inventory/copy all DNS and DNSSEC records before delegation, or keep current authoritative DNS and add only app records | Azure DNS `$0.50/month` + queries |
| 7 Database bootstrap | Topology and network dependent | Bootstrap each server independently; create Entra principals, default privileges, cache table, and isolation/RLS tests. Run from a routed in-Azure job for private DB | no separate service fee |
| 8 Application changes | Materially incomplete | Complete every gate in [architecture §6](infrastructure-architecture.md#6-application-production-contract), including migrations, web routing, health, CORS/proxy, SignalR, OAuth state, cache, and secrets | engineering effort |
| 9 Secret values | Sequenced before the vault exists | First create vault/identity/RBAC; then add different per-env values. Never read values through an OpenTofu data source | Key Vault `<$0.10/month` expected |
| 10 Environment infrastructure | Incomplete HCL | Add ingress/target ports, complete configuration, versionless Key Vault URIs, probes, network path, web app, and migration job. API max replicas is 1 initially | Container Apps about `$10–34/month` prod at assumed hours |
| 11 Pipelines | Incomplete and unsafe | Build both images once, dev then prod sequentially; tests/scans; migration job; smoke tests; digest rollback. Routine deploy identity must not be a broad infra identity | Actions `$0` for standard public-repo runners |
| 12 Custom domains | Mixed ownership | Manage binding/cert through IaC or explicitly manual commands, not both; verify `asuid`/CNAME and rollback | managed certificate `$0` |
| 13 Keep-warm | Does not work as stated | Cron is UTC, ten minutes exceeds the 300-second cooldown, Azure CLI image is oversized/mutable, and `/health` is absent | roughly `$0.03–$0.20/month` |
| 14 Verification | Missing production gates | Add tenant hub isolation, browser/API/hub routing, distributed OAuth, multi-replica behavior, cache privileges, backup restore, secrets/state scan, and cost alert checks | test/load usage only |

### Canonical order

Do not start environment provisioning until the application changes in step 7 have a reviewed implementation plan.

1. Choose the Azure subscription/region, domain owner, public-versus-private PostgreSQL network, registry option, service hours/timezone, and launch replica cap.
2. Bootstrap state storage; grant `Storage Blob Data Contributor` to apply and `Storage Blob Data Reader` to plan; migrate and test state.
3. Create separate, least-privilege OIDC identities. Pin GitHub actions and providers; reject forked state-reading plans.
4. Create GitHub `dev` and `prod` environments with required production review and concurrency locks.
5. Provision public GHCR integration or optional ACR, DNS records, two PostgreSQL servers, and—when selected—the production VNet/private DNS/Container Apps environment.
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
13. Bind domains/certificates, enable alerts/budgets/log caps, run backup restore and rollback drills, then enable the corrected keep-warm schedule if required.

### Cost checkpoint

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
| Public-firewall exception or private PostgreSQL? | Changes networking, migration execution, security posture, and recurring cost |
| ~~Public GHCR or optional ACR?~~ | **Locked: public GHCR.** No registry RBAC, no `registry` block on the Container App |
| One launch replica or scale-ready? | Scale-out requires SignalR, OAuth-state, and rate-limiter application work |
| Which domain, and is the registrar accessible? | Phase 8 blocks on a manual nameserver change you may not control |
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

Six identities in three families, not one per environment. The audit verdict above is the reason: a single per-environment identity has to hold every right that any workflow touching that environment needs, so the routine deploy that runs on every merge ends up as powerful as the gated infrastructure apply.

| Identity | Reached by | Holds |
|---|---|---|
| `plan` | any pull request | Reader everywhere, blob data Reader on state |
| `infra_apply_shared` | `shared` environment, gated | Contributor on the shared RG |
| `infra_apply[dev\|prod]` | `dev`/`prod` environment, gated | Contributor + RBAC admin on its own RG |
| `deploy[dev\|prod]` | `dev`/`prod` environment | update a Container App, start a job |

New file `.tofu/bootstrap/identities.tf` — Tofu merges every `.tf` in the directory, so `main.tf` stays limited to state bootstrap:

```hcl
locals {
  environments = toset(["dev", "prod"])
  state_layers = toset(["shared", "dev", "prod"])

  oidc_issuer   = "https://token.actions.githubusercontent.com"
  oidc_audience = ["api://AzureADTokenExchange"]
}

resource "azurerm_resource_group" "env" {
  for_each = local.environments
  name     = "intelibill-${each.key}"
  location = var.location
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

resource "azurerm_user_assigned_identity" "infra_apply_shared" {
  name                = "id-gha-infra-apply-shared"
  resource_group_name = azurerm_resource_group.shared.name
  location            = var.location
}

resource "azurerm_federated_identity_credential" "infra_apply_shared" {
  name      = "gha-infra-apply-shared"
  parent_id = azurerm_user_assigned_identity.infra_apply_shared.id
  audience  = local.oidc_audience
  issuer    = local.oidc_issuer
  subject   = "repo:${var.github_repository}:environment:shared"
}

resource "azurerm_user_assigned_identity" "infra_apply" {
  for_each            = local.environments
  name                = "id-gha-infra-apply-${each.key}"
  resource_group_name = azurerm_resource_group.env[each.key].name
  location            = var.location
}

resource "azurerm_federated_identity_credential" "infra_apply" {
  for_each  = local.environments
  name      = "gha-infra-apply-${each.key}"
  parent_id = azurerm_user_assigned_identity.infra_apply[each.key].id
  audience  = local.oidc_audience
  issuer    = local.oidc_issuer
  subject   = "repo:${var.github_repository}:environment:${each.key}"
}

resource "azurerm_user_assigned_identity" "deploy" {
  for_each            = local.environments
  name                = "id-gha-deploy-${each.key}"
  resource_group_name = azurerm_resource_group.env[each.key].name
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

`infra_apply[env]` and `deploy[env]` share a subject. That is legal: subjects are unique per identity, not globally, and the workflow selects between them by client ID. The gate they share is the environment; the difference between them is what each is allowed to do once through it.

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

The original example gave each environment identity `Contributor` over the shared resource group and permanent RBAC Administrator in its environment. That was both over-broad and still insufficient when an environment apply attempted to create `AcrPull` on the shared registry. Use separate principals and keep shared assignments in the shared layer.

New file `.tofu/bootstrap/role-assignments.tf`:

```hcl
data "azurerm_subscription" "current" {}

resource "azurerm_role_assignment" "infra_apply_env" {
  for_each             = azurerm_resource_group.env
  scope                = each.value.id
  role_definition_name = "Contributor"
  principal_id         = azurerm_user_assigned_identity.infra_apply[each.key].principal_id
}

# Needed because the environment layer creates its own role assignments
# (app identity to Key Vault, migrator to the job).
resource "azurerm_role_assignment" "infra_apply_env_rbac" {
  for_each             = azurerm_resource_group.env
  scope                = each.value.id
  role_definition_name = "Role Based Access Control Administrator"
  principal_id         = azurerm_user_assigned_identity.infra_apply[each.key].principal_id
}

resource "azurerm_role_assignment" "infra_apply_env_state" {
  for_each             = azurerm_resource_group.env
  scope                = azurerm_storage_container.state[each.key].id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = azurerm_user_assigned_identity.infra_apply[each.key].principal_id
}

resource "azurerm_role_assignment" "infra_apply_shared" {
  scope                = azurerm_resource_group.shared.id
  role_definition_name = "Contributor"
  principal_id         = azurerm_user_assigned_identity.infra_apply_shared.principal_id
}

resource "azurerm_role_assignment" "infra_apply_shared_state" {
  scope                = azurerm_storage_container.state["shared"].id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = azurerm_user_assigned_identity.infra_apply_shared.principal_id
}

resource "azurerm_role_assignment" "plan_reader" {
  for_each             = merge(azurerm_resource_group.env, { shared = azurerm_resource_group.shared })
  scope                = each.value.id
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

Each environment's apply identity gets blob data Contributor on **its own** state container only. Scope these to the container, never the storage account: account scope silently re-grants cross-environment state writes.

Use `.id` on the container, not `.resource_manager_id`, which the provider deprecated for removal in 5.0. Also avoid `for_each` over the whole `azurerm_storage_container.state` resource — that evaluates deprecated attributes and produces warnings; iterate `local.state_layers` instead.

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

resource "azurerm_role_assignment" "deploy" {
  for_each           = azurerm_resource_group.env
  scope              = each.value.id
  role_definition_id = azurerm_role_definition.container_app_deployer.role_definition_resource_id
  principal_id       = azurerm_user_assigned_identity.deploy[each.key].principal_id
}
```

Assigned at resource-group scope so the Container Apps created in Phase 10 inherit it — no second bootstrap pass after the apps exist.

`listSecrets` is withheld on purpose. The deploy job never needs the app's configured secrets, and being able to read them would defeat the point of Key Vault.

`Microsoft.ManagedIdentity/userAssignedIdentities/assign/action` is also withheld, and this is the one likely surprise in Phase 11. If `az containerapp update` fails with an assign-action error, the fix is to grant that action scoped to the single app UAMI resource — **not** at resource-group scope, which would let the deploy identity attach any identity in the group to an app it controls and escalate through it.

**Registry.** On public GHCR there are no registry role assignments at all: push uses the job-scoped `GITHUB_TOKEN`, pulls are anonymous, and the Container App needs no `registry` block. Only if ACR is chosen does `AcrPush` belong on the deploy identities and `AcrPull` on the app identity.

`Contributor` is explicitly denied `Microsoft.Authorization/roleAssignments/write`, while `Reader`/`Contributor` do not provide blob data access. Both errors can look like a generic `AuthorizationFailed`, so verify each scope explicitly.

The role name must be exactly `Role Based Access Control Administrator` — Tofu resolves `role_definition_name` by exact match and fails with a confusing "role not found" if it is abbreviated. If your organisation restricts that role, `User Access Administrator` is the broader equivalent.

### 3.3 Verify

Expose the identifiers Phase 4 needs from `.tofu/bootstrap/outputs.tf`: `plan_client_id`, `infra_apply_shared_client_id`, the `infra_apply_client_ids` and `deploy_client_ids` maps, a `principal_ids` map keyed by purpose, and `state_containers`.

```bash
tofu apply
tofu output -json | jq

az role assignment list \
  --assignee "$(tofu output -json principal_ids | jq -r .infra_apply_prod)" \
  --all -o table

az role assignment list \
  --assignee "$(tofu output -json principal_ids | jq -r .deploy_prod)" \
  --all -o table
```

Expect exactly the documented duties, and check the negatives explicitly — they are the ones that matter:

- `infra_apply_prod`: `Contributor` + RBAC admin on `intelibill-prod`, blob data Contributor on `tfstate-prod`. **Nothing on `intelibill-shared`, nothing on `tfstate-dev`.**
- `deploy_prod`: the custom deployer role on `intelibill-prod` and nothing else. No `Contributor` anywhere.
- `plan`: `Reader` on all three groups, blob data Reader on all three containers, no write anywhere.

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

Each environment now publishes two client IDs, because two identities share its gate. The plan identity is repository-scoped: pull request plans do not enter an environment, so an environment variable would be invisible to them.

```bash
SUB=$(az account show --query id -o tsv)
TENANT=$(az account show --query tenantId -o tsv)
cd .tofu/bootstrap

INFRA=$(tofu output -json infra_apply_client_ids)
DEPLOY=$(tofu output -json deploy_client_ids)

for ENV in dev prod; do
  gh variable set AZURE_SUBSCRIPTION_ID      --env "$ENV" --body "$SUB"
  gh variable set AZURE_TENANT_ID            --env "$ENV" --body "$TENANT"
  gh variable set AZURE_CLIENT_ID_INFRA      --env "$ENV" --body "$(jq -r ".$ENV" <<<"$INFRA")"
  gh variable set AZURE_CLIENT_ID_DEPLOY     --env "$ENV" --body "$(jq -r ".$ENV" <<<"$DEPLOY")"
done

# Shared layer: infrastructure apply only, no deploy.
gh variable set AZURE_SUBSCRIPTION_ID --env shared --body "$SUB"
gh variable set AZURE_TENANT_ID       --env shared --body "$TENANT"
gh variable set AZURE_CLIENT_ID_INFRA --env shared \
  --body "$(tofu output -raw infra_apply_shared_client_id)"

# Plan runs on pull requests, outside every environment.
gh variable set AZURE_SUBSCRIPTION_ID --body "$SUB"
gh variable set AZURE_TENANT_ID       --body "$TENANT"
gh variable set AZURE_CLIENT_ID_PLAN  --body "$(tofu output -raw plan_client_id)"
```

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
      # Scoped identities cannot list at subscription scope. Show the one group
      # this identity owns instead, or a working setup looks like a failure.
      - run: az group show -n intelibill-dev -o table
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
      # This must FAIL. The deploy identity holds no blob data rights, and
      # proving the negative is the whole reason the identities were split.
      - run: |
          if az storage blob list --container-name tfstate-dev \
               --account-name <your-account> --auth-mode login -o none 2>/dev/null; then
            echo "::error::deploy identity can read state; the split is not holding"
            exit 1
          fi
```

Run the same pair against `prod` once the reviewer gate is in place, and confirm the run pauses for approval before the login step executes. If it does not pause, the gate is not applied and the prod credential is reachable from any run.

**If it fails:** `AADSTS70021: No matching federated identity record found` means the subject string does not match. Print the actual claim by decoding the token payload, and compare character by character against the `subject` in Phase 3.1 — including the environment segment, which is where a `shared`-versus-`prod` mix-up shows up. `Missing id-token permission` means the `permissions` block is absent — it is required per workflow *or* per job, and a job-level block overrides the workflow-level one entirely. `AuthorizationPermissionMismatch` on the blob step is data-plane, not control-plane: check the container-scoped assignment from Phase 3.2.

---

## Phase 5 — Modules and shared infrastructure

**Goal.** Write the reusable modules and provision ACR, the DNS zone, and the PostgreSQL server.

### 5.1 Layout

```
.tofu/
  modules/{registry,database,container-app,dns,github-identity}/
  envs/{shared,dev,prod}/
```

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

### 5.2 PostgreSQL server

Create one server from the environment module, not two databases on a shared server:

```hcl
resource "azurerm_postgresql_flexible_server" "main" {
  name                = "intelibill-${var.env}-pg"
  resource_group_name = var.rg_name
  location            = var.location
  version             = "17"

  # Controlled-launch default. Resize prod after measuring CPU credits,
  # connection usage and latency; do not use this as an HA promise.
  sku_name   = var.postgres_sku # default "B_Standard_B1ms"
  storage_mb = 32768

  backup_retention_days        = var.env == "prod" ? 14 : 7
  geo_redundant_backup_enabled = false

  authentication {
    active_directory_auth_enabled = true
    password_auth_enabled         = false
    tenant_id                     = var.tenant_id
  }

  lifecycle {
    prevent_destroy = true
  }
}

resource "azurerm_postgresql_flexible_server_database" "app" {
  name      = "intelibill"
  server_id = azurerm_postgresql_flexible_server.main.id
  collation = "en_US.utf8"
  charset   = "utf8"
  lifecycle { prevent_destroy = true }
}
```

`tenant_id` is required when Active Directory authentication is enabled. B1ms + 32 GB is currently about $16.09/month per environment in East US. API `max_replicas` remains 1, with an explicit 10–15 connection pool, until the code-grounded scale blockers are closed. Resize production to B2s (currently $53.32/month) or General Purpose from measurements and the availability requirement.

`password_auth_enabled = false` means no password exists to leak. It also means **you cannot connect with a password even for emergency access** — Entra is the only path. Confirm at least two people can authenticate before relying on this.

`prevent_destroy` on the server and prod database blocks `tofu destroy` from taking production data. It has stopped more incidents than any other single line here.

### 5.3 ACR and DNS

The current public repository should use public GHCR by default. In that path there is no Azure registry resource: the workflow pushes with its ephemeral `GITHUB_TOKEN`, and Container Apps pulls the public digest anonymously. If private images are required, use this optional ACR resource:

```hcl
resource "azurerm_container_registry" "main" {
  name                = "intelibillacr"
  resource_group_name = var.shared_rg_name
  location            = var.location
  sku                 = "Basic"
  admin_enabled       = false   # identity-based pulls only
}

resource "azurerm_dns_zone" "main" {
  name                = var.domain_name
  resource_group_name = var.shared_rg_name
}
```

`admin_enabled = false` disables the registry's built-in username/password. Leaving it on creates a working long-lived credential that someone will eventually use instead of managed identity.

Azure DNS is optional. Before delegating an existing zone, export and reproduce all address, mail, TXT, CAA, and DNSSEC/DS records and prepare a registrar rollback. Otherwise keep authoritative DNS at the existing provider and create the Container Apps validation/application records there.

### 5.4 Apply and verify

```bash
cd .tofu/envs/shared && tofu init && tofu plan && tofu apply
az postgres flexible-server show -g intelibill-shared -n intelibill-pg \
  --query "{sku:sku.name, version:version, state:state}" -o table
```

Server provisioning takes 5–10 minutes.

Before Phase 7, prove the chosen network path:

- **Private target:** production Container Apps environment uses a custom VNet; PostgreSQL uses private access/private DNS; migration job and API resolve/reach the private hostname. Hosted GitHub runners cannot migrate this database.
- **Public exception:** allowlist stable Container Apps outbound IPs and a temporary migration IP only. A runner-only firewall rule does not make the API reachable, and “Allow Azure services” is unnecessarily broad.

---

## Phase 6 — DNS delegation

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

**Goal.** Run the bootstrap independently on each environment server: create its runtime/migrator principals, restrict database access, establish default privileges, and create infrastructure-owned objects such as `cache_entries`.

Separate servers provide the primary environment boundary. Keep `REVOKE CONNECT ... FROM PUBLIC` as defence in depth, and test that the dev network/identity cannot authenticate or route to production.

### 7.1 Make yourself an Entra admin

```hcl
resource "azurerm_postgresql_flexible_server_active_directory_administrator" "me" {
  server_name         = azurerm_postgresql_flexible_server.main.name
  resource_group_name = var.rg_name
  tenant_id           = var.tenant_id
  object_id           = var.admin_object_id
  principal_name      = var.admin_upn
  principal_type      = "User"
}
```

Temporarily allow your own IP:

```bash
MY_IP=$(curl -s https://api.ipify.org)
az postgres flexible-server firewall-rule create \
  -g "intelibill-${ENVIRONMENT}" -n "intelibill-${ENVIRONMENT}-pg" \
  --rule-name temp-admin --start-ip-address "$MY_IP" --end-ip-address "$MY_IP"
```

### 7.2 Connect with a token

```bash
export PGPASSWORD=$(az account get-access-token \
  --resource https://ossrdbms-aad.database.windows.net \
  --query accessToken -o tsv)

psql "host=intelibill-${ENVIRONMENT}-pg.postgres.database.azure.com \
      user=<your-upn> dbname=postgres sslmode=require"
```

The token **is** the password. It expires in roughly an hour; if `psql` starts refusing mid-session, re-run the export.

### 7.3 Create principals for the managed identities

Flexible Server uses a helper function rather than `CREATE ROLE`. Managed identities need the OID variant, with `'service'` as the type:

```sql
SELECT * FROM pgaadauth_create_principal_with_oid(
  'id-app-<environment>', '<principal_id of the identity>', 'service', false, false);
SELECT * FROM pgaadauth_create_principal_with_oid(
  'id-migrator-<environment>', '<principal_id>', 'service', false, false);
```

The principal **name must exactly match the managed identity's name** — it is what the identity presents when authenticating. Get the OIDs from `tofu output`.

### 7.4 The isolation grants

```sql
REVOKE CONNECT ON DATABASE intelibill FROM PUBLIC;
GRANT CONNECT ON DATABASE intelibill
  TO "id-app-<environment>", "id-migrator-<environment>";
```

Then, connected to each database in turn:

```sql
\c intelibill

GRANT USAGE ON SCHEMA public TO "id-app-<environment>";
GRANT CREATE, USAGE ON SCHEMA public TO "id-migrator-<environment>";

-- The migrator owns tables it creates. Without default privileges the
-- runtime identity has no access to anything created by a later migration.
ALTER DEFAULT PRIVILEGES FOR ROLE "id-migrator-<environment>" IN SCHEMA public
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO "id-app-<environment>";
ALTER DEFAULT PRIVILEGES FOR ROLE "id-migrator-<environment>" IN SCHEMA public
  GRANT USAGE, SELECT ON SEQUENCES TO "id-app-<environment>";
```

The `ALTER DEFAULT PRIVILEGES` lines are easy to miss and fail late: everything works until a migration adds a table, then the application gets `permission denied` on that table only.

Create the PostgreSQL distributed-cache schema/table through a reviewed migration or this migrator bootstrap. The application production configuration must use `CreateIfNotExists=false`, so the runtime identity never needs `CREATE`.

### 7.5 Verify isolation — do not skip

```sql
SELECT datname, datacl FROM pg_database WHERE datname = 'intelibill';
```

`datacl` must **not** contain `=Tc/` for `PUBLIC`. Then prove local privileges and cross-environment denial:

```sql
SELECT has_database_privilege('id-app-<environment>', 'intelibill', 'CONNECT');
-- must return true
```

From the dev workload identity/network, attempt a connection to the production hostname. It must fail before any application query. Also verify the runtime role cannot `CREATE`, `ALTER`, `DROP`, or disable RLS.

If the first returns `true`, the `REVOKE` did not apply. Stop and fix it before any production data exists.

### 7.6 Remove the temporary rule

```bash
az postgres flexible-server firewall-rule delete \
  -g intelibill-shared -n intelibill-pg --rule-name temp-admin --yes
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

These are the assignments that fail if Phase 3.2's RBAC role was skipped: the environment apply identity is creating a role assignment, which `Contributor` alone cannot do.

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
az containerapp show -g intelibill-dev -n intelibill-dev-api \
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
      -g intelibill-dev -n intelibill-dev-api \
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

Only for an explicitly accepted **public-network launch exception**, a hosted runner can add a temporary single-IP rule:

```yaml
- name: Add firewall rule
  run: |
    IP=$(curl -s https://api.ipify.org)
    az postgres flexible-server firewall-rule create \
      -g intelibill-shared -n intelibill-pg \
      --rule-name "gha-${{ github.run_id }}" \
      --start-ip-address "$IP" --end-ip-address "$IP"

- name: Apply migrations
  run: dotnet ef database update \
        --project src/backend/Intelibill.Infrastructure \
        --startup-project src/backend/Intelibill.Api

- name: Remove firewall rule
  if: always()
  run: |
    az postgres flexible-server firewall-rule delete \
      -g intelibill-shared -n intelibill-pg \
      --rule-name "gha-${{ github.run_id }}" --yes
```

`if: always()` is essential. Without it a failed migration leaves the rule behind permanently, and rules accumulate silently.

"Allow Azure services" does **not** cover GitHub runners — they are not Azure resources. Hence the per-run rule.

### 11.5 Verify

Run `deploy.yml` manually against dev. Confirm the app now runs your image rather than the placeholder, and that the firewall rule is gone afterwards:

```bash
az postgres flexible-server firewall-rule list \
  -g intelibill-shared -n intelibill-pg -o table
```

---

## Phase 12 — Custom domains

**Goal.** Bind real hostnames with managed certificates.

**Prerequisite.** Phase 6 must have propagated. Verify with `dig NS <domain> +short` first.

Add the DNS records, then bind:

```bash
az containerapp hostname add \
  -g intelibill-prod -n intelibill-prod-api --hostname api.<domain>

az containerapp hostname bind \
  -g intelibill-prod -n intelibill-prod-api \
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
      args    = ["curl -fsS --max-time 10 https://app.${var.domain}/health && curl -fsS --max-time 10 https://app.${var.domain}/api/health/ready"]
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
| 14 | Certificates valid | `curl -sSI https://api.<domain>` | `200`, valid TLS |
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

---

## Outstanding

1. Choose whether production starts on the recommended private PostgreSQL network or an explicitly time-bounded public-firewall exception.
2. Confirm launch is one API replica, or fund the SignalR/distributed-state/rate-limit changes plus Azure SignalR/backplane.
3. Confirm the actual business timezone and warm-hours availability target.
4. Add the existing Flutter `API_BASE_URL` `--dart-define` to release builds.
5. Treat the migration tree, not stale repository summaries, as the migration source of truth.
