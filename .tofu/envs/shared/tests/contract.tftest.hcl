mock_provider "azurerm" {
  mock_resource "azurerm_postgresql_flexible_server" {
    defaults = {
      id   = "/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/intelibill-shared/providers/Microsoft.DBforPostgreSQL/flexibleServers/intelibill-pg-01"
      fqdn = "intelibill-pg-01.postgres.database.azure.com"
    }
  }

  mock_resource "azurerm_log_analytics_workspace" {
    defaults = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/intelibill-shared/providers/Microsoft.OperationalInsights/workspaces/intelibill-logs"
    }
  }
}

override_data {
  target = data.azurerm_resource_group.shared
  values = {
    id       = "/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/intelibill-shared"
    location = "centralindia"
  }
}

variables {
  subscription_id      = "00000000-0000-0000-0000-000000000001"
  tenant_id            = "00000000-0000-0000-0000-000000000002"
  admin_object_id      = "00000000-0000-0000-0000-000000000003"
  admin_principal_name = "operator@example.com"
}

run "reconciles_environment_egress_into_owned_database_rules" {
  command = plan

  override_data {
    target = data.terraform_remote_state.dev
    values = {
      outputs = {
        container_apps = {
          outbound_ip_addresses = ["20.10.0.1", "20.10.0.2", "20.10.0.3"]
        }
      }
    }
  }

  override_data {
    target = data.terraform_remote_state.prod
    values = {
      outputs = {
        container_apps = {
          outbound_ip_addresses = ["20.20.0.1"]
        }
      }
    }
  }

  variables {
    allowed_ip_rules = {
      operator-review = {
        start_ip = "198.51.100.10"
        end_ip   = "198.51.100.10"
      }
      container-apps-dev-20-10-0-1 = {
        start_ip = "203.0.113.100"
        end_ip   = "203.0.113.100"
      }
    }
  }

  assert {
    condition = output.container_apps_firewall_ips == {
      dev  = toset(["20.10.0.1", "20.10.0.2", "20.10.0.3"])
      prod = toset(["20.20.0.1"])
    }
    error_message = "Operational output must expose the current dev and prod addresses only."
  }

  assert {
    condition = toset(keys(local.database_allowed_ip_rules)) == toset([
      "container-apps-dev-20-10-0-1",
      "container-apps-dev-20-10-0-2",
      "container-apps-dev-20-10-0-3",
      "container-apps-prod-20-20-0-1",
      "operator-review",
    ])
    error_message = "The shared database owner must plan all generated exact rules and preserve operator access."
  }

  assert {
    condition = alltrue([
      for rule in values(local.database_allowed_ip_rules) :
      rule.start_ip == rule.end_ip &&
      !contains(["0.0.0.0", "255.255.255.255"], rule.start_ip)
    ])
    error_message = "The planned contract must contain no broad firewall rule."
  }

  assert {
    condition = (
      local.database_allowed_ip_rules["container-apps-dev-20-10-0-1"].start_ip == "20.10.0.1" &&
      local.database_allowed_ip_rules["container-apps-dev-20-10-0-1"].end_ip == "20.10.0.1"
    )
    error_message = "Generated Container Apps rules must win collisions with explicit operator rule names."
  }

  assert {
    condition = (
      local.database_allowed_ip_rules["operator-review"].start_ip == "198.51.100.10" &&
      local.database_allowed_ip_rules["operator-review"].end_ip == "198.51.100.10"
    )
    error_message = "A non-colliding explicitly reviewed operator rule must remain mergeable."
  }

  assert {
    condition     = length(regexall("(?m)^\\s*allowed_ip_rules\\s*=\\s*local\\.database_allowed_ip_rules\\s*$", file("${path.root}/main.tf"))) == 1
    error_message = "The resolved firewall map must be forwarded once to the shared-owned database module."
  }
}

run "wires_retained_addresses_for_transition_applies" {
  command = plan

  override_data {
    target = data.terraform_remote_state.dev
    values = {
      outputs = {
        container_apps = {
          outbound_ip_addresses = ["20.10.0.1", "20.10.0.2", "20.10.0.3"]
        }
      }
    }
  }

  override_data {
    target = data.terraform_remote_state.prod
    values = {
      outputs = {
        container_apps = {
          outbound_ip_addresses = ["20.20.0.1"]
        }
      }
    }
  }

  variables {
    retained_container_apps_outbound_ips = {
      dev  = ["20.10.0.9"]
      prod = []
    }
  }

  assert {
    condition = (
      length(output.retained_container_apps_outbound_ips["dev"]) == 1 &&
      contains(output.retained_container_apps_outbound_ips["dev"], "20.10.0.9") &&
      length(output.retained_container_apps_outbound_ips["prod"]) == 0
    )
    error_message = "The retained operational output must expose addresses only."
  }

  assert {
    condition = (
      local.database_allowed_ip_rules["container-apps-dev-20-10-0-9"].start_ip == "20.10.0.9" &&
      local.database_allowed_ip_rules["container-apps-dev-20-10-0-9"].end_ip == "20.10.0.9"
    )
    error_message = "Retained addresses must be planned as exact shared-owned database rules."
  }
}

run "allows_empty_initial_environment_outputs" {
  command = plan

  override_data {
    target = data.terraform_remote_state.dev
    values = {
      outputs = {
        container_apps = {
          outbound_ip_addresses = []
        }
      }
    }
  }

  override_data {
    target = data.terraform_remote_state.prod
    values = {
      outputs = {
        container_apps = {
          outbound_ip_addresses = []
        }
      }
    }
  }

  assert {
    condition     = output.container_apps_firewall_ips == { dev = toset([]), prod = toset([]) }
    error_message = "Empty initial environment outputs must remain valid for workspace-first provisioning."
  }

  assert {
    condition     = length(local.database_allowed_ip_rules) == 0
    error_message = "Empty initial environment outputs must generate no database firewall rules."
  }
}
