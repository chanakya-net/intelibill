#!/usr/bin/env bash
set -euo pipefail

role_file="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/role-assignments.tf"
actions="$(sed -n '/^[[:space:]]*actions = \[/,/^[[:space:]]*\]/p' "$role_file")"

rg -q '"Microsoft.App/jobs/write"' <<<"$actions"
! rg -q 'resource "azurerm_role_assignment" "deploy"' "$role_file"
! rg -q 'Microsoft.App/containerApps/delete' <<<"$actions"
! rg -q 'Microsoft.App/jobs/delete' <<<"$actions"
! rg -q 'listSecrets' <<<"$actions"
