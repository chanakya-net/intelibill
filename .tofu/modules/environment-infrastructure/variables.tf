variable "env" {
  description = "Deployment environment"
  type        = string

  validation {
    condition     = contains(["dev", "prod"], var.env)
    error_message = "env must be either dev or prod."
  }
}

variable "resource_group_name" {
  description = "Resource group containing the environment infrastructure"
  type        = string
}

variable "location" {
  description = "Azure region"
  type        = string
}

variable "log_analytics_id" {
  description = "Shared Log Analytics workspace resource ID"
  type        = string
}

variable "deploy_principal_id" {
  description = "Object ID of the environment deployment principal"
  type        = string
}

variable "deploy_role_definition_id" {
  description = "Resource ID of the role granted to the deployment principal"
  type        = string
}

variable "app_identity" {
  description = "Existing application user-assigned identity"
  type = object({
    id           = string
    name         = string
    client_id    = string
    principal_id = string
  })
}

variable "migrator_identity" {
  description = "Existing migration user-assigned identity"
  type = object({
    id           = string
    name         = string
    client_id    = string
    principal_id = string
  })
}

variable "key_vault" {
  description = "Existing environment Key Vault contract"
  type = object({
    id                 = string
    vault_uri          = string
    jwt_signing_key_id = string
  })
}

variable "database" {
  description = "Environment PostgreSQL connection contract"
  type = object({
    host          = string
    port          = number
    name          = string
    max_pool_size = number
  })
}

variable "new_relic_api_key_secret_name" {
  description = "Optional Key Vault secret name containing the New Relic API key"
  type        = string
  default     = null
  nullable    = true
}

variable "bootstrap_image" {
  description = "Immutable public multi-architecture image used until pipelines publish application images"
  type        = string

  validation {
    condition     = can(regex("@sha256:[0-9a-f]{64}$", var.bootstrap_image))
    error_message = "bootstrap_image must end in a lowercase sha256 digest."
  }
}
