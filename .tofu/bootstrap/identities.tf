locals {
  environments = toset(["dev", "prod"])
  state_layers = toset(["shared", "dev", "prod"])

  # GitHub's OIDC subject claim is matched character-for-character by Entra:
  # no wildcards, no trailing slash. `environment:prod` matches only a job that
  # declares `environment: prod`, which is what makes the reviewer gate load-bearing.
  oidc_issuer   = "https://token.actions.githubusercontent.com"
  oidc_audience = ["api://AzureADTokenExchange"]
}

resource "azurerm_resource_group" "env" {
  for_each = local.environments
  name     = "intelibill-${each.key}"
  location = var.location
}

# --- Plan: read-only, pull requests -----------------------------------------
# One identity for every PR plan. Fork pull requests must never reach it: a plan
# cannot mutate Azure, but it can read state, and state describes everything.
resource "azurerm_user_assigned_identity" "plan" {
  name                = "id-gha-plan"
  resource_group_name = azurerm_resource_group.shared.name
  location            = var.location
}

resource "azurerm_federated_identity_credential" "plan" {
  name      = "gha-plan"
  parent_id = azurerm_user_assigned_identity.plan.id
  audience  = local.oidc_audience
  issuer    = local.oidc_issuer
  subject   = "repo:${var.github_repository}:pull_request"
}

# --- Infrastructure apply: shared layer -------------------------------------
# Gated behind a dedicated `shared` GitHub environment so that changes to the
# state account, DNS zone, and database servers need their own approval rather
# than riding along with a production app deploy.
resource "azurerm_user_assigned_identity" "infra_apply_shared" {
  name                = "id-gha-infra-apply-shared"
  resource_group_name = azurerm_resource_group.shared.name
  location            = var.location
}

resource "azurerm_federated_identity_credential" "infra_apply_shared" {
  name      = "gha-infra-apply-shared"
  parent_id = azurerm_user_assigned_identity.infra_apply_shared.id
  audience  = local.oidc_audience
  issuer    = local.oidc_issuer
  subject   = "repo:${var.github_repository}:environment:shared"
}

# --- Infrastructure apply: environment layers -------------------------------
# Separate from the deploy identity below. This one is broad (Contributor plus
# RBAC administration inside its own resource group) and is only reachable
# through an environment-gated apply job.
resource "azurerm_user_assigned_identity" "infra_apply" {
  for_each            = local.environments
  name                = "id-gha-infra-apply-${each.key}"
  resource_group_name = azurerm_resource_group.env[each.key].name
  location            = var.location
}

resource "azurerm_federated_identity_credential" "infra_apply" {
  for_each  = local.environments
  name      = "gha-infra-apply-${each.key}"
  parent_id = azurerm_user_assigned_identity.infra_apply[each.key].id
  audience  = local.oidc_audience
  issuer    = local.oidc_issuer
  subject   = "repo:${var.github_repository}:environment:${each.key}"
}

# --- Routine deploy ---------------------------------------------------------
# Runs on every release, so it holds the narrowest useful grant: update a
# Container App's image and start the migration job. Deliberately not the
# infrastructure identity above.
resource "azurerm_user_assigned_identity" "deploy" {
  for_each            = local.environments
  name                = "id-gha-deploy-${each.key}"
  resource_group_name = azurerm_resource_group.env[each.key].name
  location            = var.location
}

# Shares the environment subject with the infra-apply credential. Two identities
# may present the same subject; the workflow chooses between them by client ID.
resource "azurerm_federated_identity_credential" "deploy" {
  for_each  = local.environments
  name      = "gha-deploy-${each.key}"
  parent_id = azurerm_user_assigned_identity.deploy[each.key].id
  audience  = local.oidc_audience
  issuer    = local.oidc_issuer
  subject   = "repo:${var.github_repository}:environment:${each.key}"
}
