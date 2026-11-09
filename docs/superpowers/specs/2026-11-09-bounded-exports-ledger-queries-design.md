# Bounded exports, ledger aggregates and party directories

Date: 2026-11-09. Source baseline: `e4c34b42`, branch `codex/bounded-exports-ledger-queries`.

## Intent and authorization

Implement the attached task 36, including its additional directory task, against current source. The user requested Astra analysis/planning, Sol implementation, thorough review/fixes and a PR, with automatic transitions. The attachment's historical “planning only” statement describes its earlier creation, not the current authorization. This document records engineering decisions, not new commercial capacity promises.

A large shop must be able to find any party without downloading its directory, and request a correctly dated financial export without a per-sale query loop or an unbounded object graph. Preserve old array clients while moving all first-party online consumers to bounded routes.

## Current evidence

- `SalesExportDatasetBuilder.BuildAsync` loads all sales/items and all period return/cancellation facts before the PDF limit. It still calls `GetBySaleAsync` once per sale at line-item level. The current return reader already batches the missing original sales and credit notes; do not reintroduce older per-return patterns.
- Returns and return voids are separate dated postings; cancellations are independently dated frozen facts. A period can have adjustments without any sale in that period. Current PDF/XLSX include those adjustments, including cancellation detail lines. Counting only sales is insufficient.
- `CustomerLedgerEntryRepository` and `SupplierLedgerEntryRepository` materialize ledger rows for balances. Supplier directory calls its balance method once per supplier.
- Customer month boundaries already use `IBusinessClock` and shop timezone. Preserve this improvement; the attachment's UTC-month finding is obsolete.
- Directory pages derive shop-wide cards from complete arrays. Paging must retain global totals via SQL aggregates, not relabel the first page as the complete shop.
- Online directory consumers also include POS, stock intake and purchase-order creation. Offline customers come from a separately owned snapshot and must retain that contract.

## Bounded export contract

Use fixed initial engineering limits: PDF **2,000 logical output rows**, XLSX/Tally XML **50,000 logical output rows**, database page reads **at most 1,000 records**. Also enforce a **50,000 materialized source-record budget**, across distinct source rows needed by the export, before loading any detail graph. This second bound protects a summary request containing a huge invoice, historical sales referenced by returns, and large physical return histories. Lowering these limits later requires measurement and an explicit change; this work adds no background-job service.

Output counting includes every data-bearing section, not metadata/header/footer rows. For PDF: selected sales rows + return/void postings + cancellation headers + cancellation detail lines. For XLSX summary: sale rows + the optional earlier-sales-return aggregate row + return/void postings + cancellation summary rows + cancellation headers/detail lines. For XLSX lines: sale lines + return/void postings + cancellation headers/detail lines. For XML, use conservative logical work units: sale lines (at least one per sale), return/void headers and lines, cancellation headers and lines. XML also retains the source-record budget so large settlements/components cannot evade the bound. Fixed-size totals and generated tax-summary rows do not consume the logical-row allowance. Count a return and its in-window void separately. Use `long` counts and checked arithmetic.

Count-source coverage includes sale roots/items selected in the period, original sale roots/items needed for adjustment projection, return roots/items/settlements needed for period postings and line-return labels, cancellation roots/lines, and credit-note/item fallback records if materialized. Deduplicate overlapping dependencies; do not query/load unbounded histories to count them. SQL counts and grouped aggregates are allowed to scan matching DB rows, but may return only scalar/bounded aggregate results. Aggregate active-return quantities by sale item in SQL where possible. Never truncate an accepted report silently.

Authorization, format/level validation and date validation precede the snapshot. `IExportReadStore.OpenSnapshotAsync` starts one read-only PostgreSQL **REPEATABLE READ** transaction on the scoped `ApplicationDbContext`. Count, pages, historical return dependencies, current physical return status, cancellation status and fallback labels all use that same context/connection/snapshot. A creation-time cutoff cannot freeze later void/cancellation updates and is rejected as an alternative. Queries retain explicit shop predicates plus runtime RLS. Dispose/rollback the read transaction after materializing the bounded dataset and **before rendering**. No transaction survives the HTTP request; cancellation is propagated to every command and page boundary. Do not silently join a weaker existing transaction. There is no cross-request continuation token.

Use keyset ordering for source streams: sales `(SoldAt DESC, Id DESC)`, return facts `(ProcessedAt ASC, Id ASC)`, cancellations `(CancelledAt ASC, Id ASC)`, children `(parentId, Id)` with actual PostgreSQL UUID ordering. Return projection then retains its existing posting order with an added return-ID final tie breaker. A return fact that produces two postings is processed once, with both dated entries emitted. Each SQL materialization is capped at 1,000, including child fetches; root pagination with unbounded `Include` collections is not sufficient. Counts and page predicates must come from the same query definitions. Keep the existing renderer dataset and financially meaningful output; refactor the dataset builder to consume bounded pages and pure existing projection rules.

Keep `Export.PdfRowLimitExceeded`; add `Export.RowLimitExceeded` for other logical-row limits and `Export.ReadLimitExceeded` for source work, with actionable narrow-date-range descriptions and metadata for limit/count/level/format where supported. A refused request performs no detail materialization and no renderer call. Check cancellation before invoking a renderer and within existing long renderer loops when feasible; no promise of interruption inside an opaque synchronous library call.

## Ledger semantics

Move sums/grouping to SQL without changing balances. Customer positive types: `SaleDue`, `ReturnCreditReversal`, `PaymentReversal`. Negative types: `PaymentReceived`, `ReturnCredit`, `SaleCancellationCredit`. Preserve rejection of unknown enum values; do not make a SQL default silently credit/debit them. Supplier: only `PaymentMade` negates its amount; every other current type uses its already signed amount, including `Reversal`, `CostAdjusted`, `SupplierReturnCredit`.

For total receivables/payables, sum positive **per-party net balances**, not positive entries and not max of the whole shop net. Empty sets produce zero. Add grouped supplier balance lookup for a bounded set of returned IDs. Legacy directory handlers also use grouped balances, though legacy arrays remain intentionally compatible and unbounded.

## Directory API and compatibility

Keep existing `GET /api/customers` and `GET /api/suppliers` response arrays and existing semantics. Add `GET /api/customers/directory` and `GET /api/suppliers/directory` with `search`, `status=all|active|inactive`, `pageNumber` (default 1), `pageSize` (default 20, maximum 100). Supplier keeps `include_system` (default true); the supplier management UI explicitly sends false. Reject invalid status, page <1, size outside 1..100, and search longer than 200 characters with validation errors. Trim search. Apply case-insensitive literal substring filtering before count/order/page; `%`, `_`, and backslash are user characters, not wildcard syntax. Preserve existing searchable fields: customer name/phone/address/ID; supplier name/contact name/contact phone/city, including the displayed system alias `Unassigned`. Stable order is displayed name then ID, with deterministic system-alias treatment. Offset paging is acceptable for interactive directories; only export requests promise a repeatable snapshot.

Responses extend `{items,totalCount,pageNumber,pageSize}` with `summary`. `totalCount` is the fully filtered count. `summary` describes all visible shop parties (ignores search/status; respects `include_system`), preserving today's cards:

- Customer: `totalCustomers`, `outstandingBalance` (sum positive balances), `outstandingCount`, `totalCreditIssued`, `accountsWithCredit`, `monthlyRevenue`.
- Supplier: `totalSuppliers`, `activeSuppliers`, `preferredSuppliers`, `pendingPayables`.

Compute summaries in SQL, not by fetching parties/ledger histories. Customer revenue preserves existing sale metrics and cancellation subtraction with the shop-local month; do not invent new return-based revenue semantics in this optimization. Page-only metrics/balances use only returned IDs. Reuse read mapping helpers so legacy and paged DTO fields agree.

Add scoped `GET /api/customers/{customerId:guid}` and `GET /api/suppliers/{supplierId:guid}` returning one existing DTO for restored selections/deep links; absent or foreign IDs answer 404. These are reads for existing members, with the same live authorization as directory reads. They do not enable inactive parties for new transactions. Existing supplier system visibility and all financial-write authorization remain unchanged.

## Web behavior

Directory state owns query, page, filtered count and global summary. Search is debounced approximately 250 ms, changes reset page 1, and latest-request-wins prevents stale results. Table pagination is remote; no second local slice/filter of the page. After mutation refresh the current query and global summary; if its last row disappeared, move to the last valid page once. Existing responsive tables, keyboard operation, focus behavior, accessible names, loading/error/empty states and all nine locales remain.

Selector search is component-scoped and independent of directory NgRx paging. Online POS searches active customers remotely; intake and purchase-order supplier searches use bounded directory calls with their existing system/active rules. Retain selection by ID as a separate object; searching or paging cannot clear or substitute it. Restore/deep-link fetches use the new ID route. Same-name suppliers/customers must be disambiguated and selected by ID, never the first matching label. Preserve free-text/unassigned workflows where currently allowed. No first-party online caller may exhaust all pages or retain a fallback to the full-array route. Preserve shop-epoch protection, including A→B→A, and dispose outstanding search subscriptions.

Offline POS continues to search its owned on-device snapshot and makes no online directory/ID requests. Do not change offline envelopes, snapshots, lease/till/expiry evidence, or durable mutation recovery.

## Scope and release

No business posting changes, data migrations, schema/index additions without representative `EXPLAIN (ANALYZE, BUFFERS)` evidence, external service dependencies, cloud deployment, mobile migration, or full account-statement pagination. The old array routes remain a documented compatibility limitation. Existing P&L/dashboard readers need not be refactored unless required to share a pure projection safely; verify their regression tests when shared types move.

Use restricted-runtime-role PostgreSQL integration tests for SQL translation, RLS, snapshots, command budgets and bounded output; unit mocks alone cannot prove these. Capture allocation/elapsed/query observations on synthetic 100/1,000/10,000-party and near-limit export fixtures, with no unsupported throughput claim. A rejected oversized export must avoid source detail reads. If an accepted near-limit XLSX exceeds the measured runner memory envelope, lower the engineering limit explicitly rather than weakening bounds or silently omitting rows. API query change deployment precedes or accompanies web. Application rollback preserves old array compatibility; there is no new schema to roll back.
