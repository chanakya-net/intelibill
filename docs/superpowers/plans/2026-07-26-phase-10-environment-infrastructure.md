# Phase 10 Environment Infrastructure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Provision and verify the shared monitoring, dev/prod Container Apps workloads, exact PostgreSQL egress allowlist, drift checker, and resource-scoped deploy permissions approved in the Phase 10 design.

**Architecture:** Shared state owns Log Analytics, PostgreSQL diagnostics, and every PostgreSQL firewall rule. Dev and prod states reuse their existing identities and Key Vaults, create one workload-profile v2 Container Apps environment apiece, and publish workload IDs/FQDNs/outbound addresses. Shared state reads those outputs and converts the API, web, and migration-job outbound-address union into one exact `/32`-equivalent firewall rule per IPv4 address.

**Tech Stack:** OpenTofu 1.12.5, AzureRM 4.81, Azure CLI 2.88, Bash, `jq`, xUnit/.NET 10, Bun/Angular 21, Flutter 3.44.2.

## Global Constraints

- Work on branch `infra-setup`; preserve all existing user changes.
- Follow strict red-green-refactor: add a contract test, run it and observe the expected failure, implement the smallest change, rerun it, then format and validate.
- Use the existing `id-app-dev`, `id-migrator-dev`, `id-app-prod`, and `id-migrator-prod` resources from their current state. Never import, move, recreate, or replace them.
- Keep PostgreSQL public networking with exact advertised IPv4 rules only. Never add `0.0.0.0`, `255.255.255.255`, the Azure-services exception, NAT Gateway, or a VNet.
- Keep API and web `max_replicas = 1`.
- Never use an `azurerm_key_vault_secret` data source or persist a secret value in OpenTofu state.
- Use the immutable multi-architecture bootstrap image `ghcr.io/mendhak/http-https-echo@sha256:0fefe04350131d7bb28355e3bf037062643e45f4a8a32f23679529e1b09d8ce4`.
- Configure `HTTP_PORT=8080` for the API bootstrap and `HTTP_PORT=4000` for the web bootstrap so the final ingress and probes are valid before application images are published.
- Keep `ignore_changes` limited to each workload container image. The deploy pipeline owns image drift; OpenTofu owns all other workload configuration.
- Apply saved plan files directly. Do not pipe `tofu apply` through another process.
- Commit after each green task. Do not commit `.terraform/`, plan files, state, identity snapshots, or generated drift-check input.

---

## Task 1: Add Shared Log Analytics and PostgreSQL Diagnostics

**Files:**

- Create: `.tofu/modules/shared-monitoring/main.tf`
- Create: `.tofu/modules/shared-monitoring/variables.tf`
- Create: `.tofu/modules/shared-monitoring/outputs.tf`
- Create: `.tofu/modules/shared-monitoring/tests/contract.tftest.hcl`
- Modify: `.tofu/envs/shared/main.tf`
- Modify: `.tofu/envs/shared/variables.tf`
- Modify: `.tofu/envs/shared/outputs.tf`
- Modify: `.tofu/envs/shared/terraform.tfvars.example`

- [ ] **Step 1: Write the failing shared-monitoring contract test**

Create a mocked `plan` test that supplies the existing PostgreSQL server resource ID and asserts the exact workspace and diagnostic contract:

```hcl
mock_provider "azurerm" {}

variables {
  resource_group_name   = "intelibill-shared"
  location              = "centralindia"
  postgres_server_id    = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/intelibill-shared/providers/Microsoft.DBforPostgreSQL/flexibleServers/intelibill-pg-01"
  daily_quota_gb        = 0.1
}

run "shared_monitoring_contract" {
  command = plan

  assert {
    condition     = azurerm_log_analytics_workspace.main.name == "intelibill-logs"
    error_message = "The shared workspace name must remain stable."
  }

  assert {
    condition = (
      azurerm_log_analytics_workspace.main.location == "centralindia" &&
      azurerm_log_analytics_workspace.main.sku == "PerGB2018" &&
      azurerm_log_analytics_workspace.main.retention_in_days == 30 &&
      azurerm_log_analytics_workspace.main.daily_quota_gb == 0.1 &&
      !azurerm_log_analytics_workspace.main.local_authentication_enabled
    )
    error_message = "The workspace cost and authentication guardrails changed."
  }

  assert {
    condition = toset([
      for setting in azurerm_monitor_diagnostic_setting.postgres.enabled_log :
      setting.category
    ]) == toset(["PostgreSQLLogs"])
    error_message = "PostgreSQL must export only PostgreSQLLogs."
  }

  assert {
    condition = toset([
      for setting in azurerm_monitor_diagnostic_setting.postgres.enabled_metric :
      setting.category
    ]) == toset(["AllMetrics"])
    error_message = "PostgreSQL must export AllMetrics."
  }
}
```

- [ ] **Step 2: Run the test and confirm RED**

Run:

```bash
tofu -chdir=.tofu/modules/shared-monitoring init -backend=false
tofu -chdir=.tofu/modules/shared-monitoring test
```

Expected: failure because the module resources do not exist.

- [ ] **Step 3: Implement the monitoring module**

Declare strongly typed inputs for `resource_group_name`, `location`, `postgres_server_id`, and `daily_quota_gb`. Validate `daily_quota_gb` as greater than zero.

Implement these resources:

```hcl
resource "azurerm_log_analytics_workspace" "main" {
  name                          = "intelibill-logs"
  resource_group_name           = var.resource_group_name
  location                      = var.location
  sku                           = "PerGB2018"
  retention_in_days             = 30
  daily_quota_gb                = var.daily_quota_gb
  local_authentication_enabled = false
}

resource "azurerm_monitor_diagnostic_setting" "postgres" {
  name                       = "intelibill-postgres"
  target_resource_id         = var.postgres_server_id
  log_analytics_workspace_id = azurerm_log_analytics_workspace.main.id

  enabled_log {
    category = "PostgreSQLLogs"
  }

  enabled_metric {
    category = "AllMetrics"
  }
}
```

Output one object and no shared keys:

```hcl
output "workspace" {
  description = "Azure Monitor workspace identifiers; shared keys are deliberately excluded"
  value = {
    id          = azurerm_log_analytics_workspace.main.id
    name        = azurerm_log_analytics_workspace.main.name
    customer_id = azurerm_log_analytics_workspace.main.workspace_id
  }
}
```

- [ ] **Step 4: Wire the module into shared state**

Add `log_analytics_daily_quota_gb` to shared variables with default `0.1`, pass `module.database.server_id`, and expose:

```hcl
output "log_analytics" {
  description = "Shared Azure Monitor destination; no workspace keys are exposed"
  value = {
    id           = module.shared_monitoring.workspace.id
    name         = module.shared_monitoring.workspace.name
    workspace_id = module.shared_monitoring.workspace.customer_id
  }
}
```

Document the `0.1` GB budget fuse in `terraform.tfvars.example`.

- [ ] **Step 5: Run GREEN verification**

Run:

```bash
tofu fmt -recursive .tofu/modules/shared-monitoring .tofu/envs/shared
tofu -chdir=.tofu/modules/shared-monitoring test
tofu -chdir=.tofu/envs/shared init
tofu -chdir=.tofu/envs/shared validate
```

Expected: module tests and shared validation pass.

- [ ] **Step 6: Commit**

```bash
git add .tofu/modules/shared-monitoring .tofu/envs/shared
git commit -m "feat(infra): add shared Azure monitoring"
```

---

## Task 2: Build the Tested Environment-Infrastructure Module

**Files:**

- Create: `.tofu/modules/environment-infrastructure/variables.tf`
- Create: `.tofu/modules/environment-infrastructure/main.tf`
- Create: `.tofu/modules/environment-infrastructure/api.tf`
- Create: `.tofu/modules/environment-infrastructure/web.tf`
- Create: `.tofu/modules/environment-infrastructure/migrate.tf`
- Create: `.tofu/modules/environment-infrastructure/diagnostics.tf`
- Create: `.tofu/modules/environment-infrastructure/rbac.tf`
- Create: `.tofu/modules/environment-infrastructure/outputs.tf`
- Create: `.tofu/modules/environment-infrastructure/tests/contract.tftest.hcl`

- [ ] **Step 1: Define the module contract in a failing test**

Use one mocked test file with separate dev, prod, optional-secret, and invalid-image runs. The normal run variables must use these exact shapes:

```hcl
variables {
  env                   = "dev"
  resource_group_name   = "intelibill-shared"
  location              = "centralindia"
  log_analytics_id      = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/intelibill-shared/providers/Microsoft.OperationalInsights/workspaces/intelibill-logs"
  deploy_principal_id   = "10000000-0000-0000-0000-000000000001"
  deploy_role_definition_id = "/subscriptions/00000000-0000-0000-0000-000000000000/providers/Microsoft.Authorization/roleDefinitions/10000000-0000-0000-0000-000000000002"

  app_identity = {
    id           = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/intelibill-shared/providers/Microsoft.ManagedIdentity/userAssignedIdentities/id-app-dev"
    name         = "id-app-dev"
    client_id    = "7c33ca76-6977-4e45-9c42-fda8cd5b2aab"
    principal_id = "639aa307-3394-4da0-a5bf-bcecf7a36632"
  }

  migrator_identity = {
    id           = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/intelibill-shared/providers/Microsoft.ManagedIdentity/userAssignedIdentities/id-migrator-dev"
    name         = "id-migrator-dev"
    client_id    = "51dece82-a93c-466b-8a16-6eaca361db28"
    principal_id = "d4463264-a136-467a-af6d-e174d99dab26"
  }

  key_vault = {
    id                 = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/intelibill-shared/providers/Microsoft.KeyVault/vaults/intelibill-dev-kv"
    vault_uri          = "https://intelibill-dev-kv.vault.azure.net/"
    jwt_signing_key_id = "https://intelibill-dev-kv.vault.azure.net/keys/jwt-signing"
  }

  database = {
    host          = "intelibill-pg-01.postgres.database.azure.com"
    port          = 5432
    name          = "intelibill_dev"
    max_pool_size = 12
  }

  bootstrap_image = "ghcr.io/mendhak/http-https-echo@sha256:0fefe04350131d7bb28355e3bf037062643e45f4a8a32f23679529e1b09d8ce4"
}
```

The contract assertions must cover all of the following:

- environment name `intelibill-dev-env`, workload-profile name/type `Consumption`, `logs_destination = "azure-monitor"`;
- app names `intelibill-dev-api`, `intelibill-dev-web`, and `intelibill-dev-migrate`;
- API internal ingress on `8080`, web external ingress on `4000`, HTTPS-only ingress, and single-revision mode;
- API identity equals the supplied app identity; migration identity equals the supplied migrator identity; web has no identity block;
- dev API/web resources are `0.25` CPU and `0.5Gi`; prod API/web resources are `0.5` CPU and `1Gi`;
- API/web `min_replicas = 0`, `max_replicas = 1`, and HTTP concurrency string `"50"`;
- API startup and liveness paths `/health/live`; API readiness path `/health/ready`; all use port `8080`;
- web startup, liveness, and readiness path `/`; all use port `4000`;
- migration manual trigger, parallelism `1`, completion count `1`, retry limit `0`, timeout `1800`, CPU `0.5`, memory `1Gi`;
- exact API, web, and migration environment-variable maps from the approved design;
- absence of `Database__Password`, `Jwt__Secret`, and every `Cors__AllowedOrigins` key;
- optional secret disabled by default and, when enabled, a versionless URI ending in `/secrets/new-relic-api-key`;
- Container Apps diagnostic categories `ContainerAppConsoleLogs`, `ContainerAppSystemLogs`, `ContainerAppHTTPLogs`, and `AllMetrics`;
- Key Vault diagnostic categories `AuditEvent` and `AllMetrics`;
- exactly three deploy role assignments scoped to the API, web, and job resource IDs;
- output address union includes mocked API, web, and migration-job addresses;
- image validation rejects a tag and an uppercase or non-64-character digest through `expect_failures = [var.bootstrap_image]`.

Use map comprehensions to make environment-variable assertions independent of nested-block ordering:

```hcl
assert {
  condition = {
    for setting in azurerm_container_app.api.template[0].container[0].env :
    setting.name => coalesce(setting.value, setting.secret_name)
  }["Proxy__ForwardLimit"] == "2"
  error_message = "The API must trust both the ingress and web-proxy hops."
}
```

Override computed fields so the output union is known during the mocked plan:

```hcl
override_resource {
  target = azurerm_container_app.api
  values = {
    outbound_ip_addresses = ["20.10.0.1"]
  }
}

override_resource {
  target = azurerm_container_app.web
  values = {
    outbound_ip_addresses = ["20.10.0.2"]
  }
}

override_resource {
  target = azurerm_container_app_job.migrate
  values = {
    outbound_ip_addresses = ["20.10.0.3"]
  }
}
```

- [ ] **Step 2: Run the module test and confirm RED**

Run:

```bash
tofu -chdir=.tofu/modules/environment-infrastructure init -backend=false
tofu -chdir=.tofu/modules/environment-infrastructure test
```

Expected: failure because the environment resources do not exist.

- [ ] **Step 3: Implement validated inputs and shared locals**

Declare `env` with `dev|prod` validation; exact identity, vault, and database object types; nullable `new_relic_api_key_secret_name`; and the digest-only image validation:

```hcl
variable "bootstrap_image" {
  description = "Immutable public multi-architecture image used until pipelines publish application images"
  type        = string

  validation {
    condition     = can(regex("@sha256:[0-9a-f]{64}$", var.bootstrap_image))
    error_message = "bootstrap_image must end in a lowercase sha256 digest."
  }
}
```

Use explicit sizing and environment maps:

```hcl
locals {
  app_resources = {
    dev  = { cpu = 0.25, memory = "0.5Gi" }
    prod = { cpu = 0.5, memory = "1Gi" }
  }

  api_environment = {
    ASPNETCORE_ENVIRONMENT                        = "Production"
    ASPNETCORE_HTTP_PORTS                         = "8080"
    HTTP_PORT                                     = "8080"
    AZURE_CLIENT_ID                               = var.app_identity.client_id
    Database__Host                                = var.database.host
    Database__Port                                = tostring(var.database.port)
    Database__Database                            = var.database.name
    Database__Username                            = var.app_identity.name
    Database__UseEntraAuth                        = "true"
    Database__MaxPoolSize                         = tostring(var.database.max_pool_size)
    Jwt__SigningMode                              = "KeyVault"
    Jwt__KeyVaultKeyId                            = var.key_vault.jwt_signing_key_id
    Jwt__Issuer                                   = "Intelibill-${var.env}"
    Jwt__Audience                                 = "Intelibill-${var.env}"
    App__BaseUrl                                  = "https://${azurerm_container_app.web.ingress[0].fqdn}"
    Proxy__Enabled                                = "true"
    Proxy__ForwardLimit                           = "2"
    Proxy__TrustAnyProxy                          = "true"
    Observability__NewRelic__OtlpEndpoint         = "https://otlp.nr-data.net:4318"
    Observability__NewRelic__ServiceName          = "Intelibill.Api"
    Observability__NewRelic__Environment          = var.env
  }
}
```

Keep the web and migration maps separate. The web map is exactly `HTTP_PORT=4000`, `PORT=4000`, `API_ORIGIN=https://intelibill-${var.env}-api.internal.${azurerm_container_app_environment.main.default_domain}`, and `NODE_ENV=production`. The migration map contains `HTTP_PORT=8080`, the migrator client ID/name, and the same five `Database__*` connection values as the API; it contains no JWT, proxy, web-origin, or observability key.

- [ ] **Step 4: Implement the Container Apps environment**

Create `azurerm_container_app_environment.main` with:

```hcl
name                = "intelibill-${var.env}-env"
resource_group_name = var.resource_group_name
location            = var.location
logs_destination    = "azure-monitor"

workload_profile {
  name                  = "Consumption"
  workload_profile_type = "Consumption"
  minimum_count         = 0
  maximum_count         = 0
}
```

- [ ] **Step 5: Implement API and web apps**

Both apps use `workload_profile_name = "Consumption"`, `revision_mode = "Single"`, one container, an HTTP scale rule named `http-concurrency` with `concurrent_requests = "50"`, 100 percent traffic to the latest revision, and `allow_insecure_connections = false`.

Attach `identity_ids = [var.app_identity.id]` only to the API. Add startup, liveness, and readiness HTTP probes with the exact paths/ports in the contract. Give startup a failure threshold of `30`, interval `10`, and timeout `5`; give liveness and readiness a failure threshold of `3`, interval `10`, and timeout `5`.

Use a dynamic optional Key Vault reference:

```hcl
dynamic "secret" {
  for_each = var.new_relic_api_key_secret_name == null ? [] : [var.new_relic_api_key_secret_name]

  content {
    name                = "new-relic-api-key"
    identity            = var.app_identity.id
    key_vault_secret_id = "${var.key_vault.vault_uri}secrets/${secret.value}"
  }
}
```

Map `Observability__NewRelic__ApiKey` to `secret_name = "new-relic-api-key"` only when the optional input is non-null.

Add:

```hcl
lifecycle {
  ignore_changes = [template[0].container[0].image]
}
```

to API and web resources only after verifying the provider's nested path in a clean plan.

- [ ] **Step 6: Implement the manual migration job**

Create `azurerm_container_app_job.migrate` using the supplied migrator identity, the `Consumption` workload profile, `replica_retry_limit = 0`, `replica_timeout_in_seconds = 1800`, and:

```hcl
manual_trigger_config {
  parallelism              = 1
  replica_completion_count = 1
}
```

Use one `0.5` CPU, `1Gi` container and the migration environment map. Add the same image-only lifecycle rule. Do not start the job.

- [ ] **Step 7: Implement diagnostics, RBAC, and outputs**

Create one diagnostic setting for the Container Apps environment and one for the environment Key Vault. Route both to `var.log_analytics_id`.

Build the deploy scopes and assignments as:

```hcl
locals {
  deploy_scopes = {
    api     = azurerm_container_app.api.id
    web     = azurerm_container_app.web.id
    migrate = azurerm_container_app_job.migrate.id
  }
}

resource "azurerm_role_assignment" "deploy" {
  for_each = local.deploy_scopes

  scope              = each.value
  role_definition_id = var.deploy_role_definition_id
  principal_id       = var.deploy_principal_id
}
```

Publish environment name/ID/default domain; API, web, and job names/IDs/FQDNs; `observability_secret_configured`; and:

```hcl
output "outbound_ip_addresses" {
  description = "Complete advertised outbound IPv4 union for database firewall reconciliation"
  value = setunion(
    toset(azurerm_container_app.api.outbound_ip_addresses),
    toset(azurerm_container_app.web.outbound_ip_addresses),
    toset(azurerm_container_app_job.migrate.outbound_ip_addresses),
  )
}
```

- [ ] **Step 8: Run GREEN verification**

Run:

```bash
tofu fmt -recursive .tofu/modules/environment-infrastructure
tofu -chdir=.tofu/modules/environment-infrastructure test
tofu -chdir=.tofu/modules/environment-infrastructure validate
```

Expected: every dev, prod, optional-secret, and invalid-image run passes.

- [ ] **Step 9: Commit**

```bash
git add .tofu/modules/environment-infrastructure
git commit -m "feat(infra): define Container Apps workloads"
```

---

## Task 3: Wire Dev and Prod Without Replacing Existing Foundations

**Files:**

- Modify: `.tofu/envs/dev/main.tf`
- Modify: `.tofu/envs/dev/variables.tf`
- Modify: `.tofu/envs/dev/outputs.tf`
- Modify: `.tofu/envs/dev/terraform.tfvars.example`
- Modify: `.tofu/envs/prod/main.tf`
- Modify: `.tofu/envs/prod/variables.tf`
- Modify: `.tofu/envs/prod/outputs.tf`
- Modify: `.tofu/envs/prod/terraform.tfvars.example`
- Create: `.tofu/envs/dev/tests/contract.tftest.hcl`
- Create: `.tofu/envs/prod/tests/contract.tftest.hcl`

- [ ] **Step 1: Add failing root contract tests**

For each root, mock AzureRM and override deterministic data lookups for:

- resource group `intelibill-shared`;
- Log Analytics workspace `intelibill-logs`;
- deploy identity `id-gha-deploy-dev` or `id-gha-deploy-prod`;
- subscription-scoped role `Intelibill Container App Deployer`.

Assert that the environment module receives the identity objects directly from `module.workload_identities`, the correct Key Vault outputs, database `intelibill_dev` or `intelibill_prod`, and the immutable bootstrap image. Assert the root output includes the module's workload resources and outbound set.

- [ ] **Step 2: Run both tests and confirm RED**

Run:

```bash
tofu -chdir=.tofu/envs/dev test
tofu -chdir=.tofu/envs/prod test
```

Expected: failure because neither root calls the environment module.

- [ ] **Step 3: Add deterministic lookups and module calls**

Add these data sources in both roots with their environment-specific deploy identity names:

```hcl
data "azurerm_log_analytics_workspace" "shared" {
  name                = "intelibill-logs"
  resource_group_name = data.azurerm_resource_group.main.name
}

data "azurerm_user_assigned_identity" "deploy" {
  name                = "id-gha-deploy-${local.env}"
  resource_group_name = data.azurerm_resource_group.main.name
}

data "azurerm_subscription" "current" {}

data "azurerm_role_definition" "container_app_deployer" {
  name  = "Intelibill Container App Deployer"
  scope = data.azurerm_subscription.current.id
}
```

Introduce `local.env` and `local.database_name` in each root, then call the module with:

```hcl
app_identity             = module.workload_identities.identities.app
migrator_identity        = module.workload_identities.identities.migrator
key_vault                = module.key_vault
log_analytics_id         = data.azurerm_log_analytics_workspace.shared.id
deploy_principal_id      = data.azurerm_user_assigned_identity.deploy.principal_id
deploy_role_definition_id = data.azurerm_role_definition.container_app_deployer.id
```

Pass database host `intelibill-pg-01.postgres.database.azure.com`, port `5432`, max pool size `12`, and the environment's explicit database name.

- [ ] **Step 4: Add narrowly scoped root inputs**

Add:

```hcl
variable "bootstrap_image" {
  description = "Digest-pinned bootstrap image; deployment pipelines later own workload image changes"
  type        = string
  default     = "ghcr.io/mendhak/http-https-echo@sha256:0fefe04350131d7bb28355e3bf037062643e45f4a8a32f23679529e1b09d8ce4"
}

variable "new_relic_api_key_secret_name" {
  description = "Out-of-band Key Vault secret name, or null until the integration key exists"
  type        = string
  default     = null
}
```

Document only the optional secret name in both example tfvars; never document or accept its value.

- [ ] **Step 5: Publish the environment output**

Add one `container_apps` object containing environment, API, web, migration-job, `outbound_ip_addresses`, and `observability_secret_configured`. Keep identity and Key Vault outputs unchanged.

- [ ] **Step 6: Run GREEN tests and validation**

Run:

```bash
tofu fmt -recursive .tofu/envs/dev .tofu/envs/prod
tofu -chdir=.tofu/envs/dev init
tofu -chdir=.tofu/envs/prod init
tofu -chdir=.tofu/envs/dev test
tofu -chdir=.tofu/envs/prod test
tofu -chdir=.tofu/envs/dev validate
tofu -chdir=.tofu/envs/prod validate
```

Expected: tests and validation pass. Do not create live dev/prod plans yet: their
workspace data source must resolve the shared workspace created in Task 8,
Step 3.

- [ ] **Step 7: Commit**

```bash
git add .tofu/envs/dev .tofu/envs/prod
git commit -m "feat(infra): wire dev and prod workloads"
```

---

## Task 4: Derive Exact PostgreSQL Rules From Environment State

**Files:**

- Create: `.tofu/modules/container-apps-egress/variables.tf`
- Create: `.tofu/modules/container-apps-egress/main.tf`
- Create: `.tofu/modules/container-apps-egress/outputs.tf`
- Create: `.tofu/modules/container-apps-egress/tests/contract.tftest.hcl`
- Create: `.tofu/envs/shared/tests/contract.tftest.hcl`
- Modify: `.tofu/envs/shared/main.tf`
- Modify: `.tofu/envs/shared/variables.tf`
- Modify: `.tofu/envs/shared/outputs.tf`
- Modify: `.tofu/envs/shared/terraform.tfvars.example`

- [ ] **Step 1: Write failing egress transformation tests**

Test these cases with `command = plan`:

- dev advertised `20.10.0.1`, `20.10.0.2`, `20.10.0.3` creates three exact rules named `container-apps-dev-20-10-0-1` through `container-apps-dev-20-10-0-3`;
- prod advertised `20.20.0.1` remains environment-labelled;
- duplicate addresses within one environment collapse to one rule;
- retained address `20.10.0.9` is added alongside current addresses;
- an empty initial advertised map yields an empty rule map so the workspace-first bootstrap can proceed;
- `0.0.0.0`, `255.255.255.255`, malformed IPv4, IPv6, and unknown environment keys fail variable validation.

Use this output contract:

```hcl
output "allowed_ip_rules" {
  value = local.allowed_ip_rules
}
```

- [ ] **Step 2: Run the egress module test and confirm RED**

Run:

```bash
tofu -chdir=.tofu/modules/container-apps-egress init -backend=false
tofu -chdir=.tofu/modules/container-apps-egress test
```

Expected: failure because the egress module does not exist.

- [ ] **Step 3: Implement strict IPv4 validation and stable rule naming**

Declare both inputs as `map(set(string))`, allow only `dev` and `prod` keys, require dotted-decimal IPv4 accepted by `cidrnetmask("${ip}/32")`, and reject the two broad sentinel addresses.

Build rules with:

```hcl
locals {
  environments = setunion(
    toset(keys(var.advertised_outbound_ip_addresses)),
    toset(keys(var.retained_outbound_ip_addresses)),
  )

  address_records = flatten([
    for env in local.environments : [
      for ip in setunion(
        lookup(var.advertised_outbound_ip_addresses, env, toset([])),
        lookup(var.retained_outbound_ip_addresses, env, toset([])),
      ) : {
        env = env
        ip  = ip
      }
    ]
  ])

  allowed_ip_rules = {
    for record in local.address_records :
    "container-apps-${record.env}-${replace(record.ip, ".", "-")}" => {
      start_ip = record.ip
      end_ip   = record.ip
    }
  }
}
```

- [ ] **Step 4: Write the failing shared-root contract**

Mock AzureRM and override both remote-state data sources with known dev/prod
output objects. Assert that the shared root plans exact firewall resources for
all four test addresses, that no broad rule exists, and that an explicitly
supplied operator rule is preserved.

Run:

```bash
tofu -chdir=.tofu/envs/shared test
```

Expected: failure because the shared root does not yet read environment state
or pass generated rules to the database module.

- [ ] **Step 5: Add remote-state reads and merge ownership in shared**

Read the already-existing dev/prod state containers:

```hcl
data "terraform_remote_state" "dev" {
  backend = "azurerm"
  config = {
    resource_group_name  = "intelibill-shared"
    storage_account_name = "intelibilltfstate01"
    container_name       = "tfstate-dev"
    key                  = "dev.tfstate"
    use_azuread_auth     = true
  }
  defaults = {
    container_apps = {
      outbound_ip_addresses = []
    }
  }
}
```

Mirror this for `tfstate-prod/prod.tfstate`. Pass the two output sets into the egress module, add:

```hcl
variable "retained_container_apps_outbound_ips" {
  description = "Previously advertised exact addresses retained for one reviewed transition apply"
  type        = map(set(string))
  default = {
    dev  = []
    prod = []
  }
}
```

and change the database call to:

```hcl
allowed_ip_rules = merge(
  var.allowed_ip_rules,
  module.container_apps_egress.allowed_ip_rules,
)
```

Keep `allowed_ip_rules` for explicitly reviewed temporary operator access. If its key collides with a generated key, the generated Container Apps rule must win by appearing second in `merge`.

Expose `container_apps_firewall_ips` and `retained_container_apps_outbound_ips` outputs for operational verification. Both outputs contain addresses only.

- [ ] **Step 6: Run GREEN verification**

Run:

```bash
tofu fmt -recursive .tofu/modules/container-apps-egress .tofu/envs/shared
tofu -chdir=.tofu/modules/container-apps-egress test
tofu -chdir=.tofu/envs/shared init
tofu -chdir=.tofu/envs/shared test
tofu -chdir=.tofu/envs/shared validate
```

Expected: module and root tests pass; shared validates.

- [ ] **Step 7: Commit**

```bash
git add .tofu/modules/container-apps-egress .tofu/envs/shared
git commit -m "feat(infra): reconcile Container Apps egress"
```

---

## Task 5: Add an Executable Allowlist Drift Checker

**Files:**

- Create: `.tofu/scripts/check-container-app-egress.sh`
- Create: `.tofu/scripts/tests/check-container-app-egress.test.sh`
- Create: `.tofu/scripts/tests/fixtures/advertised-ok.json`
- Create: `.tofu/scripts/tests/fixtures/advertised-empty.json`
- Create: `.tofu/scripts/tests/fixtures/firewall-ok.json`
- Create: `.tofu/scripts/tests/fixtures/firewall-missing.json`
- Create: `.tofu/scripts/tests/fixtures/firewall-broad.json`
- Create: `.tofu/scripts/tests/fixtures/firewall-malformed.json`
- Create: `.tofu/scripts/tests/fixtures/firewall-stale.json`
- Create: `.tofu/scripts/tests/fixtures/retained.json`

- [ ] **Step 1: Write fixture-driven failing shell tests**

The checker interface is:

```text
check-container-app-egress.sh
  [--advertised-file FILE]
  [--firewall-file FILE]
  [--retained-file FILE]
  [--environment dev|prod]
  [--resource-group NAME]
  [--postgres-server NAME]
```

Without fixture files it queries:

- `az containerapp show` for `intelibill-{dev,prod}-{api,web}`;
- `az containerapp job show` for `intelibill-{dev,prod}-migrate`;
- `az postgres flexible-server firewall-rule list` for `intelibill-pg-01`.

`--environment` is repeatable. With no occurrence the checker verifies both
`dev` and `prod`; the initial staged checkpoint uses `--environment dev`.

The normalized advertised/retained fixture schema is:

```json
{
  "dev": ["20.10.0.1", "20.10.0.2", "20.10.0.3"],
  "prod": ["20.20.0.1"]
}
```

The firewall fixture is the Azure CLI array shape with `name`, `startIpAddress`, and `endIpAddress`.

The test harness must assert:

- matching exact rules exit `0`;
- empty advertised union exits nonzero;
- missing current address exits nonzero;
- broad `0.0.0.0` to `255.255.255.255` exits nonzero;
- malformed or non-identical managed rule range exits nonzero;
- stale `container-apps-*` rule exits nonzero when no retained input is supplied;
- the same rule exits `0` when explicitly present in the retained input;
- an operator rule with a non-`container-apps-` name does not satisfy a missing managed address.
- an invalid or repeated `--environment` value exits nonzero.

- [ ] **Step 2: Run the shell test and confirm RED**

Run:

```bash
bash .tofu/scripts/tests/check-container-app-egress.test.sh
```

Expected: failure because the checker does not exist.

- [ ] **Step 3: Implement normalization and comparison**

Implement with `set -euo pipefail`. Require `az` only when live files are absent and require `jq` in all modes. Normalize with `jq -S` and:

- validate every advertised and retained entry as an IPv4 address;
- union API, web, and migration addresses per environment;
- limit both live discovery and managed-rule comparison to the selected
  environment set;
- fail if the total advertised set is empty;
- derive the expected rule name with dots replaced by hyphens;
- require every expected rule to have identical start/end values;
- reject any broad rule regardless of its name;
- reject malformed `container-apps-*` ranges;
- reject unreviewed stale `container-apps-*` rules;
- print missing, broad, malformed, and stale findings to stderr;
- print a concise address/rule count on success.

The live collection must fail closed if any app/job lookup fails or returns a non-array.

- [ ] **Step 4: Run GREEN verification and make scripts executable**

Run:

```bash
chmod +x .tofu/scripts/check-container-app-egress.sh .tofu/scripts/tests/check-container-app-egress.test.sh
bash .tofu/scripts/tests/check-container-app-egress.test.sh
```

Expected: all fixture cases pass.

- [ ] **Step 5: Commit**

```bash
git add .tofu/scripts
git commit -m "feat(infra): detect egress allowlist drift"
```

---

## Task 6: Narrow Routine Deploy Permissions

**Files:**

- Create: `.tofu/bootstrap/tests/deploy-role.test.sh`
- Modify: `.tofu/bootstrap/role-assignments.tf`

- [ ] **Step 1: Write the failing static security contract**

The test must:

```bash
#!/usr/bin/env bash
set -euo pipefail

role_file="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/role-assignments.tf"
actions="$(sed -n '/^[[:space:]]*actions = \[/,/^[[:space:]]*\]/p' "$role_file")"

rg -q '"Microsoft.App/jobs/write"' "$role_file"
! rg -q 'resource "azurerm_role_assignment" "deploy"' "$role_file"
! rg -q 'Microsoft.App/containerApps/delete' <<<"$actions"
! rg -q 'Microsoft.App/jobs/delete' <<<"$actions"
! rg -q 'listSecrets' <<<"$actions"
```

- [ ] **Step 2: Run the contract and confirm RED**

Run:

```bash
bash .tofu/bootstrap/tests/deploy-role.test.sh
```

Expected: failure because `jobs/write` is absent and the group-scoped deploy assignment still exists.

- [ ] **Step 3: Update the role and remove only the temporary assignment**

Add `"Microsoft.App/jobs/write"` to `azurerm_role_definition.container_app_deployer.permissions.actions`.

Delete only `azurerm_role_assignment.deploy`. Preserve:

- both deploy identities and federated credentials;
- the custom role itself;
- infrastructure apply assignments;
- plan assignments;
- state-container assignments.

- [ ] **Step 4: Run GREEN verification and inspect a saved bootstrap plan**

Run:

```bash
chmod +x .tofu/bootstrap/tests/deploy-role.test.sh
bash .tofu/bootstrap/tests/deploy-role.test.sh
tofu fmt -recursive .tofu/bootstrap
tofu -chdir=.tofu/bootstrap validate
tofu -chdir=.tofu/bootstrap plan -out=phase10-bootstrap.tfplan
tofu -chdir=.tofu/bootstrap show -json phase10-bootstrap.tfplan | jq -e '
  [.resource_changes[]
   | select(.change.actions | index("delete"))
   | .address]
  | sort
  == [
    "azurerm_role_assignment.deploy[\"dev\"]",
    "azurerm_role_assignment.deploy[\"prod\"]"
  ]'
```

Expected: validation passes; the only deletes are the two temporary group-scoped assignments. The custom role updates in place and no identity changes.

- [ ] **Step 5: Commit**

```bash
git add .tofu/bootstrap/role-assignments.tf .tofu/bootstrap/tests/deploy-role.test.sh
git commit -m "fix(infra): narrow Container Apps deploy access"
```

---

## Task 7: Run Complete Local Verification

**Files:**

- Modify only if a verification failure exposes a Phase 10 defect.

- [ ] **Step 1: Format and run every infrastructure contract**

Run:

```bash
tofu fmt -check -recursive .tofu
tofu -chdir=.tofu/bootstrap init
tofu -chdir=.tofu/envs/shared init
tofu -chdir=.tofu/envs/dev init
tofu -chdir=.tofu/envs/prod init
tofu -chdir=.tofu/modules/shared-monitoring test
tofu -chdir=.tofu/modules/environment-infrastructure test
tofu -chdir=.tofu/modules/container-apps-egress test
tofu -chdir=.tofu/envs/shared test
tofu -chdir=.tofu/envs/dev test
tofu -chdir=.tofu/envs/prod test
bash .tofu/scripts/tests/check-container-app-egress.test.sh
bash .tofu/bootstrap/tests/deploy-role.test.sh
```

- [ ] **Step 2: Validate all OpenTofu roots**

Run:

```bash
tofu -chdir=.tofu/bootstrap validate
tofu -chdir=.tofu/envs/shared validate
tofu -chdir=.tofu/envs/dev validate
tofu -chdir=.tofu/envs/prod validate
```

- [ ] **Step 3: Run application regression checks**

Run:

```bash
dotnet build src/backend/Intelibill.slnx
dotnet test tests/backend/unit/Intelibill.Domain.Unit.Tests
dotnet test tests/backend/unit/Intelibill.Application.Unit.Tests
dotnet test tests/backend/unit/Intelibill.Api.Unit.Tests
bun run --cwd src/frontend build
bun run --cwd src/frontend test
bash -lc 'cd src/mobile/android/intelibill_mobile && flutter analyze && flutter test'
```

Run the integration suite when Docker is healthy:

```bash
docker info
dotnet test tests/backend/integration/Intelibill.Integration.Tests
```

If the documented concurrent purchase-order test alone flakes, rerun that exact test in isolation and record both results. Do not conceal any other failure.

- [ ] **Step 4: Refresh the repository graph**

Run:

```bash
graphify update . --no-viz
```

If this installation still attempts HTML generation and reports the known graph-size failure after confirming the AST graph is current, record that separately from the infrastructure verification.

- [ ] **Step 5: Check the worktree**

Run:

```bash
git status --short
git diff --check
git log --oneline -7
```

Expected: no uncommitted source changes, no whitespace errors, and one intentional commit per green task.

---

## Task 8: Apply Phase 10 to Azure in the Approved Stages

**Files:**

- Generated and untracked: `.tofu/envs/shared/phase10-shared-*.tfplan`
- Generated and untracked: `.tofu/envs/dev/phase10-dev.tfplan`
- Generated and untracked: `.tofu/envs/prod/phase10-prod.tfplan`
- Generated and untracked: `.tofu/bootstrap/phase10-bootstrap.tfplan`

- [ ] **Step 1: Record the immutable identity baseline**

Run and retain the output outside git:

```bash
tofu -chdir=.tofu/envs/dev output -json identities | jq -S
tofu -chdir=.tofu/envs/prod output -json identities | jq -S
```

The principal IDs must match:

```text
dev app       639aa307-3394-4da0-a5bf-bcecf7a36632
dev migrator  d4463264-a136-467a-af6d-e174d99dab26
prod app      122b2c9e-7ec7-4b84-9d80-5267092eb0a7
prod migrator 051aad50-f111-4c7d-8ba6-75630cba1b64
```

- [ ] **Step 2: Register and verify the Container Apps provider**

Run:

```bash
az account set --subscription cef6a1af-9d98-437f-b99c-ad6d24e5631c
az provider register --namespace Microsoft.App --wait
az provider show --namespace Microsoft.App --query registrationState -o tsv
```

Expected: `Registered`. Stop before any apply otherwise.

- [ ] **Step 3: Create shared monitoring first**

Run:

```bash
tofu -chdir=.tofu/envs/shared plan -out=phase10-shared-monitoring.tfplan
tofu -chdir=.tofu/envs/shared show phase10-shared-monitoring.tfplan
tofu -chdir=.tofu/envs/shared apply phase10-shared-monitoring.tfplan
```

The reviewed plan may create the workspace and PostgreSQL diagnostics. It must not replace PostgreSQL, databases, DNS, or state resources.

- [ ] **Step 4: Create dev workloads and scoped roles**

Regenerate the saved plan after the shared workspace exists:

```bash
tofu -chdir=.tofu/envs/dev plan -out=phase10-dev.tfplan
tofu -chdir=.tofu/envs/dev show phase10-dev.tfplan
tofu -chdir=.tofu/envs/dev show -json phase10-dev.tfplan | jq -e '
  [.resource_changes[]
   | select((.address | test("workload_identities|key_vault")) and
            (.change.actions | index("delete")))]
  | length == 0'
tofu -chdir=.tofu/envs/dev apply phase10-dev.tfplan
```

Expected: one environment, API, web, migration job, two diagnostic settings,
and three scoped role assignments; the replacement guard returns `true`.

- [ ] **Step 5: Add dev firewall rules from applied state**

Run:

```bash
tofu -chdir=.tofu/envs/shared plan -out=phase10-shared-dev-egress.tfplan
tofu -chdir=.tofu/envs/shared show phase10-shared-dev-egress.tfplan
tofu -chdir=.tofu/envs/shared apply phase10-shared-dev-egress.tfplan
.tofu/scripts/check-container-app-egress.sh --environment dev
```

Expected: the dev advertised union is nonempty and every dev address has one
exact managed rule. Prod remains outside this staged invocation until its
workloads exist.

- [ ] **Step 6: Create prod workloads and scoped roles**

Run:

```bash
tofu -chdir=.tofu/envs/prod plan -out=phase10-prod.tfplan
tofu -chdir=.tofu/envs/prod show phase10-prod.tfplan
tofu -chdir=.tofu/envs/prod show -json phase10-prod.tfplan | jq -e '
  [.resource_changes[]
   | select((.address | test("workload_identities|key_vault")) and
            (.change.actions | index("delete")))]
  | length == 0'
tofu -chdir=.tofu/envs/prod apply phase10-prod.tfplan
```

Expected: the prod equivalents only; no dev resource changes and no identity or Key Vault replacement.

- [ ] **Step 7: Reconcile the combined egress set**

Run:

```bash
tofu -chdir=.tofu/envs/shared plan -out=phase10-shared-all-egress.tfplan
tofu -chdir=.tofu/envs/shared show phase10-shared-all-egress.tfplan
tofu -chdir=.tofu/envs/shared apply phase10-shared-all-egress.tfplan
.tofu/scripts/check-container-app-egress.sh
```

Expected: every current dev/prod API, web, and migration-job outbound address has one exact managed firewall rule; no broad or stale managed rule exists.

- [ ] **Step 8: Remove the temporary group grants last**

First prove all six scoped assignments exist:

```bash
set -euo pipefail

resource_group="intelibill-shared"
deploy_role="Intelibill Container App Deployer"
verified_assignment_count=0

for environment in dev prod; do
  deploy_principal_id="$(
    az identity show \
      --resource-group "${resource_group}" \
      --name "id-gha-deploy-${environment}" \
      --query principalId \
      -o tsv
  )"

  for component in api web migrate; do
    if [[ "${component}" == "migrate" ]]; then
      resource_id="$(
        az containerapp job show \
          --resource-group "${resource_group}" \
          --name "intelibill-${environment}-migrate" \
          --query id \
          -o tsv
      )"
    else
      resource_id="$(
        az containerapp show \
          --resource-group "${resource_group}" \
          --name "intelibill-${environment}-${component}" \
          --query id \
          -o tsv
      )"
    fi

    role_assignments="$(
      az role assignment list \
        --scope "${resource_id}" \
        --query "[?roleDefinitionName=='${deploy_role}'].{principalId:principalId,scope:scope}" \
        -o json
    )"

    if ! jq -e \
      --arg expected_scope "${resource_id}" \
      --arg expected_principal_id "${deploy_principal_id}" \
      '[
        .[] |
        select((.scope | ascii_downcase) == ($expected_scope | ascii_downcase))
      ] as $exact_scope_assignments |
      ($exact_scope_assignments | length == 1) and
      (
        ($exact_scope_assignments[0].principalId | ascii_downcase) ==
        ($expected_principal_id | ascii_downcase)
      )' \
      <<<"${role_assignments}" >/dev/null; then
      printf 'Expected exactly one deploy assignment for %s at %s\n' \
        "${deploy_principal_id}" "${resource_id}" >&2
      exit 1
    fi

    jq \
      --arg expected_scope "${resource_id}" \
      '[
        .[] |
        select((.scope | ascii_downcase) == ($expected_scope | ascii_downcase))
      ]' \
      <<<"${role_assignments}"
    verified_assignment_count=$((verified_assignment_count + 1))
  done
done

[[ "${verified_assignment_count}" -eq 6 ]]
```

The scope and principal GUID comparisons normalize only case because Azure
resource IDs are case-insensitive; parent, child, unrelated scopes, and other
principals cannot satisfy the proof.
Read-only validation on 2026-07-27 exercised this shell/JMESPath sequence
against all six resources after the prod apply. Each dev scope had exactly one
assignment for deploy principal `4475d63e-2970-455f-935d-f4de25a0d7d4`, and
each prod scope had exactly one assignment for deploy principal
`be616680-7dd9-450a-85e7-cf52f28e05a4`.

Then apply the already-reviewed bootstrap plan only after confirming three dev and three prod resource scopes:

```bash
tofu -chdir=.tofu/bootstrap plan -out=phase10-bootstrap.tfplan
tofu -chdir=.tofu/bootstrap show phase10-bootstrap.tfplan
tofu -chdir=.tofu/bootstrap apply phase10-bootstrap.tfplan
```

Expected: custom role update in place and deletion of only the dev/prod group-scoped assignments.

---

## Task 9: Verify Live State, Document Completion, and Commit Evidence

**Files:**

- Modify: `docs/phase-10-handoff.md`
- Modify: `docs/superpowers/specs/2026-07-26-phase-10-environment-infrastructure-design.md`

- [ ] **Step 1: Confirm workload ingress and scaling**

Run:

```bash
for environment in dev prod; do
  az containerapp show \
    --resource-group intelibill-shared \
    --name "intelibill-${environment}-api" \
    --query "{name:name,external:properties.configuration.ingress.external,fqdn:properties.configuration.ingress.fqdn,min:properties.template.scale.minReplicas,max:properties.template.scale.maxReplicas}" \
    -o json
  az containerapp show \
    --resource-group intelibill-shared \
    --name "intelibill-${environment}-web" \
    --query "{name:name,external:properties.configuration.ingress.external,fqdn:properties.configuration.ingress.fqdn,min:properties.template.scale.minReplicas,max:properties.template.scale.maxReplicas}" \
    -o json
done
```

Expected: API external is `false`, web external is `true`, and both max values are `1`.

- [ ] **Step 2: Confirm jobs, diagnostics, allowlist, and role scopes**

Run:

```bash
set -euo pipefail

for environment in dev prod; do
  az containerapp job show \
    --resource-group intelibill-shared \
    --name "intelibill-${environment}-migrate" \
    --query "{name:name,trigger:properties.configuration.triggerType,identity:identity.userAssignedIdentities}" \
    -o json
done

.tofu/scripts/check-container-app-egress.sh

for target in \
  "$(az containerapp env show --resource-group intelibill-shared --name intelibill-dev-env --query id -o tsv)" \
  "$(az containerapp env show --resource-group intelibill-shared --name intelibill-prod-env --query id -o tsv)" \
  "$(az postgres flexible-server show --resource-group intelibill-shared --name intelibill-pg-01 --query id -o tsv)" \
  "$(az keyvault show --resource-group intelibill-shared --name intelibill-dev-kv --query id -o tsv)" \
  "$(az keyvault show --resource-group intelibill-shared --name intelibill-prod-kv --query id -o tsv)"
do
  az monitor diagnostic-settings list --resource "$target" -o json
done

resource_group="intelibill-shared"
deploy_role="Intelibill Container App Deployer"
verified_assignment_count=0

for environment in dev prod; do
  deploy_principal_id="$(
    az identity show \
      --resource-group "${resource_group}" \
      --name "id-gha-deploy-${environment}" \
      --query principalId \
      -o tsv
  )"

  for component in api web migrate; do
    if [[ "${component}" == "migrate" ]]; then
      resource_id="$(
        az containerapp job show \
          --resource-group "${resource_group}" \
          --name "intelibill-${environment}-migrate" \
          --query id \
          -o tsv
      )"
    else
      resource_id="$(
        az containerapp show \
          --resource-group "${resource_group}" \
          --name "intelibill-${environment}-${component}" \
          --query id \
          -o tsv
      )"
    fi

    role_assignments="$(
      az role assignment list \
        --scope "${resource_id}" \
        --query "[?roleDefinitionName=='${deploy_role}'].{principalId:principalId,scope:scope}" \
        -o json
    )"

    if ! jq -e \
      --arg expected_scope "${resource_id}" \
      --arg expected_principal_id "${deploy_principal_id}" \
      '[
        .[] |
        select((.scope | ascii_downcase) == ($expected_scope | ascii_downcase))
      ] as $exact_scope_assignments |
      ($exact_scope_assignments | length == 1) and
      (
        ($exact_scope_assignments[0].principalId | ascii_downcase) ==
        ($expected_principal_id | ascii_downcase)
      )' \
      <<<"${role_assignments}" >/dev/null; then
      printf 'Expected exactly one deploy assignment for %s at %s\n' \
        "${deploy_principal_id}" "${resource_id}" >&2
      exit 1
    fi

    jq \
      --arg expected_scope "${resource_id}" \
      '[
        .[] |
        select((.scope | ascii_downcase) == ($expected_scope | ascii_downcase))
      ]' \
      <<<"${role_assignments}"
    verified_assignment_count=$((verified_assignment_count + 1))
  done
done

[[ "${verified_assignment_count}" -eq 6 ]]

resource_group_id="$(
  az group show \
    --name "${resource_group}" \
    --query id \
    -o tsv
)"
group_role_assignments="$(
  az role assignment list \
    --scope "${resource_group_id}" \
    --query "[?roleDefinitionName=='${deploy_role}'].{principalId:principalId,scope:scope}" \
    -o json
)"

if ! jq -e \
  --arg expected_scope "${resource_group_id}" \
  '[.[] | select((.scope | ascii_downcase) == ($expected_scope | ascii_downcase))] | length == 0' \
  <<<"${group_role_assignments}" >/dev/null; then
  printf 'Deploy assignment still exists at resource-group scope %s\n' \
    "${resource_group_id}" >&2
  exit 1
fi
```

Read-only validation on 2026-07-27 confirmed the resource-group query works
with Azure CLI 2.88 and finds the two temporary group-scoped grants before
bootstrap cleanup. Therefore, the zero-assignment assertion is intentionally a
post-Task 8 Step 8 check.

Verify the exact categories from Tasks 1 and 2. The role proof must find exactly
six resource-scoped assignments bound to the matching environment deploy
identity and zero deploy assignments whose scope equals the resource-group ID.

- [ ] **Step 3: Confirm identities and idempotence**

Run:

```bash
tofu -chdir=.tofu/envs/dev output -json identities | jq -S
tofu -chdir=.tofu/envs/prod output -json identities | jq -S
tofu -chdir=.tofu/envs/shared plan -detailed-exitcode
tofu -chdir=.tofu/envs/dev plan -detailed-exitcode
tofu -chdir=.tofu/envs/prod plan -detailed-exitcode
tofu -chdir=.tofu/bootstrap plan -detailed-exitcode
```

Expected: all four principal IDs match the baseline and each plan exits `0`. Exit `2` requires inspection and reconciliation before completion.

- [ ] **Step 4: Verify logs after propagation**

Resolve the workspace customer ID and run:

```bash
workspace_id="$(
  az monitor log-analytics workspace show \
    --resource-group intelibill-shared \
    --workspace-name intelibill-logs \
    --query customerId \
    -o tsv
)"

az monitor log-analytics query \
  --workspace "$workspace_id" \
  --analytics-query '
    union isfuzzy=true
      ContainerAppConsoleLogs_CL,
      ContainerAppSystemLogs_CL,
      ContainerAppHTTPLogs_CL,
      AzureDiagnostics,
      AzureMetrics
    | where TimeGenerated > ago(2h)
    | summarize Records=count() by Type, Category=tostring(Category)
    | order by Type asc, Category asc
  ' \
  -o table
```

Confirm recent:

- Container Apps console, system, and HTTP records from dev and prod;
- PostgreSQL log or metric records;
- Key Vault audit or metric records.

Do not manufacture traffic by starting the migration job. It remains manual and unexecuted until the real migration image is deployed in Phase 11.

- [ ] **Step 5: Update the handoff and design status**

Record:

- completion date and final commit;
- created resource names;
- current advertised address sets;
- confirmation that all firewall rules are exact;
- deploy assignment scopes;
- unchanged identity principal IDs;
- optional New Relic secret remains disabled if it still does not exist;
- the two-apply retained-address procedure for future egress changes;
- actual verification results and any separately documented graph visualization limitation.

Mark the design status `Implemented and verified` only after every acceptance check passes.

- [ ] **Step 6: Commit documentation**

```bash
git add docs/phase-10-handoff.md docs/superpowers/specs/2026-07-26-phase-10-environment-infrastructure-design.md
git commit -m "docs(infra): record Phase 10 verification"
```

- [ ] **Step 7: Final completion check**

Run:

```bash
git status --short --branch
git diff --check
git log --oneline -10
```

Expected: clean worktree, branch ahead only by intentional Phase 10 commits, all local and live verification evidence recorded.
