variable "subscription_id" {
  description = "Azure subscription"
  type        = string
}

variable "resource_group_name" {
  description = "The single resource group, created in bootstrap"
  type        = string
  default     = "intelibill-shared"
}

variable "location" {
  description = "Azure region"
  type        = string
  default     = "centralindia"
}

variable "secret_officer_object_ids" {
  description = <<-EOT
    Object IDs allowed to write secret values by hand in Phase 9. Key Vault's
    data plane is not implied by Owner on the subscription, so without an entry
    here `az keyvault secret set` is denied. Find yours with
    `az ad signed-in-user show --query id -o tsv`.
  EOT
  type        = list(string)
  default     = []
}

variable "bootstrap_image" {
  description = "Digest-pinned bootstrap image; deployment pipelines later own workload image changes"
  type        = string
  default     = "ghcr.io/mendhak/http-https-echo@sha256:0fefe04350131d7bb28355e3bf037062643e45f4a8a32f23679529e1b09d8ce4"
}

variable "new_relic_api_key_secret_name" {
  description = "Out-of-band Key Vault secret-name segment, or null until the integration key exists"
  type        = string
  default     = "new-relic-api-key"
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
}
