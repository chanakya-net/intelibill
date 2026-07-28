resource "azurerm_container_app" "api" {
  name                         = "intelibill-${var.env}-api"
  container_app_environment_id = azurerm_container_app_environment.main.id
  resource_group_name          = var.resource_group_name
  revision_mode                = "Single"
  workload_profile_name        = "Consumption"

  identity {
    type         = "UserAssigned"
    identity_ids = [var.app_identity.id]
  }

  dynamic "secret" {
    for_each = var.new_relic_api_key_secret_name == null ? [] : [var.new_relic_api_key_secret_name]

    content {
      name                = "new-relic-api-key"
      identity            = var.app_identity.id
      key_vault_secret_id = "${var.key_vault.vault_uri}secrets/${secret.value}"
    }
  }

  ingress {
    external_enabled           = false
    allow_insecure_connections = false
    target_port                = 8080
    transport                  = "auto"

    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  template {
    min_replicas               = 0
    max_replicas               = 1
    cooldown_period_in_seconds = var.env == "dev" ? 1800 : null

    http_scale_rule {
      name                = "http-concurrency"
      concurrent_requests = "50"
    }

    container {
      name   = "api"
      image  = var.bootstrap_image
      cpu    = local.api_resources[var.env].cpu
      memory = local.api_resources[var.env].memory

      dynamic "env" {
        for_each = local.api_environment

        content {
          name  = env.key
          value = env.value
        }
      }

      dynamic "env" {
        for_each = var.new_relic_api_key_secret_name == null ? [] : [var.new_relic_api_key_secret_name]

        content {
          name        = "Observability__NewRelic__ApiKey"
          secret_name = "new-relic-api-key"
        }
      }

      startup_probe {
        transport               = "HTTP"
        path                    = "/health/live"
        port                    = 8080
        failure_count_threshold = 30
        interval_seconds        = 10
        timeout                 = 5
      }

      liveness_probe {
        transport               = "HTTP"
        path                    = "/health/live"
        port                    = 8080
        failure_count_threshold = 3
        interval_seconds        = 10
        timeout                 = 5
      }

      readiness_probe {
        transport               = "HTTP"
        path                    = "/health/ready"
        port                    = 8080
        failure_count_threshold = 3
        interval_seconds        = 10
        timeout                 = 5
      }
    }
  }

  lifecycle {
    ignore_changes = [template[0].container[0].image]
  }
}
