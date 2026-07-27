variable "resource_group_name" {
  description = "Resource group that holds shared monitoring"
  type        = string
}

variable "location" {
  description = "Azure region for shared monitoring"
  type        = string
}

variable "postgres_server_id" {
  description = "Resource ID of the shared PostgreSQL Flexible Server"
  type        = string
}

variable "daily_quota_gb" {
  description = "Daily ingestion budget fuse for the shared Log Analytics workspace"
  type        = number

  validation {
    condition     = var.daily_quota_gb > 0
    error_message = "Daily Log Analytics quota must be greater than zero."
  }
}
