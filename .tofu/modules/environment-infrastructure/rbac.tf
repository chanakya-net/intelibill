locals {
  deploy_scopes = {
    api     = azurerm_container_app.api.id
    web     = azurerm_container_app.web.id
    migrate = azurerm_container_app_job.migrate.id
  }
}

resource "azurerm_role_assignment" "deploy" {
  for_each = local.deploy_scopes

  scope              = each.value
  role_definition_id = var.deploy_role_definition_id
  principal_id       = var.deploy_principal_id
}
