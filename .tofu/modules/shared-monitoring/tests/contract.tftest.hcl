mock_provider "azurerm" {
  mock_resource "azurerm_log_analytics_workspace" {
    defaults = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/intelibill-shared/providers/Microsoft.OperationalInsights/workspaces/intelibill-logs"
    }
  }
}

variables {
  resource_group_name = "intelibill-shared"
  location            = "centralindia"
  postgres_server_id  = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/intelibill-shared/providers/Microsoft.DBforPostgreSQL/flexibleServers/intelibill-pg-01"
  daily_quota_gb      = 0.1
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
      azurerm_log_analytics_workspace.main.local_authentication_disabled
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
