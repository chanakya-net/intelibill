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
