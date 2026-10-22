terraform {
  required_version = ">= 1.8"

  required_providers {
    azurerm = { source = "hashicorp/azurerm", version = "~> 4.0" }
  }
}

locals {
  api_resources = {
    dev  = { cpu = 1, memory = "2Gi" }
    prod = { cpu = 0.75, memory = "1.5Gi" }
  }

  web_resources = {
    dev  = { cpu = 0.25, memory = "0.5Gi" }
    prod = { cpu = 0.5, memory = "1Gi" }
  }

  mail_secret_references = {
    for alias, name in {
      email-password      = var.mail_delivery.password_secret_name
      identity-outbox-key = var.mail_delivery.outbox_key_secret_name
    } : alias => name if name != null
  }

  mail_secret_environment = {
    for name, alias in {
      Email__Password     = "email-password"
      Identity__OutboxKey = "identity-outbox-key"
    } : name => alias if contains(keys(local.mail_secret_references), alias)
  }

  api_environment = merge({
    ASPNETCORE_ENVIRONMENT                = "Production"
    Email__Enabled                        = tostring(var.mail_delivery.enabled)
    Email__LocalCapture                   = "false"
    ASPNETCORE_HTTP_PORTS                 = "8080"
    HTTP_PORT                             = "8080"
    AZURE_CLIENT_ID                       = var.app_identity.client_id
    Database__Host                        = var.database.host
    Database__Port                        = tostring(var.database.port)
    Database__Database                    = var.database.name
    Database__Username                    = var.app_identity.name
    Database__UseEntraAuth                = "true"
    Database__MaxPoolSize                 = tostring(var.database.max_pool_size)
    Jwt__SigningMode                      = "KeyVault"
    Jwt__KeyVaultKeyId                    = var.key_vault.jwt_signing_key_id
    Jwt__Issuer                           = "Intelibill-${var.env}"
    Jwt__Audience                         = "Intelibill-${var.env}"
    App__BaseUrl                          = "https://${azurerm_container_app.web.ingress[0].fqdn}"
    Proxy__Enabled                        = "true"
    Proxy__ForwardLimit                   = "2"
    Proxy__TrustAnyProxy                  = "true"
    Observability__NewRelic__OtlpEndpoint = var.new_relic_otlp_endpoint
    Observability__NewRelic__ServiceName  = "Intelibill.Api"
    Observability__NewRelic__Environment  = var.env
    }, var.mail_delivery.enabled ? {
    Email__Host        = var.mail_delivery.host
    Email__Port        = tostring(var.mail_delivery.port)
    Email__UseStartTls = tostring(var.mail_delivery.use_start_tls)
    Email__Username    = var.mail_delivery.username
    Email__FromEmail   = var.mail_delivery.from_email
    Email__FromName    = var.mail_delivery.from_name
  } : {})

  web_environment = {
    HTTP_PORT  = "4000"
    PORT       = "4000"
    API_ORIGIN = "https://intelibill-${var.env}-api.internal.${azurerm_container_app_environment.main.default_domain}"
    NODE_ENV   = "production"
  }

  migration_environment = {
    HTTP_PORT              = "8080"
    AZURE_CLIENT_ID        = var.migrator_identity.client_id
    Database__Host         = var.database.host
    Database__Port         = tostring(var.database.port)
    Database__Database     = var.database.name
    Database__Username     = var.migrator_identity.name
    Database__UseEntraAuth = "true"
    Database__MaxPoolSize  = tostring(var.database.max_pool_size)
  }
}

resource "azurerm_container_app_environment" "main" {
  name                = "intelibill-${var.env}-env"
  resource_group_name = var.resource_group_name
  location            = var.location
  logs_destination    = "azure-monitor"

  workload_profile {
    name                  = "Consumption"
    workload_profile_type = "Consumption"
    minimum_count         = 0
    maximum_count         = 0
  }
}
