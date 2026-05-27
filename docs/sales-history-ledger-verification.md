# Sales History ledger — verification checklist

This checklist captures the end-to-end verification steps for the Sales History ledger page.

## Backend

- Unit: `dotnet test tests/backend/unit/Intelibill.Application.Unit.Tests -c Release`
- Integration (requires Docker): `dotnet test tests/backend/integration/Intelibill.Integration.Tests -c Release --filter SalesControllerTests`

## Frontend

- Install: `cd src/frontend && bun install`
- Sales tests: `cd src/frontend && bun run test -- sales`
- i18n coverage: `cd src/frontend && bun run test -- i18n-coverage`
- Build: `cd src/frontend && bun run build`

## Manual UX verification (requires running app + login)

- Desktop: confirm header + export toolbar, KPI cards, ledger panel, controls row, table, and pagination match the approved mock.
- Mobile: confirm controls and table are usable (no overlaps/clipping), and pagination remains accessible.
- Export: confirm Excel/PDF/Tally exports include selected date range + report level.
- Receipt overlay: confirm “View Receipt” opens sale detail overlay and print actions remain available.

