variable "resource_group_name" {
  description = "Resource group that holds the server"
  type        = string
}

variable "location" {
  description = "Azure region"
  type        = string
}

variable "tenant_id" {
  description = "Entra tenant, required whenever Active Directory authentication is enabled"
  type        = string
}

# Flexible Server names are globally unique DNS labels, and Azure pins a name to
# the region of its first creation attempt — including a *failed* attempt. A name
# burned against a region the subscription cannot provision into is unusable
# until the reservation lapses, with no published window. Bump this to move on.
variable "name_serial" {
  description = "Serial suffix on the server name. Increment when a name is stuck on a dead region"
  type        = string
  default     = "01"

  validation {
    condition     = can(regex("^[0-9]{2}$", var.name_serial))
    error_message = "Two digits, e.g. 01."
  }
}

# One server hosting both environments. Isolation between these databases is
# therefore entirely a matter of in-database grants (Phase 7.4), not topology.
variable "databases" {
  description = "Database names to create on the server"
  type        = set(string)

  validation {
    condition     = length(var.databases) > 0
    error_message = "At least one database."
  }
}

# With password authentication disabled, Entra is the *only* way in. If no
# administrator is assigned, nobody can connect — including to fix it. Assign at
# least one, and confirm a second person can authenticate before relying on it.
variable "admin_object_id" {
  description = "Entra object ID of the server administrator"
  type        = string
}

variable "admin_principal_name" {
  description = "UPN of the server administrator; must match the object ID"
  type        = string
}

variable "postgres_sku" {
  description = "Server SKU. B1ms is the controlled-launch default, not an HA promise"
  type        = string
  default     = "B_Standard_B1ms"
}

variable "storage_mb" {
  description = "Storage in MB, shared by every database on the server. Cannot be reduced later"
  type        = number
  default     = 32768
}

variable "postgres_version" {
  description = "Major version"
  type        = string
  default     = "17"
}

# Retention is per server, so a shared server cannot keep dev and prod on
# different schedules. Set this to whatever production requires.
variable "backup_retention_days" {
  description = "Point-in-time restore window, applied to the whole server"
  type        = number
  default     = 14

  validation {
    condition     = var.backup_retention_days >= 7 && var.backup_retention_days <= 35
    error_message = "Azure allows 7 to 35 days."
  }
}

# Public-network launch exception. Every rule here is internet-facing, so the map
# stays empty by default and each entry is added deliberately and narrowly:
# stable Container Apps outbound IPs, and temporary migration IPs that are removed
# in the same workflow run that adds them.
#
# On a shared server a rule admits traffic to *both* databases; there is no
# per-database firewall.
variable "allowed_ip_rules" {
  description = "Firewall rules as name => { start_ip, end_ip }"
  type = map(object({
    start_ip = string
    end_ip   = string
  }))
  default = {}

  validation {
    condition = alltrue([
      for r in values(var.allowed_ip_rules) : r.start_ip != "0.0.0.0"
    ])
    error_message = "0.0.0.0 as a start IP is either the Allow-Azure-services rule or an open-to-the-world rule. Neither is permitted; allowlist real addresses."
  }

  validation {
    condition = alltrue([
      for r in values(var.allowed_ip_rules) : r.end_ip != "255.255.255.255"
    ])
    error_message = "255.255.255.255 opens the server to the entire internet."
  }
}
