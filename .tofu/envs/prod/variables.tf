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
