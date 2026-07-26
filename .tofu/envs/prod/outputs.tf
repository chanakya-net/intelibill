output "identities" {
  description = "Runtime and migrator identities for Phase 7.3 and Phase 10"
  value       = module.workload_identities.identities
}

output "key_vault" {
  description = "Vault name and base URI for Phase 9 entry and Phase 10 references"
  value = {
    name               = module.key_vault.name
    id                 = module.key_vault.id
    vault_uri          = module.key_vault.vault_uri
    jwt_signing_key_id = module.key_vault.jwt_signing_key_id
  }
}
