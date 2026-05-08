# Dashboard Date Range Inventory Loss Bug - Technical Requirements

## Problem Statement

The dashboard initially loads correct profit/loss data, including losses from inventory decrease adjustments such as damaged, expired, stolen, missing/lost, stock count correction, and other loss. After selecting **Last 7 days** and clicking **Apply**, those inventory correction losses disappear from dashboard financial calculations, leaving only sales-based profit data.

This is incorrect. Applying a date range must preserve the same profit/loss semantics as the initial dashboard load, constrained only by the selected range.

## Scope

Fix the dashboard summary flow for selected date ranges, especially the **Last 7 days** preset, so in-range inventory decrease adjustment losses remain included in:

- `wastageCost`
- `profitBeforeTax`
- `profitAfterTax`
- `profitTrendSeries`
- `previousPeriodSummary.profitAfterTax`
- `hasNoSalesActivity`

Do not change the meaning of expense corrections. Expense correction fields continue to use expense correction records, while inventory correction losses continue to be represented through the sales/profit loss side as adjustment loss or wastage cost.

## Decisions

| # | Question | Decision |
|---|---|---|
| 1 | Should selected ranges include all historical inventory correction losses? | No. Include only active inventory decrease adjustments whose performed date falls inside the selected range. |
| 2 | Should **Last 7 days** be local-calendar based or backend UTC-calendar based? | Use one consistent calendar basis everywhere, preferably shop/user local calendar date for India usage. A correction made today locally must remain inside Today/Last 7 Days after Apply. |
| 3 | Should Staff users see financial loss fields? | No. Preserve existing Staff behavior: financial fields, profit trends, and previous period summary remain hidden/null. |
| 4 | Should voided and increase adjustments affect dashboard losses? | No. Continue including only non-voided decrease adjustments. |
| 5 | Should CodeGraph be used? | No. The user requested graphify instead of CodeGraph if graph help is required. Full graphify output is not required for this requirements pass and would violate the break-req file-write limit. |

## Current System Findings

### UI

Dashboard UI lives under:

- `src/frontend/src/app/features/dashboard/pages/dashboard-page/dashboard-page.component.ts`
- `src/frontend/src/app/features/dashboard/pages/dashboard-page/dashboard-page.component.html`
- `src/frontend/src/app/features/dashboard/state/dashboard.actions.ts`
- `src/frontend/src/app/features/dashboard/state/dashboard.effects.ts`
- `src/frontend/src/app/features/dashboard/state/dashboard.reducer.ts`
- `src/frontend/src/app/features/dashboard/services/dashboard.service.ts`

The UI computes presets in the browser and sends `startDate` / `endDate` query params to `GET /api/dashboard`.

Risk found:

- Frontend preset dates are generated from JavaScript `Date` and serialized with `toISOString().slice(0, 10)`.
- Backend default dates and range filters use UTC-derived dates.
- This mixed date basis can drop same-day local adjustments near timezone boundaries when a range is applied.

### Backend

Dashboard endpoint and handler:

- `src/backend/Intelibill.Api/Controllers/DashboardController.cs`
- `src/backend/Intelibill.Application/Features/Dashboard/Queries/GetDashboard/GetDashboardQueryHandler.cs`
- `src/backend/Intelibill.Application/Features/Dashboard/Services/DashboardKpiCalculator.cs`

Existing backend behavior already has the right model:

- `GetDashboardQueryHandler` fetches `adjustmentLosses` through `GetDashboardLossesByShopAndDateRangeAsync`.
- `DashboardKpiCalculator.CalculateSalesKpis` subtracts active decrease adjustment loss cost from profit.
- `DashboardKpiCalculator.BuildTrendSeries` subtracts adjustment losses per day.
- `DashboardKpiCalculator.BuildPreviousPeriodSummary` subtracts previous-period adjustment losses.

Implementation should preserve this model and fix the range/date consistency defect if confirmed.

### Data

Inventory adjustment repository:

- `src/backend/Intelibill.Infrastructure/Repositories/InventoryAdjustmentRepository.cs`

Relevant query:

- `GetDashboardLossesByShopAndDateRangeAsync(Guid shopId, DateOnly startDate, DateOnly endDate, ...)`

It filters:

- `ShopId == shopId`
- `Direction == InventoryAdjustmentDirection.Decrease`
- `!IsVoided`
- `PerformedAt >= start`
- `PerformedAt < exclusiveEnd`

Requirement:

- Continue using `PerformedAt` as the date source for dashboard inclusion.
- Ensure conversion from `DateOnly` range to `DateTimeOffset`/database boundaries uses the same intended local calendar basis as the frontend presets.
- Do not introduce schema changes unless the implementation discovers shop timezone storage is needed and already available elsewhere.

## Functional Requirements

1. Initial dashboard load and **Last 7 days + Apply** must calculate inventory adjustment losses consistently for the same effective date range.
2. A non-voided decrease adjustment inside the selected range must increase `wastageCost` by its `CostImpact`.
3. That same adjustment must reduce `profitBeforeTax` and `profitAfterTax` by its `CostImpact`.
4. An in-range decrease adjustment with no sales must still make `hasNoSalesActivity` false for Owner/Manager and Staff responses.
5. Increase adjustments must not affect wastage/profit loss.
6. Voided decrease adjustments must not affect wastage/profit loss.
7. Previous-period comparison must include decrease adjustment losses that fall in the computed previous period.
8. Staff responses must keep financial fields null while still reflecting activity presence through `hasNoSalesActivity`.
9. The dashboard freshness/range badge should continue showing the effective API response range.
10. Persisted dashboard ranges in local storage must not cause stale or invalid date basis behavior.

## Non-Functional Requirements

1. Keep changes narrowly scoped to dashboard range calculation and tests.
2. Preserve existing API shape for `GET /api/dashboard`.
3. Preserve existing DTO property names and nullability semantics.
4. Avoid adding a new frontend date library unless the existing stack cannot express the required local-date behavior cleanly.
5. Use deterministic date helpers where possible to make tests stable.
6. Do not use CodeGraph for this fix.

## Acceptance Criteria

1. Given an Owner/Manager shop with one inventory batch at cost price 50 and one active decrease adjustment of quantity 2 performed inside the Last 7 Days range, applying Last 7 Days returns:
   - `wastageCost == 100`
   - `profitBeforeTax == -100` when there are no sales
   - `profitAfterTax == -100` when there are no sales
   - `hasNoSalesActivity == false`
2. Given the same data on initial dashboard load for the same effective range, the response values match the applied-range response.
3. Given an adjustment just inside the local end date, it is included.
4. Given an adjustment just outside the local start date, it is excluded.
5. Given an increase adjustment inside the range, profit is unchanged by that adjustment.
6. Given a voided decrease adjustment inside the range, profit is unchanged by that adjustment.
7. Given a Staff user with in-range adjustment loss, financial fields remain null and `hasNoSalesActivity == false`.

## Test Requirements

### Backend Integration Tests

Add or update tests in:

- `tests/backend/integration/Intelibill.Integration.Tests/DashboardControllerTests.cs`

Required scenarios:

1. `GetDashboard_WithLast7DaysRange_IncludesInRangeInventoryDecreaseLosses`
2. `GetDashboard_WithLast7DaysRange_ExcludesOutOfRangeInventoryDecreaseLosses`
3. `GetDashboard_WithLast7DaysRange_UsesConsistentLocalDateBoundaries`
4. `GetDashboard_WithDateRange_InitialAndAppliedRangeUseSameLossSemantics`

Existing tests already cover active decrease only, previous period losses, and Staff hiding behavior; keep them passing.

### Frontend Unit Tests

Add or update tests in:

- `src/frontend/src/app/features/dashboard/pages/dashboard-page/dashboard-page.component.spec.ts`
- `src/frontend/src/app/features/dashboard/state/dashboard.effects.spec.ts`

Required scenarios:

1. Selecting `last7` computes a 7-calendar-day inclusive range and dispatches `applyRange(startDate, endDate, 'last7')`.
2. Apply sends the computed `startDate` and `endDate` to `DashboardService.getDashboard`.
3. A dashboard response with `wastageCost`, negative `profitAfterTax`, and profit trend points after Apply still renders/selects chart data correctly.
4. Date helper behavior is deterministic around timezone-sensitive cases, preferably by isolating date calculation into a testable helper.

## Implementation Notes

Recommended fix path:

1. Isolate frontend date preset computation into a small pure helper that works in local calendar dates without `toISOString()` date shifts.
2. Align backend `DateOnly` to `PerformedAt` boundary conversion with the chosen calendar basis.
3. Keep `GetDashboardLossesByShopAndDateRangeAsync` as the single source for dashboard inventory loss filtering.
4. Add regression tests before or alongside the fix so the Last 7 Days Apply path cannot regress again.

## Out of Scope

- Changing dashboard API response contract.
- Adding new dashboard cards or new UX copy.
- Changing profit/loss report endpoint behavior under `GET /api/sales/profit-loss`.
- Changing inventory adjustment creation rules.
- Creating GitHub issues or implementing code as part of this requirements-only pass.
