#!/usr/bin/env bash
set -euo pipefail

role_file="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/role-assignments.tf"

ruby - "${role_file}" <<'RUBY'
role_file = ARGV.fetch(0)
source = File.read(role_file)
blocks = source.scan(
  /^resource "azurerm_role_assignment" "[^"]+" \{\n.*?^\}/m,
)
infra_apply_blocks = blocks.select do |block|
  block.include?(
    "principal_id         = azurerm_user_assigned_identity.infra_apply.principal_id",
  )
end

crypto_officer_blocks = infra_apply_blocks.select do |block|
  block.include?('role_definition_name = "Key Vault Crypto Officer"')
end

raise "infra_apply must have exactly one Key Vault Crypto Officer assignment" unless crypto_officer_blocks.length == 1

crypto_officer = crypto_officer_blocks.fetch(0)
raise "infra_apply Key Vault role must be scoped to the shared resource group" unless crypto_officer.include?(
  "scope                = azurerm_resource_group.shared.id",
)

forbidden_secret_role = infra_apply_blocks.any? do |block|
  block.match?(/role_definition_name = "Key Vault Secrets (Officer|User)"/)
end
raise "infra_apply must not receive a Key Vault secrets role" if forbidden_secret_role
RUBY
