resource "azurerm_container_app" "web" {
  name                         = "intelibill-${var.env}-web"
  container_app_environment_id = azurerm_container_app_environment.main.id
  resource_group_name          = var.resource_group_name
  revision_mode                = "Single"
  workload_profile_name        = "Consumption"

  ingress {
    external_enabled           = true
    allow_insecure_connections = false
    target_port                = 4000
    transport                  = "auto"

    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  template {
    min_replicas = 0
    max_replicas = 1

    http_scale_rule {
      name                = "http-concurrency"
      concurrent_requests = "50"
    }

    container {
      name   = "web"
      image  = var.bootstrap_image
      cpu    = local.app_resources[var.env].cpu
      memory = local.app_resources[var.env].memory

      dynamic "env" {
        for_each = local.web_environment

        content {
          name  = env.key
          value = env.value
        }
      }

      startup_probe {
        transport               = "HTTP"
        path                    = "/"
        port                    = 4000
        failure_count_threshold = 30
        interval_seconds        = 10
        timeout                 = 5
      }

      liveness_probe {
        transport               = "HTTP"
        path                    = "/"
        port                    = 4000
        failure_count_threshold = 3
        interval_seconds        = 10
        timeout                 = 5
      }

      readiness_probe {
        transport               = "HTTP"
        path                    = "/"
        port                    = 4000
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
