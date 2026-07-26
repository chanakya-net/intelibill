run "builds_environment_labelled_exact_rules" {
  command = plan

  variables {
    advertised_outbound_ip_addresses = {
      dev  = ["20.10.0.1", "20.10.0.2", "20.10.0.3"]
      prod = ["20.20.0.1"]
    }
  }

  assert {
    condition = output.allowed_ip_rules == {
      container-apps-dev-20-10-0-1 = {
        start_ip = "20.10.0.1"
        end_ip   = "20.10.0.1"
      }
      container-apps-dev-20-10-0-2 = {
        start_ip = "20.10.0.2"
        end_ip   = "20.10.0.2"
      }
      container-apps-dev-20-10-0-3 = {
        start_ip = "20.10.0.3"
        end_ip   = "20.10.0.3"
      }
      container-apps-prod-20-20-0-1 = {
        start_ip = "20.20.0.1"
        end_ip   = "20.20.0.1"
      }
    }
    error_message = "Advertised addresses must become stable environment-labelled exact rules."
  }
}

run "collapses_duplicate_addresses" {
  command = plan

  variables {
    advertised_outbound_ip_addresses = {
      dev = ["20.10.0.1", "20.10.0.1"]
    }
  }

  assert {
    condition = output.allowed_ip_rules == {
      container-apps-dev-20-10-0-1 = {
        start_ip = "20.10.0.1"
        end_ip   = "20.10.0.1"
      }
    }
    error_message = "Duplicate advertised addresses within an environment must collapse to one rule."
  }
}

run "adds_retained_addresses_alongside_current_addresses" {
  command = plan

  variables {
    advertised_outbound_ip_addresses = {
      dev = ["20.10.0.1"]
    }
    retained_outbound_ip_addresses = {
      dev = ["20.10.0.9"]
    }
  }

  assert {
    condition = output.allowed_ip_rules == {
      container-apps-dev-20-10-0-1 = {
        start_ip = "20.10.0.1"
        end_ip   = "20.10.0.1"
      }
      container-apps-dev-20-10-0-9 = {
        start_ip = "20.10.0.9"
        end_ip   = "20.10.0.9"
      }
    }
    error_message = "Retained addresses must be unioned with currently advertised addresses."
  }
}

run "allows_empty_initial_advertised_state" {
  command = plan

  variables {
    advertised_outbound_ip_addresses = {}
  }

  assert {
    condition     = output.allowed_ip_rules == {}
    error_message = "An empty initial advertised map must generate no rules."
  }
}

run "rejects_zero_address" {
  command = plan

  variables {
    advertised_outbound_ip_addresses = {
      dev = ["0.0.0.0"]
    }
  }

  expect_failures = [var.advertised_outbound_ip_addresses]
}

run "rejects_broadcast_address" {
  command = plan

  variables {
    advertised_outbound_ip_addresses = {
      prod = ["255.255.255.255"]
    }
  }

  expect_failures = [var.advertised_outbound_ip_addresses]
}

run "rejects_malformed_ipv4" {
  command = plan

  variables {
    advertised_outbound_ip_addresses = {
      dev = ["20.10.0.999"]
    }
  }

  expect_failures = [var.advertised_outbound_ip_addresses]
}

run "rejects_ipv6" {
  command = plan

  variables {
    advertised_outbound_ip_addresses = {
      prod = ["2001:db8::1"]
    }
  }

  expect_failures = [var.advertised_outbound_ip_addresses]
}

run "rejects_unknown_environment_key" {
  command = plan

  variables {
    advertised_outbound_ip_addresses = {
      staging = ["20.30.0.1"]
    }
  }

  expect_failures = [var.advertised_outbound_ip_addresses]
}

run "validates_retained_addresses_too" {
  command = plan

  variables {
    retained_outbound_ip_addresses = {
      dev = ["not-an-ip"]
    }
  }

  expect_failures = [var.retained_outbound_ip_addresses]
}

run "rejects_advertised_leading_zero_ipv4" {
  command = plan

  variables {
    advertised_outbound_ip_addresses = {
      dev = ["20.10.0.01"]
    }
  }

  expect_failures = [var.advertised_outbound_ip_addresses]
}

run "rejects_advertised_disguised_zero_sentinel" {
  command = plan

  variables {
    advertised_outbound_ip_addresses = {
      dev = ["000.000.000.000"]
    }
  }

  expect_failures = [var.advertised_outbound_ip_addresses]
}

run "rejects_retained_leading_zero_ipv4" {
  command = plan

  variables {
    retained_outbound_ip_addresses = {
      prod = ["20.10.0.01"]
    }
  }

  expect_failures = [var.retained_outbound_ip_addresses]
}

run "rejects_retained_disguised_zero_sentinel" {
  command = plan

  variables {
    retained_outbound_ip_addresses = {
      prod = ["000.000.000.000"]
    }
  }

  expect_failures = [var.retained_outbound_ip_addresses]
}
