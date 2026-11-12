# Guided merchant onboarding rollout

## Release order

1. Apply `20261111145704_AddShopSetupPreferences` through the migration job. The new table uses forced shop RLS and starts empty for existing shops.
2. Deploy the compatible API. New shops receive their startup preference in the same transaction that creates their owner membership.
3. Deploy the web build. At the application root, a live owner with the startup preference enabled opens `/shops/setup`. Existing shops with no saved preference land on their usual page. The active-shop menu opens setup on demand. Direct routes keep their destinations.

## Scope and billing

- `GET /api/shops/{shopId}/setup` and `GET /api/shops/{shopId}/setup/sample` require the token's active shop and a live membership. `GET/PUT /api/shops/{shopId}/setup/preferences` require a live owner in that scope. A preference update compares the version the owner read and preserves the first explicit sample acknowledgement.
- Setup derives readiness from the verified owner, business details, declared tax profile and invoice series, and a usable saved catalog line. Goods require a supported unit, an eligible lot and sufficient available stock. A usable service can be sold without inventory. Bank details and checking the sample are optional.
- Readiness describes the setup prerequisites for a simple walk-in cash sale. Checkout still applies its existing permission, numbering, stock, tax, expiry, posting period and till checks.
- The A4 and thermal sample use the saved seller and a priced catalog line. Both omit the invoice number and mark the page as a sample in print and on screen. Viewing or printing leaves the database unchanged and takes no fiscal number. Only an explicit acknowledgement updates preferences.

## Rollback

Remove the web entry and startup redirect by reverting the web release. Retain the additive schema, recorded preferences and all shop and financial facts. The previous web workflow can continue to use the existing billing and tax endpoints. No readiness backfill or tax inference is required.

## Verification

Run `dotnet test tests/backend/integration/Intelibill.Integration.Tests --filter FullyQualifiedName~ShopSetupTests` and `dotnet ef migrations has-pending-model-changes --project src/backend/Intelibill.Infrastructure --startup-project src/backend/Intelibill.Api`.

From `src/frontend`, run `bun run build`, then `bun run audit:ui:onboarding`. The audit requires Docker and Chromium. It starts the real API on loopback with a new Testcontainers database, serves the production bundle through the built web proxy, and takes down its own processes and database. The normal `bun run audit:ui:release` suite remains separate.
