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

variable "new_relic_otlp_endpoint" {
  description = "New Relic OTLP HTTP/protobuf base endpoint"
  type        = string
  default     = "https://otlp.nr-data.net:4318"

  validation {
    condition = contains([
      "https://otlp.nr-data.net:4318",
      "https://otlp.eu01.nr-data.net:4318",
      "https://otlp.jp.nr-data.net:4318",
    ], var.new_relic_otlp_endpoint)
    error_message = "new_relic_otlp_endpoint must be an approved New Relic regional OTLP HTTP/protobuf endpoint."
  }
}

variable "new_relic_api_key_secret_name" {
  description = "Optional single Key Vault secret-name segment containing the New Relic API key"
  type        = string
  default     = null
  nullable    = true

  validation {
    condition = (
      var.new_relic_api_key_secret_name == null ?
      true :
      can(regex("^[0-9A-Za-z-]{1,127}$", var.new_relic_api_key_secret_name))
    )
    error_message = "new_relic_api_key_secret_name must be null or a 1-127 character Key Vault secret name containing only letters, numbers, and hyphens."
  }
}

variable "mail_delivery" {
  description = "Optional authenticated STARTTLS mail transport. Supply only Key Vault secret names; values are entered out of band."
  type = object({
    enabled                = optional(bool, false)
    host                   = optional(string, "")
    port                   = optional(number, 587)
    use_start_tls          = optional(bool, true)
    username               = optional(string, "")
    from_email             = optional(string, "")
    from_name              = optional(string, "Intelibill")
    password_secret_name   = optional(string)
    outbox_key_secret_name = optional(string)
  })
  default  = {}
  nullable = false

  validation {
    condition = alltrue([
      for name in [var.mail_delivery.password_secret_name, var.mail_delivery.outbox_key_secret_name] :
      name == null ? true : can(regex("^[0-9A-Za-z-]{1,127}$", name))
    ])
    error_message = "Mail secret names must be null or a single 1-127 character Key Vault name containing only letters, numbers, and hyphens."
  }

  validation {
    condition = (
      var.mail_delivery.port >= 1 && var.mail_delivery.port <= 65535 &&
      var.mail_delivery.port == floor(var.mail_delivery.port)
    )
    error_message = "Mail port must be an integer between 1 and 65535."
  }

  validation {
    condition = !var.mail_delivery.enabled || (
      var.mail_delivery.use_start_tls &&
      alltrue([for value in [var.mail_delivery.host, var.mail_delivery.username, var.mail_delivery.from_email, var.mail_delivery.from_name] : length(trimspace(value)) > 0]) &&
      var.mail_delivery.password_secret_name != null &&
      var.mail_delivery.outbox_key_secret_name != null
    )
    error_message = "Enabled mail requires STARTTLS, host, username, sender email/name, SMTP password secret name, and persistent outbox key secret name."
  }
}

variable "bootstrap_image" {
  description = "Immutable public multi-architecture image used until pipelines publish application images"
  type        = string

  validation {
    condition     = can(regex("@sha256:[0-9a-f]{64}$", var.bootstrap_image))
    error_message = "bootstrap_image must end in a lowercase sha256 digest."
  }
}
