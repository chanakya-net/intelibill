# Intelibill

Shared domain language for shop operations reporting and decision support.

## Language

**Reporting Day**:
A reporting window bounded by 00:00:00 to 23:59:59 in the active shop's local timezone.
_Avoid_: Today (without timezone), server day

**Profit Before Tax**:
Revenue before tax minus total cost for the same reporting scope.
_Avoid_: Margin (without tax qualifier)

**Profit After Tax**:
Revenue after tax minus total cost for the same reporting scope.
_Avoid_: Net profit (if other deductions are not included)

**Sales Booked**:
Sum of invoice total amounts created in the reporting scope.
_Avoid_: Collection, cash-in

**Cash Collected**:
Sum of paid amounts received in the reporting scope.
_Avoid_: Sales total, revenue total

**Credit Sales Amount**:
Sum of invoice total amounts whose payment method is Credit in the reporting scope.
_Avoid_: Outstanding due

**Credit Sales Percentage**:
Credit Sales Amount divided by Sales Booked for the same reporting scope.
_Avoid_: Credit share of collected cash

**Payment Mix**:
Distribution of Sales Booked by payment method (Cash, UPI, Card, Credit) in the same reporting scope.
_Avoid_: Collection mix

**Running Low Stock**:
An inventory item whose current quantity is less than or equal to its reorder level.
_Avoid_: Low stock by fixed dashboard threshold

**Critical Stock**:
An inventory item whose current quantity is zero.
_Avoid_: Low stock

**Expense Recorded**:
Sum of new expense entries created in the reporting scope.
_Avoid_: Net expense

**Expense Correction**:
Sum of correction adjustments applied in the reporting scope.
_Avoid_: New expense

**Net Expense**:
Expense Recorded plus Expense Correction for the same reporting scope.
_Avoid_: Raw expense total

**Outstanding Due**:
Current receivable balance for a customer based on customer ledger entries.
_Avoid_: Today's due sales sum

**Highest Due Customer**:
Customer with the maximum current Outstanding Due in the active shop.
_Avoid_: Customer with highest due created today

**Live Snapshot Metric**:
Current-state metric that is intentionally independent of reporting day filters.
_Avoid_: Time-window aggregate

**Dashboard Refresh Cadence**:
Automatic dashboard refresh interval set to every 5 minutes.
_Avoid_: High-frequency polling by default

**Data Freshness Stamp**:
Displayed timestamp indicating when dashboard data was last successfully refreshed.
_Avoid_: Hidden staleness

**Financial KPI Access**:
Dashboard visibility rule where profit, expense, and receivable metrics are restricted to Owner and Manager roles.
_Avoid_: Role-blind financial visibility

**Operational KPI Access**:
Dashboard visibility rule where Staff sees operational metrics such as sales count and stock status.
_Avoid_: Full financial exposure

**No-Activity Empty State**:
Dashboard behavior where KPI cards remain visible, values render as zero, and a hint indicates no activity in the selected reporting scope.
_Avoid_: Hidden cards, blank values

**Due Ranking Eligibility**:
Customers are eligible for highest-due ranking based on Outstanding Due even when profile details are incomplete.
_Avoid_: Excluding unnamed customers

**Customer Display Fallback**:
When customer name is missing, display a fallback label using available phone identifier.
_Avoid_: Blank customer identity

**Gross Sales Metric**:
Sales metric that does not include return or reversal adjustments.
_Avoid_: Net sales

**Net Sales Metric**:
Sales metric that includes return or reversal adjustments and is reported separately from gross sales.
_Avoid_: Implicitly mixed totals

**Precision Rule**:
Calculations retain full precision and are not rounded during computation.
_Avoid_: Early rounding

**Currency Display Rule**:
Currency values are rendered with two decimal places using a consistent locale format.
_Avoid_: Inconsistent decimal display

**Percentage Display Rule**:
Percent values are rendered with one decimal place using a consistent locale format.
_Avoid_: Mixed percent precision

**Dashboard Alert Priority**:
Default alert order is Critical Stock first, then Highest Due Customer risk, then Running Low Stock, then credit-share warning.
_Avoid_: Unordered or purely visual alert stacking

**Credit Share Warning Threshold**:
Default warning trigger when Credit Sales Percentage is at or above 40.0%, with future per-shop configurability.
_Avoid_: Fixed hardcoded threshold forever

## Relationships

- A **Reporting Day** is evaluated within exactly one active **Shop**
- Dashboard metrics aggregate events (sales, expenses, stock state) inside one **Reporting Day**
- **Profit Before Tax** and **Profit After Tax** are derived from the same revenue/cost set for a given scope
- **Sales Booked** and **Cash Collected** are distinct metrics and must not be merged
- **Credit Sales Percentage** uses **Sales Booked** as denominator, not collected cash
- **Payment Mix** compares Cash, UPI, Card, and Credit over the same scope
- **Running Low Stock** is evaluated from current quantity against reorder level
- **Critical Stock** is a subset where current quantity is exactly zero
- **Net Expense** is the primary expense KPI and includes correction adjustments
- **Highest Due Customer** is selected from current Outstanding Due snapshot
- **Highest Due Customer** is a **Live Snapshot Metric** and does not change with reporting day filter
- Dashboard auto-refresh uses **Dashboard Refresh Cadence** of 5 minutes
- Dashboard always exposes **Data Freshness Stamp** and supports manual refresh
- **Financial KPI Access** is restricted to Owner and Manager
- **Operational KPI Access** is available to Staff
- During no-activity periods, dashboard uses **No-Activity Empty State** with visible zero values
- **Due Ranking Eligibility** depends on receivable balance, not profile completeness
- Current dashboard sales KPIs are **Gross Sales Metric** only
- Future **Net Sales Metric** must be separate and explicitly labeled
- Dashboard follows **Precision Rule** for computation
- Dashboard follows **Currency Display Rule** and **Percentage Display Rule** for rendering
- Dashboard highlights alerts using **Dashboard Alert Priority**
- Credit-share warning uses **Credit Share Warning Threshold** default 40.0%

## Example dialogue

> **Dev:** "Should we compute today's numbers in UTC?"
> **Domain expert:** "No. A **Reporting Day** follows the active shop's local timezone, not the server timezone."

> **Dev:** "If cost is 100, revenue before tax is 150, and revenue after tax is 165, what are profits?"
> **Domain expert:** "**Profit Before Tax** is 50 and **Profit After Tax** is 65."

> **Dev:** "Can I show only one number called total sales?"
> **Domain expert:** "Use both: **Sales Booked** for invoices created and **Cash Collected** for money received."

> **Dev:** "How do we show credit contribution?"
> **Domain expert:** "Show **Credit Sales Amount**, **Credit Sales Percentage**, and a **Payment Mix** split across Cash/UPI/Card/Credit."

> **Dev:** "How do we identify low stock items?"
> **Domain expert:** "Use **Running Low Stock** when quantity is at or below reorder level, and **Critical Stock** when quantity is zero."

> **Dev:** "Should dashboard expense ignore corrections?"
> **Domain expert:** "No. Show **Net Expense** as primary, with **Expense Recorded** and **Expense Correction** alongside for transparency."

> **Dev:** "Who is the customer with highest due?"
> **Domain expert:** "The one with the highest current **Outstanding Due** from ledger balance, not just today's credit activity."

> **Dev:** "Should changing the date range affect highest due customer?"
> **Domain expert:** "No. It is a **Live Snapshot Metric** and remains based on current balance."

> **Dev:** "How often should dashboard metrics auto-refresh?"
> **Domain expert:** "Every 5 minutes, with manual refresh and a visible **Data Freshness Stamp**."

> **Dev:** "Should staff see profit and due metrics?"
> **Domain expert:** "No. Staff gets **Operational KPI Access**; financial KPIs remain under **Financial KPI Access** for Owner/Manager."

> **Dev:** "What should the dashboard show if there are no sales in the selected period?"
> **Domain expert:** "Keep cards visible with zero values and show a **No-Activity Empty State** hint."

> **Dev:** "Should unnamed customers be excluded from highest due ranking?"
> **Domain expert:** "No. Keep them in ranking using **Customer Display Fallback** with phone-based identity."

> **Dev:** "Should returns be mixed into existing sales KPIs now?"
> **Domain expert:** "No. Keep explicit **Gross Sales Metric** now; add **Net Sales Metric** later as a separate KPI."

> **Dev:** "How should amounts and percentages be rounded on dashboard?"
> **Domain expert:** "Keep full precision in calculations, display currency to 2 decimals and percentages to 1 decimal."

> **Dev:** "Which alert should appear first on dashboard load?"
> **Domain expert:** "Use **Dashboard Alert Priority**: Critical Stock, Highest Due Customer risk, Running Low Stock, then credit-share warning."

> **Dev:** "At what credit share should we warn?"
> **Domain expert:** "Warn at **Credit Share Warning Threshold** of 40.0% by default, and allow per-shop configuration later."

## Flagged ambiguities

- "today" was ambiguous between server timezone and shop timezone; resolved: use **Reporting Day** in active shop local timezone.
- profit label meaning was ambiguous against existing code naming; resolved domain meaning is:
	- **Profit Before Tax** = **Revenue Before Tax** - **Total Cost**
	- **Profit After Tax** = **Revenue After Tax** - **Total Cost**
- "total sales" was ambiguous between booked and collected money; resolved into **Sales Booked** and **Cash Collected**.
- credit-share denominator was ambiguous; resolved: **Credit Sales Percentage** = **Credit Sales Amount** / **Sales Booked**.
- "running low" threshold was ambiguous; resolved: compare current quantity to item reorder level.
- "expense done" was ambiguous; resolved into **Expense Recorded**, **Expense Correction**, and **Net Expense**.
- "highest due customer" was ambiguous across timeframe; resolved to current outstanding ledger snapshot in active shop.
- date-filter coupling was ambiguous for receivable risk metric; resolved: **Highest Due Customer** is filter-independent.
- refresh frequency was ambiguous; resolved to 5-minute auto-refresh cadence with manual refresh and visible freshness timestamp.
- role visibility was ambiguous; resolved with separate **Financial KPI Access** and **Operational KPI Access**.
- empty-state behavior was ambiguous; resolved to visible zeroes plus explicit no-activity hint.
- highest-due identity behavior was ambiguous; resolved by ranking all customers with due and rendering fallback label when name is missing.
- returns-adjustment behavior was ambiguous; resolved to gross-only KPIs now with separately labeled net metrics in future.
- rounding behavior was ambiguous; resolved by full-precision computation with standardized currency and percentage display rules.
- alert ordering was ambiguous; resolved with explicit default priority sequence for first-load attention.
- credit-share warning trigger was ambiguous; resolved to default 40.0% with future per-shop configurability.
