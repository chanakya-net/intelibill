terraform {
  required_version = ">= 1.8"
  required_providers {
    azurerm = { source = "hashicorp/azurerm", version = "~> 4.0" }
    time    = { source = "hashicorp/time", version = "~> 0.11" }
  }
}

# The environment's secret store: the JWT signing key and whatever integration
# credentials are enabled. Container Apps resolves the values at runtime under
# the app's managed identity, so CI never sees them and they never reach state
# ([decision §7]).

resource "azurerm_key_vault" "main" {
  name                = "intelibill-${var.env}-kv"
  resource_group_name = var.resource_group_name
  location            = var.location
  tenant_id           = var.tenant_id
  sku_name            = "standard"

  # RBAC rather than access policies. Access policies are a second, parallel
  # permission system that Azure role assignments do not appear in, which is how
  # a vault ends up readable by a principal nobody can account for.
  rbac_authorization_enabled = true

  # Recovering a deleted vault is only possible while the soft-delete window is
  # open. It is also what reserves the name: a purged-protected vault cannot be
  # removed early, and its name stays taken until the window closes.
  soft_delete_retention_days = var.soft_delete_retention_days
  purge_protection_enabled   = var.purge_protection_enabled

  # Public, consistent with the rest of this estate ([decision §8] locks the
  # public network path). Container Apps egress addresses are neither stable nor
  # published, so an IP allowlist here would block the only caller that matters.
  # The control that stands in its place is RBAC: no principal outside the two
  # role assignments below can read a secret.
  public_network_access_enabled = true
}

# The runtime identity reads secrets and does nothing else. Not Officer: the
# application has no reason to be able to write or delete one.
resource "azurerm_role_assignment" "app_secrets_user" {
  scope                = azurerm_key_vault.main.id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = var.app_principal_id
}

# Sign, verify, and read the public key — everything the API needs to issue and
# validate a token, and nothing that would let it read the private material.
# There is no operation that exports it: that is the point of signing here
# rather than holding a secret.
resource "azurerm_role_assignment" "app_crypto_user" {
  scope                = azurerm_key_vault.main.id
  role_definition_name = "Key Vault Crypto User"
  principal_id         = var.app_principal_id
}

# Creating a key is a data-plane operation, so whoever runs the apply needs this
# even holding Owner on the subscription.
resource "azurerm_role_assignment" "operator_crypto_officer" {
  for_each = toset(var.secret_officer_object_ids)

  scope                = azurerm_key_vault.main.id
  role_definition_name = "Key Vault Crypto Officer"
  principal_id         = each.value
}

# Role assignments take up to a minute to reach the data plane. Without this the
# first apply creates the vault, grants the role, and then fails creating the key
# with a Forbidden that disappears on a re-run — the kind of failure people learn
# to retry past instead of read.
resource "time_sleep" "rbac_propagation" {
  depends_on = [
    azurerm_role_assignment.operator_crypto_officer,
    azurerm_role_assignment.operator_secrets_officer,
  ]

  create_duration = "60s"
}

# The JWT signing key. RSA rather than a symmetric secret because a Standard
# vault will not generate symmetric material at all — `oct` is rejected outright
# and `oct-HSM` needs a Managed HSM, which costs more per hour than this estate
# does per month. Asymmetric is the better answer anyway: the private key has no
# export operation, so it exists in exactly one place and the API can only ask
# for a signature.
resource "azurerm_key_vault_key" "jwt_signing" {
  name         = "jwt-signing"
  key_vault_id = azurerm_key_vault.main.id
  key_type     = "RSA"
  key_size     = 2048
  key_opts     = ["sign", "verify"]

  depends_on = [time_sleep.rbac_propagation]

  # Rotation is a vault policy rather than a runbook step. Key Vault creates the
  # new version on schedule; the API picks it up because it resolves validation
  # keys by `kid`, so tokens signed by the previous version keep validating until
  # they expire.
  rotation_policy {
    expire_after         = var.key_expire_after
    notify_before_expiry = "P30D"

    automatic {
      time_before_expiry = var.key_rotate_before_expiry
    }
  }

  lifecycle {
    # A replaced key invalidates every token in flight and cannot be recovered
    # for anything already signed with it.
    prevent_destroy = true
  }
}

# Phase 9 sets secret values by hand, and Key Vault's data plane is not implied
# by Owner or Contributor on the subscription — without this, `az keyvault
# secret set` fails with a permission error that reads like a bug in the CLI.
resource "azurerm_role_assignment" "operator_secrets_officer" {
  for_each = toset(var.secret_officer_object_ids)

  scope                = azurerm_key_vault.main.id
  role_definition_name = "Key Vault Secrets Officer"
  principal_id         = each.value
}
