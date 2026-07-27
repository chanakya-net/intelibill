variable "env" {
  description = "Environment these identities serve"
  type        = string

  validation {
    condition     = contains(["dev", "prod"], var.env)
    error_message = "Must be dev or prod."
  }
}

variable "resource_group_name" {
  description = "Resource group that holds the identities"
  type        = string
}

variable "location" {
  description = "Azure region"
  type        = string
}
