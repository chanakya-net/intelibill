data "azurerm_subscription" "current" {}

# Control-plane and data-plane rights are separate in Azure, and both failure modes
# surface as a generic `AuthorizationFailed`. Contributor is explicitly denied
# `Microsoft.Authorization/roleAssignments/write`, and neither Reader nor Contributor
# grants blob *data* access. Every scope below is therefore granted explicitly.

# --- Infrastructure apply ---------------------------------------------------
# Everything lives in one resource group, so this is the whole estate. Creating
# resources requires write at group scope — it cannot be narrowed to resources
# that do not exist yet — which is why a single group means a single blast radius.
resource "azurerm_role_assignment" "infra_apply" {
  scope                = azurerm_resource_group.shared.id
  role_definition_name = "Contributor"
  principal_id         = azurerm_user_assigned_identity.infra_apply.principal_id
}

# Needed because the environment layers create their own role assignments
# (app identity to Key Vault, migrator to the job).
resource "azurerm_role_assignment" "infra_apply_rbac" {
  scope                = azurerm_resource_group.shared.id
  role_definition_name = "Role Based Access Control Administrator"
  principal_id         = azurerm_user_assigned_identity.infra_apply.principal_id
}

# One identity applies all three layers, so it needs write on all three state
# containers. The containers remain separate to keep a mistaken backend `key`
# from landing in the wrong layer, but they no longer isolate dev from prod:
# that isolation left with the per-environment identities.
resource "azurerm_role_assignment" "infra_apply_state" {
  for_each             = local.state_layers
  scope                = azurerm_storage_container.state[each.key].id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = azurerm_user_assigned_identity.infra_apply.principal_id
}

# --- Plan: read-only --------------------------------------------------------
resource "azurerm_role_assignment" "plan_reader" {
  scope                = azurerm_resource_group.shared.id
  role_definition_name = "Reader"
  principal_id         = azurerm_user_assigned_identity.plan.principal_id
}

resource "azurerm_role_assignment" "plan_state" {
  for_each             = local.state_layers
  scope                = azurerm_storage_container.state[each.key].id
  role_definition_name = "Storage Blob Data Reader"
  principal_id         = azurerm_user_assigned_identity.plan.principal_id
}

# --- Routine deploy ---------------------------------------------------------
# No built-in role covers "update a Container App and start a job" without also
# granting delete, secret read, or environment-wide control, so define one.
resource "azurerm_role_definition" "container_app_deployer" {
  name        = "Intelibill Container App Deployer"
  scope       = data.azurerm_subscription.current.id
  description = "Update Container Apps and start Container Apps jobs. No delete, no secret listing, no identity assignment."

  permissions {
    actions = [
      "Microsoft.App/containerApps/read",
      "Microsoft.App/containerApps/write",
      "Microsoft.App/containerApps/revisions/read",
      "Microsoft.App/containerApps/revisions/activate/action",
      "Microsoft.App/containerApps/revisions/deactivate/action",
      "Microsoft.App/jobs/read",
      "Microsoft.App/jobs/write",
      "Microsoft.App/jobs/start/action",
      "Microsoft.App/jobs/executions/read",
      "Microsoft.Resources/subscriptions/resourceGroups/read",
    ]

    # listSecrets is withheld deliberately: the deploy job never needs to read
    # the app's configured secrets, and reading them would defeat Key Vault.
    not_actions = []
  }

  assignable_scopes = [data.azurerm_subscription.current.id]
}

# Public GHCR needs no registry role assignment: pushes use the job-scoped
# GITHUB_TOKEN and pulls are anonymous. If the registry decision is revisited,
# AcrPush belongs on the deploy identities and AcrPull on the app identity.
