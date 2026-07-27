locals {
  environments = setunion(
    toset(keys(var.advertised_outbound_ip_addresses)),
    toset(keys(var.retained_outbound_ip_addresses)),
  )

  address_records = flatten([
    for env in local.environments : [
      for ip in setunion(
        lookup(var.advertised_outbound_ip_addresses, env, toset([])),
        lookup(var.retained_outbound_ip_addresses, env, toset([])),
        ) : {
        env = env
        ip  = ip
      }
    ]
  ])

  allowed_ip_rules = {
    for record in local.address_records :
    "container-apps-${record.env}-${replace(record.ip, ".", "-")}" => {
      start_ip = record.ip
      end_ip   = record.ip
    }
  }
}
