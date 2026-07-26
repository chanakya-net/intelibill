resource "azurerm_monitor_diagnostic_setting" "container_apps" {
  name                       = "intelibill-${var.env}-container-apps"
  target_resource_id         = azurerm_container_app_environment.main.id
  log_analytics_workspace_id = var.log_analytics_id

  enabled_log {
    category = "ContainerAppConsoleLogs"
  }

  enabled_log {
    category = "ContainerAppSystemLogs"
  }

  enabled_log {
    category = "ContainerAppHTTPLogs"
  }

  enabled_metric {
    category = "AllMetrics"
  }
}

resource "azurerm_monitor_diagnostic_setting" "key_vault" {
  name                       = "intelibill-${var.env}-key-vault"
  target_resource_id         = var.key_vault.id
  log_analytics_workspace_id = var.log_analytics_id

  enabled_log {
    category = "AuditEvent"
  }

  enabled_metric {
    category = "AllMetrics"
  }
}
