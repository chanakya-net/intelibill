mock_provider "azurerm" {}

override_data {
  target = data.azurerm_resource_group.main
  values = {
    id       = "/subscriptions/00000000-0000-0000-0000-000000000011/resourceGroups/intelibill-shared"
    location = "centralindia"
  }
}

override_data {
  target = data.azurerm_client_config.current
  values = {
    tenant_id = "00000000-0000-0000-0000-000000000012"
  }
}

override_data {
  target = data.azurerm_log_analytics_workspace.shared
  values = {
    id = "/subscriptions/00000000-0000-0000-0000-000000000011/resourceGroups/intelibill-shared/providers/Microsoft.OperationalInsights/workspaces/intelibill-logs"
  }
}

override_data {
  target = data.azurerm_user_assigned_identity.deploy
  values = {
    id           = "/subscriptions/00000000-0000-0000-0000-000000000011/resourceGroups/intelibill-shared/providers/Microsoft.ManagedIdentity/userAssignedIdentities/id-gha-deploy-prod"
    principal_id = "00000000-0000-0000-0000-000000000013"
  }
}

override_data {
  target = data.azurerm_subscription.current
  values = {
    id              = "/subscriptions/00000000-0000-0000-0000-000000000011"
    subscription_id = "00000000-0000-0000-0000-000000000011"
  }
}

override_data {
  target = data.azurerm_role_definition.container_app_deployer
  values = {
    id = "/subscriptions/00000000-0000-0000-0000-000000000011/providers/Microsoft.Authorization/roleDefinitions/00000000-0000-0000-0000-000000000014"
  }
}

override_module {
  target = module.workload_identities
  outputs = {
    identities = {
      app = {
        id           = "/subscriptions/00000000-0000-0000-0000-000000000011/resourceGroups/intelibill-shared/providers/Microsoft.ManagedIdentity/userAssignedIdentities/id-intelibill-app-prod"
        name         = "id-intelibill-app-prod"
        client_id    = "00000000-0000-0000-0000-000000000015"
        principal_id = "00000000-0000-0000-0000-000000000016"
      }
      migrator = {
        id           = "/subscriptions/00000000-0000-0000-0000-000000000011/resourceGroups/intelibill-shared/providers/Microsoft.ManagedIdentity/userAssignedIdentities/id-intelibill-migrator-prod"
        name         = "id-intelibill-migrator-prod"
        client_id    = "00000000-0000-0000-0000-000000000017"
        principal_id = "00000000-0000-0000-0000-000000000018"
      }
    }
  }
}

override_module {
  target = module.key_vault
  outputs = {
    name               = "kv-intelibill-prod"
    id                 = "/subscriptions/00000000-0000-0000-0000-000000000011/resourceGroups/intelibill-shared/providers/Microsoft.KeyVault/vaults/kv-intelibill-prod"
    vault_uri          = "https://kv-intelibill-prod.vault.azure.net/"
    jwt_signing_key_id = "https://kv-intelibill-prod.vault.azure.net/secrets/jwt-signing-key"
  }
}

override_resource {
  target = module.environment_infrastructure.azurerm_container_app_environment.main
  values = {
    id             = "/subscriptions/00000000-0000-0000-0000-000000000011/resourceGroups/intelibill-shared/providers/Microsoft.App/managedEnvironments/cae-intelibill-prod"
    default_domain = "cae-intelibill-prod.centralindia.azurecontainerapps.io"
  }
}

override_resource {
  target = module.environment_infrastructure.azurerm_container_app.api
  values = {
    id                    = "/subscriptions/00000000-0000-0000-0000-000000000011/resourceGroups/intelibill-shared/providers/Microsoft.App/containerApps/ca-intelibill-api-prod"
    outbound_ip_addresses = ["192.0.2.20"]
  }
}

override_resource {
  target = module.environment_infrastructure.azurerm_container_app.web
  values = {
    id                    = "/subscriptions/00000000-0000-0000-0000-000000000011/resourceGroups/intelibill-shared/providers/Microsoft.App/containerApps/ca-intelibill-web-prod"
    outbound_ip_addresses = ["192.0.2.21"]
  }
}

override_resource {
  target = module.environment_infrastructure.azurerm_container_app_job.migrate
  values = {
    id                    = "/subscriptions/00000000-0000-0000-0000-000000000011/resourceGroups/intelibill-shared/providers/Microsoft.App/jobs/caj-intelibill-migrate-prod"
    outbound_ip_addresses = ["192.0.2.22"]
  }
}

run "wires_existing_foundations_into_prod_workloads" {
  command = plan

  variables {
    subscription_id               = "00000000-0000-0000-0000-000000000011"
    new_relic_api_key_secret_name = "new-relic-api-key"
    bootstrap_image               = "ghcr.io/mendhak/http-https-echo@sha256:0fefe04350131d7bb28355e3bf037062643e45f4a8a32f23679529e1b09d8ce4"
  }

  assert {
    condition = (
      data.azurerm_resource_group.main.name == "intelibill-shared" &&
      data.azurerm_log_analytics_workspace.shared.name == "intelibill-logs" &&
      data.azurerm_user_assigned_identity.deploy.name == "id-gha-deploy-prod" &&
      data.azurerm_role_definition.container_app_deployer.name == "Intelibill Container App Deployer"
    )
    error_message = "Prod shared infrastructure and deploy authority must be discovered by deterministic name."
  }

  assert {
    condition = (
      local.env == "prod" &&
      local.database_name == "intelibill_prod" &&
      var.bootstrap_image == "ghcr.io/mendhak/http-https-echo@sha256:0fefe04350131d7bb28355e3bf037062643e45f4a8a32f23679529e1b09d8ce4"
    )
    error_message = "Prod must retain its explicit database name and immutable bootstrap image."
  }

  assert {
    condition     = output.container_apps.environment.name == module.environment_infrastructure.environment_name
    error_message = "The root environment output must come from the environment infrastructure module."
  }

  assert {
    condition     = output.container_apps.api.name == module.environment_infrastructure.api_name
    error_message = "The root API output must come from the environment infrastructure module."
  }

  assert {
    condition     = output.container_apps.web.name == module.environment_infrastructure.web_name
    error_message = "The root web output must come from the environment infrastructure module."
  }

  assert {
    condition     = output.container_apps.migration_job.name == module.environment_infrastructure.migration_job_name
    error_message = "The root migration job output must come from the environment infrastructure module."
  }

  assert {
    condition = (
      output.container_apps.environment.name == "intelibill-prod-env" &&
      output.container_apps.api.name == "intelibill-prod-api" &&
      output.container_apps.web.name == "intelibill-prod-web" &&
      output.container_apps.migration_job.name == "intelibill-prod-migrate"
    )
    error_message = "Prod must publish the environment-specific workload resources."
  }

  assert {
    condition     = output.container_apps.outbound_ip_addresses == module.environment_infrastructure.outbound_ip_addresses
    error_message = "The root must publish the module's complete outbound address set."
  }

  assert {
    condition     = output.container_apps.outbound_ip_addresses == toset(["192.0.2.20", "192.0.2.21", "192.0.2.22"])
    error_message = "The root must preserve the API, web, and migration job outbound address union."
  }

  assert {
    condition     = output.container_apps.observability_secret_configured
    error_message = "The optional observability secret name must be forwarded to the environment module."
  }
}
