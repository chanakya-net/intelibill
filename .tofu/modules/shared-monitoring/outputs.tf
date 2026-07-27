output "workspace" {
  description = "Azure Monitor workspace identifiers; shared keys are deliberately excluded"
  value = {
    id          = azurerm_log_analytics_workspace.main.id
    name        = azurerm_log_analytics_workspace.main.name
    customer_id = azurerm_log_analytics_workspace.main.workspace_id
  }
}
