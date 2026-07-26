terraform {
  required_version = ">= 1.8"
  required_providers {
    azurerm = { source = "hashicorp/azurerm", version = "~> 4.0" }
  }
}

# The identities the *workload* authenticates with — distinct from the
# GitHub-facing identities in bootstrap. These carry no federated credentials:
# Container Apps attaches them directly.
#
# Runtime and migrator are separate on purpose ([decision §18]). The migrator
# holds CREATE and owns the schema; the runtime holds DML only and can never
# alter it. One combined identity would give the API permanent DDL rights.

resource "azurerm_user_assigned_identity" "app" {
  name                = "id-app-${var.env}"
  resource_group_name = var.resource_group_name
  location            = var.location
}

resource "azurerm_user_assigned_identity" "migrator" {
  name                = "id-migrator-${var.env}"
  resource_group_name = var.resource_group_name
  location            = var.location
}
