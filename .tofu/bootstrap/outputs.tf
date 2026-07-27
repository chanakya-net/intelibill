# Client IDs are identifiers, not credentials. They are published as GitHub
# *variables* rather than secrets in Phase 4 so that an OIDC failure shows the
# actual value instead of `***`.

output "plan_client_id" {
  description = "Client ID for the read-only pull request plan identity"
  value       = azurerm_user_assigned_identity.plan.client_id
}

output "infra_apply_client_id" {
  description = "Client ID for the infrastructure apply identity, used by every layer"
  value       = azurerm_user_assigned_identity.infra_apply.client_id
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
      plan        = azurerm_user_assigned_identity.plan.principal_id
      infra_apply = azurerm_user_assigned_identity.infra_apply.principal_id
    },
    { for env, id in azurerm_user_assigned_identity.deploy : "deploy_${env}" => id.principal_id },
  )
}

output "resource_group_name" {
  description = "The single resource group holding every resource"
  value       = azurerm_resource_group.shared.name
}

output "state_containers" {
  description = "State container name per layer, for the backend blocks in Phases 5 and 10"
  value       = { for layer, c in azurerm_storage_container.state : layer => c.name }
}
