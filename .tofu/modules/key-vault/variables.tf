variable "env" {
  description = "Environment this vault serves"
  type        = string

  validation {
    condition     = contains(["dev", "prod"], var.env)
    error_message = "Must be dev or prod."
  }
}

variable "resource_group_name" {
  description = "Resource group that holds the vault"
  type        = string
}

variable "location" {
  description = "Azure region"
  type        = string
}

variable "tenant_id" {
  description = "Entra tenant that owns the identities allowed to authenticate"
  type        = string
}

variable "app_principal_id" {
  description = "Principal (object) ID of the runtime identity that reads secrets"
  type        = string
}

variable "secret_officer_object_ids" {
  description = <<-EOT
    Object IDs of the humans who enter secret values out of band in Phase 9.
    Empty in CI: the pipeline never reads or writes a secret. Find yours with
    `az ad signed-in-user show --query id -o tsv`.
  EOT
  type        = list(string)
  default     = []
}

variable "soft_delete_retention_days" {
  description = "How long a deleted vault stays recoverable, 7 to 90"
  type        = number

  validation {
    condition     = var.soft_delete_retention_days >= 7 && var.soft_delete_retention_days <= 90
    error_message = "Azure accepts 7 to 90 days."
  }
}

variable "purge_protection_enabled" {
  description = <<-EOT
    Blocks early deletion of a soft-deleted vault. Irreversible once applied:
    Azure offers no way to turn it back off, and the vault name stays reserved
    for the whole retention window.
  EOT
  type        = bool
}

variable "key_expire_after" {
  description = <<-EOT
    ISO 8601 lifetime of each signing key version. Key Vault requires the
    rotation window to fit inside it.
  EOT
  type        = string
  default     = "P1Y"
}

variable "key_rotate_before_expiry" {
  description = "How long before expiry Key Vault creates the next version"
  type        = string
  default     = "P30D"
}
