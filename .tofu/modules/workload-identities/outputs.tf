# The PostgreSQL principal created in Phase 7.3 must be named *exactly* after the
# identity — the name is what the identity presents when it authenticates — and
# registered with the principal (object) ID, not the client ID.
output "identities" {
  description = "Name, principal ID, and client ID per role"
  value = {
    app = {
      name         = azurerm_user_assigned_identity.app.name
      principal_id = azurerm_user_assigned_identity.app.principal_id
      client_id    = azurerm_user_assigned_identity.app.client_id
      id           = azurerm_user_assigned_identity.app.id
    }
    migrator = {
      name         = azurerm_user_assigned_identity.migrator.name
      principal_id = azurerm_user_assigned_identity.migrator.principal_id
      client_id    = azurerm_user_assigned_identity.migrator.client_id
      id           = azurerm_user_assigned_identity.migrator.id
    }
  }
}
