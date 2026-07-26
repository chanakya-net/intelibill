variable "subscription_id" {
  description = "Azure subscription that holds shared infrastructure"
  type        = string
}

variable "tenant_id" {
  description = "Entra tenant, required for PostgreSQL Active Directory authentication"
  type        = string
}

variable "resource_group_name" {
  description = "Existing shared resource group, created in bootstrap"
  type        = string
  default     = "intelibill-shared"
}

variable "location" {
  description = "Azure region for shared resources"
  type        = string
  default     = "centralindia"

  # southindia and eastus are offer-restricted for this subscription: PostgreSQL
  # Flexible Server creation fails there with LocationIsOfferRestricted. Verify
  # any new region against the subscription-scoped capabilities API before
  # changing this — prevent_destroy makes a later move expensive.
  validation {
    condition     = !contains(["southindia", "eastus"], var.location)
    error_message = "PostgreSQL is offer-restricted in this region for this subscription. centralindia and southeastasia are open."
  }
}

# az ad signed-in-user show --query "{oid:id, upn:userPrincipalName}"
variable "admin_object_id" {
  description = "Entra object ID of the PostgreSQL administrator"
  type        = string
}

variable "admin_principal_name" {
  description = "UPN of the PostgreSQL administrator"
  type        = string
}

variable "postgres_sku" {
  description = "Shared server SKU. Resize from measurements, not in advance"
  type        = string
  default     = "B_Standard_B1ms"
}

variable "storage_mb" {
  description = "Storage shared by both databases. Cannot be reduced later"
  type        = number
  default     = 32768
}

variable "backup_retention_days" {
  description = "Per-server restore window; production's requirement governs both databases"
  type        = number
  default     = 14
}

# Distinct names because both live on one server. The application takes its
# database name from configuration, so this is the only place it is decided.
variable "databases" {
  description = "Databases to create on the shared server"
  type        = set(string)
  default     = ["intelibill_dev", "intelibill_prod"]
}

# Public-network launch exception. Empty until Phase 10 produces the Container
# Apps outbound IPs; migration IPs are added and removed within a single run.
#
# One rule list for one server: an allowlisted address can reach both databases,
# so per-environment separation has to come from grants, not from here.
variable "allowed_ip_rules" {
  description = "Firewall rules as name => { start_ip, end_ip }"
  type = map(object({
    start_ip = string
    end_ip   = string
  }))
  default = {}
}

# Left null until the domain and registrar access are confirmed. The zone is
# skipped entirely rather than created against a guessed name, because Phase 6
# delegation against the wrong zone is an outage, not a typo.
variable "domain_name" {
  description = "Apex domain for the Azure DNS zone, or null to skip DNS"
  type        = string
  default     = null
}
