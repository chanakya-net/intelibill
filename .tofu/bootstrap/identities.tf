locals {
  # Deploy identities remain per environment. Unlike infrastructure apply, deploy
  # never creates resources — it updates an existing Container App and starts a
  # job — so Phase 10 can scope each one to its own app resource and keep the
  # environment boundary that the single resource group otherwise removes.
  environments = toset(["dev", "prod"])

  # Every layer's apply reaches the same identity, so all three subjects are
  # credentials on one identity rather than three identities.
  apply_environments = toset(["dev", "prod", "shared"])

  state_layers = toset(["shared", "dev", "prod"])

  # GitHub's OIDC subject claim is matched character-for-character by Entra:
  # no wildcards, no trailing slash. `environment:prod` matches only a job that
  # declares `environment: prod`, which is what makes the reviewer gate load-bearing.
  oidc_issuer   = "https://token.actions.githubusercontent.com"
  oidc_audience = ["api://AzureADTokenExchange"]
}

# --- Plan: read-only, pull requests -----------------------------------------
# One identity for every PR plan. Fork pull requests must never reach it: a plan
# cannot mutate Azure, but it reads state, and state describes everything.
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

# --- Infrastructure apply: one identity, every layer ------------------------
# Collapsed deliberately. With a single resource group, per-environment apply
# identities would hold identical Contributor rights over the same group, so
# separate identities would imply a boundary that does not exist. The reviewer
# gate on the prod and shared environments is what actually restricts access.
resource "azurerm_user_assigned_identity" "infra_apply" {
  name                = "id-gha-infra-apply"
  resource_group_name = azurerm_resource_group.shared.name
  location            = var.location
}

# One credential per environment, all resolving to the same identity: the gate
# differs per environment, the rights do not.
resource "azurerm_federated_identity_credential" "infra_apply" {
  for_each  = local.apply_environments
  name      = "gha-infra-apply-${each.key}"
  parent_id = azurerm_user_assigned_identity.infra_apply.id
  audience  = local.oidc_audience
  issuer    = local.oidc_issuer
  subject   = "repo:${var.github_repository}:environment:${each.key}"
}

# --- Routine deploy ---------------------------------------------------------
# Kept per environment: these hold the narrowest useful grant, and because they
# only update existing resources they can be scoped to a single Container App in
# Phase 10 rather than to the resource group.
resource "azurerm_user_assigned_identity" "deploy" {
  for_each            = local.environments
  name                = "id-gha-deploy-${each.key}"
  resource_group_name = azurerm_resource_group.shared.name
  location            = var.location
}

resource "azurerm_federated_identity_credential" "deploy" {
  for_each  = local.environments
  name      = "gha-deploy-${each.key}"
  parent_id = azurerm_user_assigned_identity.deploy[each.key].id
  audience  = local.oidc_audience
  issuer    = local.oidc_issuer
  subject   = "repo:${var.github_repository}:environment:${each.key}"
}
