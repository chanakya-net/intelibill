mock_provider "azurerm" {
  mock_resource "azurerm_container_app_environment" {
    defaults = {
      id             = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/intelibill-shared/providers/Microsoft.App/managedEnvironments/intelibill-env"
      default_domain = "mock.centralindia.azurecontainerapps.io"
    }
  }

  mock_resource "azurerm_container_app" {
    defaults = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/intelibill-shared/providers/Microsoft.App/containerApps/intelibill-app"
    }
  }

  mock_resource "azurerm_container_app_job" {
    defaults = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/intelibill-shared/providers/Microsoft.App/jobs/intelibill-migrate"
    }
  }
}

variables {
  env                       = "dev"
  resource_group_name       = "intelibill-shared"
  location                  = "centralindia"
  log_analytics_id          = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/intelibill-shared/providers/Microsoft.OperationalInsights/workspaces/intelibill-logs"
  deploy_principal_id       = "10000000-0000-0000-0000-000000000001"
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

run "dev_contract" {
  command = plan

  override_resource {
    target = azurerm_container_app_environment.main
    values = {
      id             = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/intelibill-shared/providers/Microsoft.App/managedEnvironments/intelibill-dev-env"
      default_domain = "mock.centralindia.azurecontainerapps.io"
    }
  }

  override_resource {
    target = azurerm_container_app.api
    values = {
      id                    = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/intelibill-shared/providers/Microsoft.App/containerApps/intelibill-dev-api"
      outbound_ip_addresses = ["20.10.0.1"]
    }
  }

  override_resource {
    target = azurerm_container_app.web
    values = {
      id                    = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/intelibill-shared/providers/Microsoft.App/containerApps/intelibill-dev-web"
      outbound_ip_addresses = ["20.10.0.2"]
    }
  }

  override_resource {
    target = azurerm_container_app_job.migrate
    values = {
      id                    = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/intelibill-shared/providers/Microsoft.App/jobs/intelibill-dev-migrate"
      outbound_ip_addresses = ["20.10.0.3"]
    }
  }

  assert {
    condition = (
      azurerm_container_app_environment.main.name == "intelibill-dev-env" &&
      azurerm_container_app_environment.main.logs_destination == "azure-monitor" &&
      length(azurerm_container_app_environment.main.workload_profile) == 1 &&
      one(azurerm_container_app_environment.main.workload_profile).name == "Consumption" &&
      one(azurerm_container_app_environment.main.workload_profile).workload_profile_type == "Consumption"
    )
    error_message = "The dev Container Apps environment contract changed."
  }

  assert {
    condition = (
      azurerm_container_app.api.name == "intelibill-dev-api" &&
      azurerm_container_app.web.name == "intelibill-dev-web" &&
      azurerm_container_app_job.migrate.name == "intelibill-dev-migrate"
    )
    error_message = "Environment workload names must be stable."
  }

  assert {
    condition = (
      azurerm_container_app.api.revision_mode == "Single" &&
      !azurerm_container_app.api.ingress[0].external_enabled &&
      !azurerm_container_app.api.ingress[0].allow_insecure_connections &&
      azurerm_container_app.api.ingress[0].target_port == 8080 &&
      azurerm_container_app.api.ingress[0].transport == "auto" &&
      azurerm_container_app.web.revision_mode == "Single" &&
      azurerm_container_app.web.ingress[0].external_enabled &&
      !azurerm_container_app.web.ingress[0].allow_insecure_connections &&
      azurerm_container_app.web.ingress[0].target_port == 4000 &&
      azurerm_container_app.web.ingress[0].transport == "auto"
    )
    error_message = "Ingress must keep the API internal, the web external, and both HTTPS-only."
  }

  assert {
    condition = (
      length(azurerm_container_app.api.identity) == 1 &&
      azurerm_container_app.api.identity[0].type == "UserAssigned" &&
      azurerm_container_app.api.identity[0].identity_ids == toset([var.app_identity.id]) &&
      length(azurerm_container_app.web.identity) == 0 &&
      length(azurerm_container_app_job.migrate.identity) == 1 &&
      azurerm_container_app_job.migrate.identity[0].type == "UserAssigned" &&
      azurerm_container_app_job.migrate.identity[0].identity_ids == toset([var.migrator_identity.id])
    )
    error_message = "Workloads must reuse only their supplied identities and the web must stay identity-free."
  }

  assert {
    condition = (
      azurerm_container_app.api.workload_profile_name == "Consumption" &&
      azurerm_container_app.api.template[0].container[0].cpu == 1 &&
      azurerm_container_app.api.template[0].container[0].memory == "2Gi" &&
      azurerm_container_app.web.workload_profile_name == "Consumption" &&
      azurerm_container_app.web.template[0].container[0].cpu == 0.25 &&
      azurerm_container_app.web.template[0].container[0].memory == "0.5Gi"
    )
    error_message = "Dev API must use 1 vCPU / 2 GiB while the web remains at the consumption floor."
  }

  assert {
    condition = (
      azurerm_container_app.api.template[0].min_replicas == 0 &&
      azurerm_container_app.api.template[0].max_replicas == 1 &&
      azurerm_container_app.api.template[0].http_scale_rule[0].name == "http-concurrency" &&
      azurerm_container_app.api.template[0].http_scale_rule[0].concurrent_requests == "50" &&
      azurerm_container_app.web.template[0].min_replicas == 0 &&
      azurerm_container_app.web.template[0].max_replicas == 1 &&
      azurerm_container_app.web.template[0].http_scale_rule[0].name == "http-concurrency" &&
      azurerm_container_app.web.template[0].http_scale_rule[0].concurrent_requests == "50"
    )
    error_message = "Both apps must scale from zero to one on HTTP concurrency 50."
  }

  assert {
    condition = (
      azurerm_container_app.api.template[0].container[0].startup_probe[0].path == "/health/live" &&
      azurerm_container_app.api.template[0].container[0].startup_probe[0].port == 8080 &&
      azurerm_container_app.api.template[0].container[0].startup_probe[0].failure_count_threshold == 30 &&
      azurerm_container_app.api.template[0].container[0].startup_probe[0].interval_seconds == 10 &&
      azurerm_container_app.api.template[0].container[0].startup_probe[0].timeout == 5 &&
      azurerm_container_app.api.template[0].container[0].liveness_probe[0].path == "/health/live" &&
      azurerm_container_app.api.template[0].container[0].liveness_probe[0].port == 8080 &&
      azurerm_container_app.api.template[0].container[0].liveness_probe[0].failure_count_threshold == 3 &&
      azurerm_container_app.api.template[0].container[0].liveness_probe[0].interval_seconds == 10 &&
      azurerm_container_app.api.template[0].container[0].liveness_probe[0].timeout == 5 &&
      azurerm_container_app.api.template[0].container[0].readiness_probe[0].path == "/health/ready" &&
      azurerm_container_app.api.template[0].container[0].readiness_probe[0].port == 8080 &&
      azurerm_container_app.api.template[0].container[0].readiness_probe[0].failure_count_threshold == 3 &&
      azurerm_container_app.api.template[0].container[0].readiness_probe[0].interval_seconds == 10 &&
      azurerm_container_app.api.template[0].container[0].readiness_probe[0].timeout == 5
    )
    error_message = "API probes must distinguish liveness from database-backed readiness."
  }

  assert {
    condition = alltrue([
      for probe in [
        azurerm_container_app.web.template[0].container[0].startup_probe[0],
        azurerm_container_app.web.template[0].container[0].liveness_probe[0],
        azurerm_container_app.web.template[0].container[0].readiness_probe[0],
      ] :
      probe.path == "/" && probe.port == 4000 && probe.interval_seconds == 10 && probe.timeout == 5
      ]) && (
      azurerm_container_app.web.template[0].container[0].startup_probe[0].failure_count_threshold == 30 &&
      azurerm_container_app.web.template[0].container[0].liveness_probe[0].failure_count_threshold == 3 &&
      azurerm_container_app.web.template[0].container[0].readiness_probe[0].failure_count_threshold == 3
    )
    error_message = "Web probes must use the app shell with the approved timings."
  }

  assert {
    condition = (
      azurerm_container_app_job.migrate.workload_profile_name == "Consumption" &&
      azurerm_container_app_job.migrate.replica_retry_limit == 0 &&
      azurerm_container_app_job.migrate.replica_timeout_in_seconds == 1800 &&
      length(azurerm_container_app_job.migrate.manual_trigger_config) == 1 &&
      azurerm_container_app_job.migrate.manual_trigger_config[0].parallelism == 1 &&
      azurerm_container_app_job.migrate.manual_trigger_config[0].replica_completion_count == 1 &&
      azurerm_container_app_job.migrate.template[0].container[0].cpu == 0.5 &&
      azurerm_container_app_job.migrate.template[0].container[0].memory == "1Gi"
    )
    error_message = "Migration must remain a bounded, manual, single-replica job."
  }

  assert {
    condition = {
      for setting in azurerm_container_app.api.template[0].container[0].env :
      setting.name => coalesce(setting.value, setting.secret_name)
      } == {
      ASPNETCORE_ENVIRONMENT                = "Production"
      ASPNETCORE_HTTP_PORTS                 = "8080"
      HTTP_PORT                             = "8080"
      AZURE_CLIENT_ID                       = var.app_identity.client_id
      Database__Host                        = var.database.host
      Database__Port                        = tostring(var.database.port)
      Database__Database                    = var.database.name
      Database__Username                    = var.app_identity.name
      Database__UseEntraAuth                = "true"
      Database__MaxPoolSize                 = tostring(var.database.max_pool_size)
      Jwt__SigningMode                      = "KeyVault"
      Jwt__KeyVaultKeyId                    = var.key_vault.jwt_signing_key_id
      Jwt__Issuer                           = "Intelibill-dev"
      Jwt__Audience                         = "Intelibill-dev"
      App__BaseUrl                          = "https://${azurerm_container_app.web.ingress[0].fqdn}"
      Proxy__Enabled                        = "true"
      Proxy__ForwardLimit                   = "2"
      Proxy__TrustAnyProxy                  = "true"
      Observability__NewRelic__OtlpEndpoint = "https://otlp.nr-data.net:4318"
      Observability__NewRelic__ServiceName  = "Intelibill.Api"
      Observability__NewRelic__Environment  = "dev"
    }
    error_message = "The API environment must exactly match the approved secret-free runtime contract."
  }

  assert {
    condition = {
      for setting in azurerm_container_app.web.template[0].container[0].env :
      setting.name => coalesce(setting.value, setting.secret_name)
      } == {
      HTTP_PORT  = "4000"
      PORT       = "4000"
      API_ORIGIN = "https://intelibill-dev-api.internal.${azurerm_container_app_environment.main.default_domain}"
      NODE_ENV   = "production"
    }
    error_message = "The web environment must exactly implement the same-origin proxy contract."
  }

  assert {
    condition = {
      for setting in azurerm_container_app_job.migrate.template[0].container[0].env :
      setting.name => coalesce(setting.value, setting.secret_name)
      } == {
      HTTP_PORT              = "8080"
      AZURE_CLIENT_ID        = var.migrator_identity.client_id
      Database__Host         = var.database.host
      Database__Port         = tostring(var.database.port)
      Database__Database     = var.database.name
      Database__Username     = var.migrator_identity.name
      Database__UseEntraAuth = "true"
      Database__MaxPoolSize  = tostring(var.database.max_pool_size)
    }
    error_message = "The migration environment must contain only its HTTP and Entra database settings."
  }

  assert {
    condition = (
      length(azurerm_container_app.api.secret) == 0 &&
      !output.observability_secret_configured &&
      alltrue([
        for name in keys({
          for setting in azurerm_container_app.api.template[0].container[0].env :
          setting.name => coalesce(setting.value, setting.secret_name)
        }) :
        name != "Database__Password" &&
        name != "Jwt__Secret" &&
        !startswith(name, "Cors__AllowedOrigins")
      ])
    )
    error_message = "Secret values and CORS origins must be absent, with New Relic disabled by default."
  }

  assert {
    condition = (
      toset([
        for setting in azurerm_monitor_diagnostic_setting.container_apps.enabled_log :
        setting.category
      ]) == toset(["ContainerAppConsoleLogs", "ContainerAppSystemLogs", "ContainerAppHTTPLogs"]) &&
      toset([
        for setting in azurerm_monitor_diagnostic_setting.container_apps.enabled_metric :
        setting.category
      ]) == toset(["AllMetrics"]) &&
      azurerm_monitor_diagnostic_setting.container_apps.log_analytics_workspace_id == var.log_analytics_id
    )
    error_message = "Container Apps diagnostics must export the exact approved categories."
  }

  assert {
    condition = (
      toset([
        for setting in azurerm_monitor_diagnostic_setting.key_vault.enabled_log :
        setting.category
      ]) == toset(["AuditEvent"]) &&
      toset([
        for setting in azurerm_monitor_diagnostic_setting.key_vault.enabled_metric :
        setting.category
      ]) == toset(["AllMetrics"]) &&
      azurerm_monitor_diagnostic_setting.key_vault.log_analytics_workspace_id == var.log_analytics_id
    )
    error_message = "Key Vault diagnostics must export only audit events and metrics."
  }

  assert {
    condition = (
      length(azurerm_role_assignment.deploy) == 3 &&
      toset([for assignment in azurerm_role_assignment.deploy : assignment.scope]) == toset([
        azurerm_container_app.api.id,
        azurerm_container_app.web.id,
        azurerm_container_app_job.migrate.id,
      ]) &&
      alltrue([
        for assignment in azurerm_role_assignment.deploy :
        assignment.principal_id == var.deploy_principal_id &&
        assignment.role_definition_id == var.deploy_role_definition_id
      ])
    )
    error_message = "Deploy access must consist of exactly three workload-scoped assignments."
  }

  assert {
    condition = output.outbound_ip_addresses == toset([
      "20.10.0.1",
      "20.10.0.2",
      "20.10.0.3",
    ])
    error_message = "The outbound address output must union every advertised app and job address."
  }
}

run "prod_sizing_contract" {
  command = plan

  variables {
    env = "prod"
  }

  override_resource {
    target = azurerm_container_app_environment.main
    values = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/intelibill-shared/providers/Microsoft.App/managedEnvironments/intelibill-prod-env"
    }
  }

  override_resource {
    target = azurerm_container_app.api
    values = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/intelibill-shared/providers/Microsoft.App/containerApps/intelibill-prod-api"
    }
  }

  override_resource {
    target = azurerm_container_app.web
    values = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/intelibill-shared/providers/Microsoft.App/containerApps/intelibill-prod-web"
    }
  }

  override_resource {
    target = azurerm_container_app_job.migrate
    values = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/intelibill-shared/providers/Microsoft.App/jobs/intelibill-prod-migrate"
    }
  }

  assert {
    condition = (
      azurerm_container_app_environment.main.name == "intelibill-prod-env" &&
      azurerm_container_app.api.name == "intelibill-prod-api" &&
      azurerm_container_app.web.name == "intelibill-prod-web" &&
      azurerm_container_app_job.migrate.name == "intelibill-prod-migrate" &&
      azurerm_container_app.api.template[0].container[0].cpu == 0.75 &&
      azurerm_container_app.api.template[0].container[0].memory == "1.5Gi" &&
      azurerm_container_app.web.template[0].container[0].cpu == 0.5 &&
      azurerm_container_app.web.template[0].container[0].memory == "1Gi" &&
      azurerm_container_app.api.template[0].max_replicas == 1 &&
      azurerm_container_app.web.template[0].max_replicas == 1
    )
    error_message = "Prod must use its names and approved sizing without raising either replica cap."
  }
}

run "optional_secret_contract" {
  command = plan

  variables {
    new_relic_api_key_secret_name = "new-relic-api-key"
  }

  override_resource {
    target = azurerm_container_app_environment.main
    values = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/intelibill-shared/providers/Microsoft.App/managedEnvironments/intelibill-dev-env"
    }
  }

  override_resource {
    target = azurerm_container_app.api
    values = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/intelibill-shared/providers/Microsoft.App/containerApps/intelibill-dev-api"
    }
  }

  override_resource {
    target = azurerm_container_app.web
    values = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/intelibill-shared/providers/Microsoft.App/containerApps/intelibill-dev-web"
    }
  }

  override_resource {
    target = azurerm_container_app_job.migrate
    values = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/intelibill-shared/providers/Microsoft.App/jobs/intelibill-dev-migrate"
    }
  }

  assert {
    condition = (
      output.observability_secret_configured &&
      length(azurerm_container_app.api.secret) == 1 &&
      one(azurerm_container_app.api.secret).name == "new-relic-api-key" &&
      one(azurerm_container_app.api.secret).identity == var.app_identity.id &&
      one(azurerm_container_app.api.secret).key_vault_secret_id == "https://intelibill-dev-kv.vault.azure.net/secrets/new-relic-api-key" &&
      {
        for setting in azurerm_container_app.api.template[0].container[0].env :
        setting.name => coalesce(setting.value, setting.secret_name)
      }["Observability__NewRelic__ApiKey"] == "new-relic-api-key"
    )
    error_message = "The optional New Relic key must use a versionless Key Vault reference."
  }
}

run "reject_empty_secret_name" {
  command = plan

  variables {
    new_relic_api_key_secret_name = ""
  }

  expect_failures = [var.new_relic_api_key_secret_name]
}

run "reject_path_secret_name" {
  command = plan

  variables {
    new_relic_api_key_secret_name = "integrations/new-relic-api-key"
  }

  expect_failures = [var.new_relic_api_key_secret_name]
}

run "reject_versioned_secret_name" {
  command = plan

  variables {
    new_relic_api_key_secret_name = "new-relic-api-key/0123456789abcdef0123456789abcdef"
  }

  expect_failures = [var.new_relic_api_key_secret_name]
}

run "reject_invalid_secret_name_character" {
  command = plan

  variables {
    new_relic_api_key_secret_name = "new_relic_api_key"
  }

  expect_failures = [var.new_relic_api_key_secret_name]
}

run "reject_tagged_image" {
  command = plan

  variables {
    bootstrap_image = "ghcr.io/mendhak/http-https-echo:31"
  }

  expect_failures = [var.bootstrap_image]
}

run "reject_uppercase_digest" {
  command = plan

  variables {
    bootstrap_image = "ghcr.io/mendhak/http-https-echo@sha256:0FEFE04350131D7BB28355E3BF037062643E45F4A8A32F23679529E1B09D8CE4"
  }

  expect_failures = [var.bootstrap_image]
}

run "reject_short_digest" {
  command = plan

  variables {
    bootstrap_image = "ghcr.io/mendhak/http-https-echo@sha256:0fefe043"
  }

  expect_failures = [var.bootstrap_image]
}
