## Problem Statement

Shop operators and managers currently need to open multiple screens to understand daily performance, inventory risk, receivable risk, and payment behavior. This creates delayed decisions, inconsistent interpretation of business metrics, and reduced confidence in operational priorities. The absence of one trusted dashboard increases cognitive load and causes teams to react late to Critical Stock, rising credit dependence, and customer due concentration.

## Solution

Deliver a role-aware dashboard that presents operational and financial KPIs in one place, using shared domain definitions and consistent rendering rules. The dashboard will support Reporting Day filters for period-based KPIs, preserve Live Snapshot behavior for receivable risk, surface prioritized alerts, and expose payment behavior through Credit Sales Amount, Credit Sales Percentage, and Payment Mix. The experience will refresh every 5 minutes, support manual refresh, and display a Data Freshness Stamp.

## User Stories

1. As an Owner, I want to see Sales Booked for the Reporting Day, so that I can measure invoice activity.
2. As an Owner, I want to see Cash Collected for the Reporting Day, so that I can track real cash movement.
3. As an Owner, I want Profit Before Tax and Profit After Tax, so that I can evaluate margin quality with tax context.
4. As a Manager, I want Net Expense for the Reporting Day, so that I can understand true expense impact after corrections.
5. As a Manager, I want Expense Recorded and Expense Correction values, so that I can audit why Net Expense moved.
6. As an Owner, I want Credit Sales Amount, so that I can quantify receivable exposure.
7. As an Owner, I want Credit Sales Percentage, so that I can monitor how dependent sales are on credit.
8. As a Manager, I want Payment Mix across Cash, UPI, Card, and Credit, so that I can understand payment behavior trends.
9. As a Staff user, I want operational cards without sensitive financial data, so that I can act on inventory and sales flow safely.
10. As an Owner, I want Highest Due Customer from current Outstanding Due balances, so that I can prioritize collection activity.
11. As a Manager, I want Top 5 due customers, so that I can run follow-up operations and reduce receivables risk.
12. As a Store Operator, I want Running Low Stock count, so that I can prioritize replenishment before stockout.
13. As a Store Operator, I want Critical Stock count, so that I can urgently act on zero-quantity items.
14. As a Manager, I want a low-stock action list sorted by shortage severity, so that I can allocate purchase tasks effectively.
15. As an Owner, I want no-activity periods to show zero values with a clear hint, so that I can distinguish inactivity from system errors.
16. As a Manager, I want dashboard values to update automatically every 5 minutes, so that I can rely on current information without frequent manual actions.
17. As any dashboard user, I want a manual refresh action, so that I can pull latest data after important events.
18. As any dashboard user, I want to see a Data Freshness Stamp, so that I can judge data recency before acting.
19. As a Manager, I want dashboard data to stay visible if refresh fails, so that I can continue operations while issues are resolved.
20. As an Owner, I want a warning when Credit Sales Percentage reaches the threshold, so that I can control credit policy proactively.
21. As a Manager, I want alert ordering to prioritize Critical Stock and due risk before softer signals, so that urgent actions are not buried.
22. As a Staff user, I want operational visibility of stock risk and sales count, so that daily work is guided without exposing profit metrics.
23. As an Owner, I want Highest Due Customer to remain filter-independent, so that receivable risk stays visible regardless of date filter changes.
24. As an Owner, I want customer due ranking to include customers with incomplete profile names, so that no real due risk is hidden.
25. As a Manager, I want fallback customer labeling when name is missing, so that ranked entries are still actionable.
26. As an Owner, I want gross sales KPIs to remain explicitly gross, so that return-adjusted interpretations are not mixed accidentally.
27. As a Product team member, I want future Net Sales Metric capability clearly separated, so that return workflows can be added without KPI confusion.
28. As any user, I want consistent number formatting across cards and tables, so that dashboard values are easy to compare.
29. As any user, I want currency to display with two decimals and percentages with one decimal, so that visual interpretation remains stable.
30. As a Manager, I want payment behavior and due risk represented together, so that I can make balanced credit and collections decisions.
31. As an Owner, I want role-based visibility enforced by backend contracts, so that sensitive data is protected even if frontend code changes.
32. As an Engineering team, I want one dashboard API response model for all cards, so that frontend loading and caching are simpler.
33. As an Engineering team, I want isolated modules for KPI computation and alert evaluation, so that behavior can be tested independently.
34. As an Engineering team, I want explicit glossary-aligned metric names in contracts, so that implementation and domain language do not drift.
35. As an Engineering team, I want resilient partial-response behavior, so that one failing metric does not collapse the entire dashboard.

## Implementation Decisions

- Build a dedicated Dashboard Query endpoint for aggregated read-only KPI delivery rather than composing many page-level calls on the client.
- Introduce a Dashboard Aggregation application module that orchestrates data collection and enforces glossary semantics for Reporting Day metrics.
- Extract a KPI Computation Engine deep module with a stable interface that computes Sales Booked, Cash Collected, profit pair, expense trio, credit metrics, and payment mix.
- Extract a Stock Risk Evaluator deep module that computes Running Low Stock and Critical Stock and returns ranked shortage items.
- Extract a Receivable Risk Evaluator deep module that computes Outstanding Due leaderboard and Highest Due Customer using live balances.
- Extract an Alert Prioritization module that applies Dashboard Alert Priority and credit threshold logic and emits deterministic alert ordering.
- Define role-aware response shaping at backend query level so Financial KPI Access and Operational KPI Access are enforced before payload delivery.
- Keep Highest Due Customer as a Live Snapshot Metric and intentionally independent from Reporting Day filters.
- Keep current sales dashboard metrics as Gross Sales Metric only; reserve Net Sales Metric as a future distinct contract extension.
- Adopt a single dashboard payload contract with explicit sections: period KPIs, live snapshot KPIs, payment analytics, stock risk, receivable risk, alerts, and freshness metadata.
- Include Data Freshness Stamp and refresh status in payload to support transparent stale-data behavior on the client.
- Set Credit Share Warning Threshold default to 40.0% and design configuration extension point for per-shop override in future.
- Correct existing profit-label semantic mismatch in server mapping so Profit Before Tax and Profit After Tax align with glossary meaning.
- Avoid introducing write-model changes for this phase; leverage existing repositories and query patterns.
- Frontend implementation follows feature-store patterns: actions, effects, reducer, selectors, facade, and standalone page/component composition.
- Frontend view composition separates card rendering, alert ribbon, stock/due lists, and freshness controls into reusable presentational components.
- Use unified formatting utilities for Precision Rule, Currency Display Rule, and Percentage Display Rule to avoid per-card divergence.
- Preserve active shop scoping and current authorization conventions across all dashboard reads.
- Keep module boundaries focused on deep, testable interfaces that minimize cross-feature coupling.

## Testing Decisions

- A good test validates externally observable behavior and business outcomes, not internal implementation details or private helpers.
- Unit-test the KPI Computation Engine for formula correctness, denominator behavior, rounding-display boundaries, zero-activity periods, and gross-vs-net labeling semantics.
- Unit-test Stock Risk Evaluator for reorder-level edge cases, zero quantity behavior, and shortage ranking order.
- Unit-test Receivable Risk Evaluator for balance aggregation, fallback identity handling, and live snapshot filtering behavior.
- Unit-test Alert Prioritization for deterministic ordering and threshold-trigger combinations, including non-trigger paths.
- Unit-test role-aware response shaping to ensure Staff does not receive restricted fields while Owner/Manager does.
- Integration-test the dashboard query endpoint for active shop scoping, authorization policy behavior, and composite payload structure.
- Integration-test refresh-failure behavior contract to ensure stale data can be retained with warning metadata.
- Frontend state tests should cover dashboard load, success, partial failure, refresh, and role-based visibility rendering decisions.
- Frontend component tests should assert visible behavior for no-activity empty state, alert order, metric formatting, and fallback customer labels.
- Prior art for testing includes existing reducer/effects/service and controller/query-handler patterns already used in sales, expenses, inventory, and customer account features.

## Out of Scope

- Implementing returns/cancellations flow and Net Sales Metric in this phase.
- Building a custom charting analytics suite beyond required dashboard cards and lists.
- Real-time push updates via websockets for dashboard KPIs.
- Per-shop configurable threshold management UI in this phase.
- Historical trend analysis beyond current quick filters.
- Mobile-specific redesign beyond responsive behavior expected by current frontend standards.
- Changes to write-side domain entities unrelated to dashboard read aggregation.

## Further Notes

- The dashboard contract should remain backward-compatible as new KPIs are added.
- Any future Net Sales Metric introduction should remain explicitly separate from Gross Sales Metric labels.
- The glossary in CONTEXT.md is now a source of truth and should be updated alongside any future KPI semantics changes.
- An issue should be tracked with needs-triage label to enter normal planning and delivery workflow.
