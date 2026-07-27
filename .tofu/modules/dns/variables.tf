variable "domain_name" {
  description = "Apex domain, without a trailing dot"
  type        = string

  validation {
    condition     = !endswith(var.domain_name, ".")
    error_message = "Azure DNS zone names carry no trailing dot."
  }
}

variable "resource_group_name" {
  description = "Resource group that holds the zone"
  type        = string
}
