## Problem Statement

The current dashboard is not intuitive for operators and managers because it is heavily card-based, lacks visual trends, and does not let users select a date range for performance analysis. Users cannot quickly answer basic operational questions such as whether sales are improving, whether profit is moving in the right direction, or how payment behavior is changing over time. This causes slower decisions, fragmented analysis across multiple screens, and lower confidence in day-to-day shop operations.

## Solution

Deliver Slice A of the dashboard modernization as a real end-to-end date-range and chart experience. The dashboard will keep existing operational and financial semantics but shift to a chart-first interaction model, with Last 30 Days default context, explicit date-range controls, and role-aware chart visibility.

The solution includes:
- Real backend date-range API contract (`startDate`, `endDate`) with guardrails.
- PrimeNG chart rendering for trend and mix analysis.
- Apply-based reload flow with validation and safe loading behavior.
- Previous-period comparison for Owner/Manager users.
- Shop-local Reporting Day semantics and i18n-complete UI messaging.

## User Stories

1. As an Owner, I want the dashboard to open with Last 30 Days selected, so that I can immediately see meaningful trend context.
2. As a Manager, I want quick presets (Today, Last 7 Days, Last 30 Days, This Month, Last Month), so that I can switch reporting windows quickly.
3. As a Staff user, I want consistent preset behavior, so that I can understand activity without configuring dates manually.
4. As an Owner, I want a custom date range picker, so that I can analyze arbitrary windows.
5. As a Manager, I want chart data to reload only when I click Apply, so that incomplete date selection does not trigger noisy requests.
6. As any user, I want invalid ranges to disable Apply, so that I do not submit bad requests.
7. As any user, I want inline validation messages for invalid ranges, so that I know exactly how to fix them.
8. As any user, I want future end dates auto-corrected to today with helper feedback, so that accidental future selection does not block analysis.
9. As an Owner, I want daily Sales Booked trend in a line chart, so that I can spot sales momentum.
10. As a Manager, I want daily Profit trend in a line chart, so that I can detect earnings movement over time.
11. As an Owner, I want Payment Mix shown as a donut chart for the selected range, so that I can evaluate payment behavior concentration.
12. As a Staff user, I want operational chart visibility only, so that sensitive financial information remains protected.
13. As an Owner, I want financial charts visible for Owner/Manager only, so that financial access rules remain enforced.
14. As a Manager, I want day-only aggregation, so that trend lines are consistent and easy to compare.
15. As any user, I want date-range selection capped at 90 days, so that chart performance stays responsive.
16. As any user, I want the previous valid dashboard data to remain visible during loading, so that context is never lost.
17. As any user, I want a loading overlay on top of existing content, so that refresh progress is clear without blank states.
18. As an Owner, I want previous-period comparison metrics for the same duration, so that I can judge direction and not just raw totals.
19. As a Manager, I want up/down comparison indicators on primary KPIs, so that trend direction is obvious at a glance.
20. As a Staff user, I want comparison indicators only for permitted operational metrics, so that role constraints are respected.
21. As any user, I want date controls in a sticky top control bar, so that context controls are always accessible while scrolling.
22. As a mobile user, I want a two-row sticky compact control layout, so that range controls remain usable on small screens.
23. As any user, I want shop-local day boundary behavior, so that dashboard days match real shop operations.
24. As any user, I want clear empty-state messages when selected range has no sales, so that I can distinguish inactivity from failure.
25. As any user, I want line chart axes preserved for zero-data periods, so that scale continuity is maintained.
26. As any user, I want payment donut hidden when no payment data exists, so that visuals do not imply false proportions.
27. As any user, I want prior range selection persisted per user in browser storage, so that dashboard context is restored on return.
28. As an Owner, I want API responses pre-aggregated for charts, so that client rendering is lightweight and consistent.
29. As an Engineering team member, I want current-period and previous-period values in one API response, so that comparison logic remains atomic.
30. As a Manager, I want the dashboard to reduce visual clutter by emphasizing charts and primary KPIs first, so that it feels intuitive.
31. As any user, I want secondary metric sections collapsed by default, so that first-load cognitive load is lower.
32. As any user, I want all new strings localized through translation keys, so that the experience is consistent across supported languages.
33. As an Engineering team member, I want backend role-aware shaping to remain the source of truth, so that frontend cannot accidentally expose restricted data.
34. As an Engineering team member, I want validation enforced both client-side and server-side, so that contracts are robust.
35. As a Product owner, I want Slice A to ship with measurable completion criteria, so that delivery quality is auditable.

## Implementation Decisions

- Keep a two-slice delivery strategy. This PRD covers Slice A only.
- Preserve endpoint route and evolve contract from single day filter to `startDate` and `endDate` query parameters.
- Enforce server-side range validation rules:
- `startDate` must be less than or equal to `endDate`.
- Maximum range is 90 days.
- `endDate` must not exceed current shop-local day.
- Keep day-level aggregation only for Slice A.
- Return pre-aggregated chart series in API payload.
- Return previous-period comparison aggregates in the same API response.
- Keep role-aware response shaping at backend boundary. Owner/Manager receive financial chart data; Staff receives only operational chart data.
- Preserve existing Financial KPI Access and Operational KPI Access semantics.
- Use shop-local Reporting Day boundaries for all date-window metrics.
- Keep Highest Due Customer and due rankings as live snapshot semantics unless explicitly changed in a future slice.
- Build dashboard interaction around Apply-based reload, not auto-refresh-on-each-edit.
- Keep stale data visible while a new request is loading, with a dashboard-local loading overlay tied to dashboard feature loading state.
- Introduce sticky top control bar with presets and custom range controls.
- Implement mobile behavior as two-row sticky compact layout.
- Persist last selected range per browser user using client storage and restore on dashboard open.
- Introduce PrimeNG chart components for line and donut charts.
- Make chart-first information hierarchy with only top primary KPIs above charts; move secondary metrics into collapsed sections.
- Keep secondary sections collapsed by default.
- Include explicit empty-state behavior for no-sales windows:
- show no-activity message,
- keep line chart axes/zero baseline,
- hide payment donut and show no-payment-data message.
- Use i18n keys for all new labels, validation strings, helper messages, empty states, and comparison badges.
- Keep calculations aligned to shared glossary terms in domain language (Reporting Day, Sales Booked, Profit Before Tax, Profit After Tax, Payment Mix, etc.).
- Maintain active shop scoping and existing auth/session conventions.
- No write-model or persistence schema changes are required for Slice A.

## Testing Decisions

- A good test validates externally observable behavior, business meaning, and contract semantics, not private implementation details.
- Backend tests should cover:
- `startDate` and `endDate` contract handling,
- invalid range rejection,
- 90-day cap enforcement,
- future-date handling,
- role-based shaping for chart and comparison fields,
- day-bucket aggregation correctness,
- previous-period comparison correctness,
- shop-local boundary behavior.
- Frontend state tests should cover:
- default Last 30 Days,
- preset switching,
- Apply-only reload,
- invalid-range disabled apply and inline validation,
- future-date auto-correction helper,
- stale-data retention while loading,
- persisted-range restore behavior,
- role-aware rendering behavior.
- Frontend component tests should cover:
- sticky control bar interactions,
- mobile two-row layout behavior,
- chart rendering with valid series,
- no-data chart empty states,
- collapsed-by-default secondary sections,
- i18n key usage for visible strings.
- Prior art should follow existing dashboard reducer/effects tests, existing interceptor-driven loading patterns, and existing backend query/controller unit and integration test patterns.

## Out of Scope

- Slice B visual polish and deeper aesthetic redesign details.
- New chart types beyond the locked Slice A set.
- Week/month aggregation toggles.
- Websocket/live push dashboard updates.
- Per-shop configurable credit warning threshold management UI.
- Net Sales Metric introduction and returns-adjusted KPI family.
- Changes to unrelated feature pages outside dashboard scope.

## Further Notes

- Slice A Definition of Done:
- Date range presets and custom range function with Apply-only reload and validation.
- Backend enforces range rules and role-safe payload behavior.
- Three locked charts render correctly with role-aware visibility.
- Previous-period comparison and direction indicators are present for Owner/Manager.
- i18n keys and supported-language translations are provided for new copy.
- New backend and frontend tests for range/chart behavior pass.
- This PRD is intended to be published to the project issue tracker with label `needs-triage`.