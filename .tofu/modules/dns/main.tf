terraform {
  required_version = ">= 1.8"
  required_providers {
    azurerm = { source = "hashicorp/azurerm", version = "~> 4.0" }
  }
}

resource "azurerm_dns_zone" "main" {
  name                = var.domain_name
  resource_group_name = var.resource_group_name

  # Delegation is a manual registrar change (Phase 6) and destroying the zone
  # while it is authoritative takes the domain down.
  lifecycle {
    prevent_destroy = true
  }
}
