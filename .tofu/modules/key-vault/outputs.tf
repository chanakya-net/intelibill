# Names and URIs only. Never a secret value: a Key Vault secret data source
# exposes `value`, and OpenTofu persists data-source attributes in state even
# when they are marked sensitive ([decision §7]).

output "name" {
  description = "Vault name, for the Phase 9 az commands"
  value       = azurerm_key_vault.main.name
}

output "id" {
  description = "Vault resource ID, for further role assignments"
  value       = azurerm_key_vault.main.id
}

output "vault_uri" {
  description = <<-EOT
    Base URI. Phase 10 builds versionless secret references from it as
    "$${vault_uri}secrets/<name>" so Container Apps resolves the current
    version under the runtime identity, without a rebuild when one rotates.
  EOT
  value       = azurerm_key_vault.main.vault_uri
}

output "jwt_signing_key_id" {
  description = <<-EOT
    Versionless key identifier. The application resolves the current version
    from it, so a rotation needs no configuration change and no redeploy.
  EOT
  value       = azurerm_key_vault_key.jwt_signing.versionless_id
}
