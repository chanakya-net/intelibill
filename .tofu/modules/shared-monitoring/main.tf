terraform {
  required_version = ">= 1.8"

  required_providers {
    azurerm = { source = "hashicorp/azurerm", version = "~> 4.0" }
  }
}

resource "azurerm_log_analytics_workspace" "main" {
  name                         = "intelibill-logs"
  resource_group_name          = var.resource_group_name
  location                     = var.location
  sku                          = "PerGB2018"
  retention_in_days            = 30
  daily_quota_gb               = var.daily_quota_gb
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
