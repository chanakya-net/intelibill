mock_provider "azurerm" {}

override_data {
  target = data.azurerm_resource_group.main
  values = {
    id       = "/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/intelibill-shared"
    location = "centralindia"
  }
}

override_data {
  target = data.azurerm_client_config.current
  values = {
    tenant_id = "00000000-0000-0000-0000-000000000002"
  }
}

override_data {
  target = data.azurerm_log_analytics_workspace.shared
  values = {
    id = "/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/intelibill-shared/providers/Microsoft.OperationalInsights/workspaces/intelibill-logs"
  }
}

override_data {
  target = data.azurerm_user_assigned_identity.deploy
  values = {
    id           = "/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/intelibill-shared/providers/Microsoft.ManagedIdentity/userAssignedIdentities/id-gha-deploy-dev"
    principal_id = "00000000-0000-0000-0000-000000000003"
  }
}

override_data {
  target = data.azurerm_subscription.current
  values = {
    id              = "/subscriptions/00000000-0000-0000-0000-000000000001"
    subscription_id = "00000000-0000-0000-0000-000000000001"
  }
}

override_data {
  target = data.azurerm_role_definition.container_app_deployer
  values = {
    id = "/subscriptions/00000000-0000-0000-0000-000000000001/providers/Microsoft.Authorization/roleDefinitions/00000000-0000-0000-0000-000000000004"
  }
}

override_module {
  target = module.workload_identities
  outputs = {
    identities = {
      app = {
        id           = "/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/intelibill-shared/providers/Microsoft.ManagedIdentity/userAssignedIdentities/id-intelibill-app-dev"
        name         = "id-intelibill-app-dev"
        client_id    = "00000000-0000-0000-0000-000000000005"
        principal_id = "00000000-0000-0000-0000-000000000006"
      }
      migrator = {
        id           = "/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/intelibill-shared/providers/Microsoft.ManagedIdentity/userAssignedIdentities/id-intelibill-migrator-dev"
        name         = "id-intelibill-migrator-dev"
        client_id    = "00000000-0000-0000-0000-000000000007"
        principal_id = "00000000-0000-0000-0000-000000000008"
      }
    }
  }
}

override_module {
  target = module.key_vault
  outputs = {
    name               = "kv-intelibill-dev"
    id                 = "/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/intelibill-shared/providers/Microsoft.KeyVault/vaults/kv-intelibill-dev"
    vault_uri          = "https://kv-intelibill-dev.vault.azure.net/"
    jwt_signing_key_id = "https://kv-intelibill-dev.vault.azure.net/secrets/jwt-signing-key"
  }
}

override_resource {
  target = module.environment_infrastructure.azurerm_container_app_environment.main
  values = {
    id             = "/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/intelibill-shared/providers/Microsoft.App/managedEnvironments/cae-intelibill-dev"
    default_domain = "cae-intelibill-dev.centralindia.azurecontainerapps.io"
  }
}

override_resource {
  target = module.environment_infrastructure.azurerm_container_app.api
  values = {
    id                    = "/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/intelibill-shared/providers/Microsoft.App/containerApps/ca-intelibill-api-dev"
    outbound_ip_addresses = ["192.0.2.10"]
  }
}

override_resource {
  target = module.environment_infrastructure.azurerm_container_app.web
  values = {
    id                    = "/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/intelibill-shared/providers/Microsoft.App/containerApps/ca-intelibill-web-dev"
    outbound_ip_addresses = ["192.0.2.11"]
  }
}

override_resource {
  target = module.environment_infrastructure.azurerm_container_app_job.migrate
  values = {
    id                    = "/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/intelibill-shared/providers/Microsoft.App/jobs/caj-intelibill-migrate-dev"
    outbound_ip_addresses = ["192.0.2.12"]
  }
}

run "wires_existing_foundations_into_dev_workloads" {
  command = plan

  variables {
    subscription_id               = "00000000-0000-0000-0000-000000000001"
    new_relic_api_key_secret_name = "new-relic-api-key"
    bootstrap_image               = "ghcr.io/mendhak/http-https-echo@sha256:0fefe04350131d7bb28355e3bf037062643e45f4a8a32f23679529e1b09d8ce4"
  }

  assert {
    condition = (
      data.azurerm_resource_group.main.name == "intelibill-shared" &&
      data.azurerm_log_analytics_workspace.shared.name == "intelibill-logs" &&
      data.azurerm_user_assigned_identity.deploy.name == "id-gha-deploy-dev" &&
      data.azurerm_role_definition.container_app_deployer.name == "Intelibill Container App Deployer"
    )
    error_message = "Dev shared infrastructure and deploy authority must be discovered by deterministic name."
  }

  assert {
    condition = (
      local.env == "dev" &&
      local.database_name == "intelibill_dev" &&
      var.bootstrap_image == "ghcr.io/mendhak/http-https-echo@sha256:0fefe04350131d7bb28355e3bf037062643e45f4a8a32f23679529e1b09d8ce4"
    )
    error_message = "Dev must retain its explicit database name and immutable bootstrap image."
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
      output.container_apps.environment.name == "intelibill-dev-env" &&
      output.container_apps.api.name == "intelibill-dev-api" &&
      output.container_apps.web.name == "intelibill-dev-web" &&
      output.container_apps.migration_job.name == "intelibill-dev-migrate"
    )
    error_message = "Dev must publish the environment-specific workload resources."
  }

  assert {
    condition     = output.container_apps.outbound_ip_addresses == module.environment_infrastructure.outbound_ip_addresses
    error_message = "The root must publish the module's complete outbound address set."
  }

  assert {
    condition     = output.container_apps.outbound_ip_addresses == toset(["192.0.2.10", "192.0.2.11", "192.0.2.12"])
    error_message = "The root must preserve the API, web, and migration job outbound address union."
  }

  assert {
    condition     = output.container_apps.observability_secret_configured
    error_message = "The optional observability secret name must be forwarded to the environment module."
  }
}
