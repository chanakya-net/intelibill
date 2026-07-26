data "azurerm_subscription" "current" {}

# Control-plane and data-plane rights are separate in Azure, and both failure modes
# surface as a generic `AuthorizationFailed`. Contributor is explicitly denied
# `Microsoft.Authorization/roleAssignments/write`, and neither Reader nor Contributor
# grants blob *data* access. Every scope below is therefore granted explicitly.

# --- Infrastructure apply: environment layers -------------------------------
resource "azurerm_role_assignment" "infra_apply_env" {
  for_each             = azurerm_resource_group.env
  scope                = each.value.id
  role_definition_name = "Contributor"
  principal_id         = azurerm_user_assigned_identity.infra_apply[each.key].principal_id
}

# Needed because the environment layer creates its own role assignments (app
# identity to Key Vault, migrator to the job). The role name must be exact —
# Tofu resolves `role_definition_name` by literal match.
resource "azurerm_role_assignment" "infra_apply_env_rbac" {
  for_each             = azurerm_resource_group.env
  scope                = each.value.id
  role_definition_name = "Role Based Access Control Administrator"
  principal_id         = azurerm_user_assigned_identity.infra_apply[each.key].principal_id
}

resource "azurerm_role_assignment" "infra_apply_env_state" {
  for_each             = azurerm_resource_group.env
  scope                = azurerm_storage_container.state[each.key].id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = azurerm_user_assigned_identity.infra_apply[each.key].principal_id
}

# --- Infrastructure apply: shared layer -------------------------------------
# Scoped to the shared resource group only. The environment identities get no
# rights here: an environment apply that needs something shared is a signal that
# the resource belongs in the shared layer, not that this grant should widen.
resource "azurerm_role_assignment" "infra_apply_shared" {
  scope                = azurerm_resource_group.shared.id
  role_definition_name = "Contributor"
  principal_id         = azurerm_user_assigned_identity.infra_apply_shared.principal_id
}

resource "azurerm_role_assignment" "infra_apply_shared_state" {
  scope                = azurerm_storage_container.state["shared"].id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = azurerm_user_assigned_identity.infra_apply_shared.principal_id
}

# --- Plan: read-only --------------------------------------------------------
resource "azurerm_role_assignment" "plan_reader" {
  for_each             = merge(azurerm_resource_group.env, { shared = azurerm_resource_group.shared })
  scope                = each.value.id
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

# Assigned at resource-group scope, so the Container Apps created in Phase 10
# inherit it without a second bootstrap pass.
resource "azurerm_role_assignment" "deploy" {
  for_each           = azurerm_resource_group.env
  scope              = each.value.id
  role_definition_id = azurerm_role_definition.container_app_deployer.role_definition_resource_id
  principal_id       = azurerm_user_assigned_identity.deploy[each.key].principal_id
}

# Public GHCR needs no registry role assignment: pushes use the job-scoped
# GITHUB_TOKEN and pulls are anonymous. If the registry decision is revisited,
# AcrPush belongs on the deploy identities and AcrPull on the app identity.
