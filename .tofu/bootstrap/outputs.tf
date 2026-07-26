# Client IDs are identifiers, not credentials. They are published as GitHub
# *variables* rather than secrets in Phase 4 so that an OIDC failure shows the
# actual value instead of `***`.

output "plan_client_id" {
  description = "Client ID for the read-only pull request plan identity"
  value       = azurerm_user_assigned_identity.plan.client_id
}

output "infra_apply_shared_client_id" {
  description = "Client ID for the shared-layer infrastructure apply identity"
  value       = azurerm_user_assigned_identity.infra_apply_shared.client_id
}

output "infra_apply_client_ids" {
  description = "Client IDs for the per-environment infrastructure apply identities"
  value       = { for env, id in azurerm_user_assigned_identity.infra_apply : env => id.client_id }
}

output "deploy_client_ids" {
  description = "Client IDs for the per-environment routine deploy identities"
  value       = { for env, id in azurerm_user_assigned_identity.deploy : env => id.client_id }
}

# Principal (object) IDs, for verifying role assignments with `az role assignment list`.
output "principal_ids" {
  description = "Principal IDs of every GitHub-facing identity, keyed by purpose"
  value = merge(
    {
      plan               = azurerm_user_assigned_identity.plan.principal_id
      infra_apply_shared = azurerm_user_assigned_identity.infra_apply_shared.principal_id
    },
    { for env, id in azurerm_user_assigned_identity.infra_apply : "infra_apply_${env}" => id.principal_id },
    { for env, id in azurerm_user_assigned_identity.deploy : "deploy_${env}" => id.principal_id },
  )
}

output "state_containers" {
  description = "State container name per layer, for the backend blocks in Phases 5 and 10"
  value       = { for layer, c in azurerm_storage_container.state : layer => c.name }
}
