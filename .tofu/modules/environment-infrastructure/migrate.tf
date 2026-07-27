resource "azurerm_container_app_job" "migrate" {
  name                         = "intelibill-${var.env}-migrate"
  container_app_environment_id = azurerm_container_app_environment.main.id
  resource_group_name          = var.resource_group_name
  location                     = var.location
  workload_profile_name        = "Consumption"
  replica_retry_limit          = 0
  replica_timeout_in_seconds   = 1800

  identity {
    type         = "UserAssigned"
    identity_ids = [var.migrator_identity.id]
  }

  manual_trigger_config {
    parallelism              = 1
    replica_completion_count = 1
  }

  template {
    container {
      name   = "migrate"
      image  = var.bootstrap_image
      cpu    = 0.5
      memory = "1Gi"

      dynamic "env" {
        for_each = local.migration_environment

        content {
          name  = env.key
          value = env.value
        }
      }
    }
  }

  lifecycle {
    ignore_changes = [template[0].container[0].image]
  }
}
