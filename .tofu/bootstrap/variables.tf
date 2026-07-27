variable "subscription_id" {
  description = "Azure subscription that holds shared infrastructure"
  type        = string
}

variable "location" {
  description = "Azure region for all shared resources"
  type        = string
  default     = "eastus"
}

variable "resource_group_name" {
  description = "Resource group that holds shared infrastructure"
  type        = string
}


variable "github_repository" {
  description = "owner/name of the repository whose workflows federate into these identities"
  type        = string
  default     = "chanakya-net/intelibill"

  validation {
    condition     = can(regex("^[A-Za-z0-9._-]+/[A-Za-z0-9._-]+$", var.github_repository))
    error_message = "Must be owner/name, with no scheme and no trailing slash."
  }
}

variable "state_storage_account_name" {
  description = "Globally unique storage account for OpenTofu state (3-24 chars, lowercase alphanumeric)"
  type        = string

  validation {
    condition     = can(regex("^[a-z0-9]{3,24}$", var.state_storage_account_name))
    error_message = "Must be 3-24 lowercase alphanumeric characters."
  }
}