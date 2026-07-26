output "allowed_ip_rules" {
  description = "Stable environment-labelled exact PostgreSQL firewall rules"
  value       = local.allowed_ip_rules
}
