# Dev New Relic Enablement Design

**Date:** 2026-07-28
**Scope:** Backend API telemetry in the dev environment only

## Goal

Enable the existing OpenTelemetry-to-New Relic pipeline for the dev API while
keeping the New Relic ingest key out of source code, OpenTofu configuration,
plans, and state. Production must remain unchanged and disabled.

The supplied dev key is intentionally a placeholder. The Key Vault reference
will be enabled immediately, but New Relic authentication is expected to fail
until the placeholder is replaced with a real `Ingest - License` key.

## Design

Create the `new-relic-api-key` secret manually in `intelibill-dev-kv`. OpenTofu
will know only the secret name and construct a versionless Key Vault reference;
it will never read or persist the secret value.

Make the New Relic OTLP endpoint an environment-module input. Keep the module's
current US endpoint as its default, pass the EU endpoint
`https://otlp.eu01.nr-data.net:4318` from the dev root, and leave prod on the
existing US default.

Enable the optional Key Vault reference by changing only the dev root's
non-secret `new_relic_api_key_secret_name` default to `new-relic-api-key`.
Leave the prod default as `null`.

The existing Container App mapping remains authoritative:

1. Key Vault secret `new-relic-api-key`
2. Versionless Container Apps Key Vault reference
3. Container App secret `new-relic-api-key`
4. Environment variable `Observability__NewRelic__ApiKey`
5. Existing OpenTelemetry and Serilog exporters using the `api-key` header

## Files and State

Tracked OpenTofu changes:

- Add a validated `new_relic_otlp_endpoint` input to the environment module.
- Use that input for `Observability__NewRelic__OtlpEndpoint`.
- Pass the EU endpoint from the dev environment root.
- Change only the dev secret-name default from `null` to
  `new-relic-api-key`.
- Extend OpenTofu contract tests for the dev-specific endpoint.

Out-of-band Azure change:

- Set the supplied placeholder as `intelibill-dev-kv/new-relic-api-key`.

No C# application changes, New Relic .NET agent, browser monitoring, mobile
monitoring, or production configuration changes are in scope.

## Error Handling and Rotation

The placeholder will produce New Relic authentication failures until replaced.
The API remains available because telemetry export is asynchronous and guarded
by the existing resilience pipeline.

Replacing the secret later creates a new Key Vault version. The versionless
Container Apps reference picks up the latest enabled version without changing
OpenTofu or source code.

If the dev OpenTofu plan includes production resources, a secret value, or any
change outside the dev New Relic endpoint/reference contract, do not apply it.

## Verification

Before applying:

- Run the environment-module OpenTofu contract tests.
- Run the dev-root OpenTofu contract tests.
- Inspect the saved dev plan and confirm its scope.

After applying, use metadata-only Azure queries to confirm:

- `intelibill-dev-kv` contains an enabled `new-relic-api-key` secret.
- `intelibill-dev-api` has a versionless Key Vault reference for that secret.
- `Observability__NewRelic__ApiKey` references the Container App secret.
- `Observability__NewRelic__OtlpEndpoint` is
  `https://otlp.eu01.nr-data.net:4318`.
- Production remains without a New Relic secret reference.

Never read, print, or persist the Key Vault secret value during verification.
