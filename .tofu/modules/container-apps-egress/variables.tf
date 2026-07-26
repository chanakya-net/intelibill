variable "advertised_outbound_ip_addresses" {
  description = "Current Container Apps outbound IPv4 addresses by environment"
  type        = map(set(string))
  default     = {}

  validation {
    condition     = length(setsubtract(toset(keys(var.advertised_outbound_ip_addresses)), toset(["dev", "prod"]))) == 0
    error_message = "Advertised outbound addresses may use only the dev and prod environment keys."
  }

  validation {
    condition = alltrue(flatten([
      for addresses in values(var.advertised_outbound_ip_addresses) : [
        for ip in addresses :
        can(regex("^(?:[0-9]{1,3}\\.){3}[0-9]{1,3}$", ip)) &&
        can(cidrnetmask("${ip}/32")) &&
        try(cidrhost("${ip}/32", 0) == ip, false) &&
        !contains(["0.0.0.0", "255.255.255.255"], ip)
      ]
    ]))
    error_message = "Advertised outbound addresses must be canonical dotted-decimal IPv4 addresses other than 0.0.0.0 and 255.255.255.255."
  }
}

variable "retained_outbound_ip_addresses" {
  description = "Previously advertised exact addresses retained for one reviewed transition apply"
  type        = map(set(string))
  default     = {}

  validation {
    condition     = length(setsubtract(toset(keys(var.retained_outbound_ip_addresses)), toset(["dev", "prod"]))) == 0
    error_message = "Retained outbound addresses may use only the dev and prod environment keys."
  }

  validation {
    condition = alltrue(flatten([
      for addresses in values(var.retained_outbound_ip_addresses) : [
        for ip in addresses :
        can(regex("^(?:[0-9]{1,3}\\.){3}[0-9]{1,3}$", ip)) &&
        can(cidrnetmask("${ip}/32")) &&
        try(cidrhost("${ip}/32", 0) == ip, false) &&
        !contains(["0.0.0.0", "255.255.255.255"], ip)
      ]
    ]))
    error_message = "Retained outbound addresses must be canonical dotted-decimal IPv4 addresses other than 0.0.0.0 and 255.255.255.255."
  }
}
