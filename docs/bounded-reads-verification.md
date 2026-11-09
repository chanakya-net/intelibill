# Bounded reads verification

Verified locally on 9 November 2026. These are engineering limits and synthetic observations,
not production throughput, latency or shop-capacity promises.

## Contracts and release

`GET /api/exports/sales` supports PDF, XLSX and Tally XML. Date windows are inclusive persisted
shop-local business dates. Authorization, format/level and date validation occur before opening
one read-only PostgreSQL REPEATABLE READ snapshot. SQL preflight, roots, children, historical
originals, current return labels and cancellations share its context/connection, explicit shop
predicates and runtime RLS. Dispose the snapshot before rendering. Existing weaker transactions
are refused; no transaction or cursor persists across requests.

| Bound | Limit |
| --- | ---: |
| PDF logical output rows | 2,000 |
| XLSX / Tally XML logical output rows | 50,000 |
| Distinct source records needed by an export | 50,000 |
| Records returned by each source/child page query | 1,000 |
| Customer / supplier directory page | 100 (default20) |
| Directory search | 200 characters |

Logical rows count data-bearing sections, excluding fixed metadata/totals/tax summaries:

- PDF: selected sale headers or lines, return/void postings, cancellation headers and lines.
- XLSX summary: sale headers, an optional earlier-invoice-return aggregate row, return/void postings,
  cancellation summary rows, cancellation headers and lines. XLSX line mode counts sale lines,
  return/void postings and cancellation headers/lines.
- Tally: at least one unit per sale (otherwise its line count), return/void headers and lines,
  cancellation headers and lines. Both an in-window return and its in-window void count separately.

Source counts cover distinct needed sale roots/items (including historical originals), return
roots/items/settlements, cancellation roots/lines, credit notes and item-name fallbacks. Overlapping
dependencies are deduplicated in SQL. Aggregates may scan histories inside PostgreSQL; materialized
pages remain capped. Oversize requests return400 `Export.PdfRowLimitExceeded`,
`Export.RowLimitExceeded` or `Export.ReadLimitExceeded` with a narrower-range remedy and count/limit
metadata. They do not load details or render. No accepted file is silently truncated.

Sales keysets order by `(SoldAt DESC, Id DESC)`, returns by `(ProcessedAt ASC, Id ASC)`,
cancellations by `(CancelledAt ASC, Id ASC)`, children by `(parentId, Id)`, using PostgreSQL UUID
ordering. Final return posting order retains its return-ID tie breaker. Cancellation tokens reach
all commands and page boundaries, the pre-render boundary and existing renderer loops. ClosedXML
SaveAs / QuestPDF GeneratePdf remain opaque synchronous calls with no immediate-interruption promise.

`GET /api/{customers|suppliers}/directory` accepts trimmed, case-insensitive literal substring
`search`, `status=all|active|inactive`, `pageNumber` default1, `pageSize` default20/max100. Filtering
precedes complete `totalCount`, displayed-name/ID order and offset page selection. `%`, `_` and
backslash are literal characters. Supplier `include_system` defaults true, with the displayed
`Unassigned` alias searchable; management passes false. Invalid status, bounds, overflowing offsets
or overlong search return400. Responses are `{items,totalCount,pageNumber,pageSize,summary}`.
`summary` covers all visible shop parties regardless of search/status, respecting `include_system`.
Customer fields: totalCustomers, outstandingBalance, outstandingCount, totalCreditIssued,
accountsWithCredit, monthlyRevenue. Supplier fields: totalSuppliers, activeSuppliers,
preferredSuppliers, pendingPayables. Receivables/payables sum positive per-party net balances;
customer unknown ledger types are rejected. Monthly revenue retains shop-local windows and
separately dated cancellation subtraction. Page enrichment uses returned IDs; SQL returns balances,
not ledger entity histories. Interactive directories have normal offset consistency and no
cross-request snapshot guarantee.

Scoped `/api/customers/{customerId:guid}` and `/api/suppliers/{supplierId:guid}` preserve existing DTOs,
live authority and foreign/missing404. Legacy array routes remain compatible and **unbounded**;
grouped balances improve them, but first-party online consumers use the new directory/ID routes.
Offline POS continues using its owned device snapshot, without online directory/ID calls or
changes to invoice/till/lease envelopes and historical payment recovery.

Deploy the API routes before/with the new web build. No migration/index was added. Application
rollback retains legacy array compatibility and requires no schema rollback; it also restores the
old performance limitations. This work does not change financial posting policy, export totals,
mobile workflows or account-statement pagination.

## Restricted-role probe and adversarial coverage

`ReadQueryProbe` is test-only EF interception with a forwarding DbDataReader that counts actual
successful Read/ReadAsync calls. Each operation/request has a correlation scope; migrations,
auth/session setup and unrelated background commands are excluded. SQL parameter values are never
recorded. Recorded row totals include scalar/grouped aggregate results and repeated projections,
so they can exceed the distinct-source count. Assertions check both actual rows and capped SQL
materialization, not just the presence of LIMIT.

The tests use the real PostgreSQL17 Testcontainers runtime role without ownership/BYPASSRLS:

- 100 and1,000 one-line invoices, each with a return: exact financial reference, line labels,
  tax/return totals, and exact command budgets of26 and36. For this fixture, preflight is14 commands.
  With `C=ceil(N/1000)`, `F=floor(N/1000)` and `E=1` for exact multiples of1000 (otherwise0), total
  commands are `14 + 10*C + 4*F + 6*E + 2`. The fixed2 is the empty cancellation stream; four child
  streams have full-page sentinels, and exact root boundaries add six empty-stream commands. This
  fixture-specific formula admits page growth, never one command per invoice. Historical originals,
  credit notes and other child streams similarly add fixed commands per capped page.
- 1,001 equal-timestamp sale and return roots, 1,001 equal-timestamp cancellation roots and2,001
  nested sale children: exact completeness/uniqueness and PostgreSQL keyset order. A large original
  invoice referenced only by an adjustment is counted before detail loading; adjustment-only return
  and void windows retain their original-sale dependency and separate dated financial effects.
- Awaitable reader barriers pause after the first page or first preflight result. A second connection
  commits an insert, return void and cancellation. The in-flight snapshot retains its earlier rows,
  labels and status; a fresh export sees the commits. No timing sleeps establish these races.
- Cancellation at the first1,000-root reader stops before child/next-page commands and disposes the
  transaction. Explicit foreign-shop predicates still return no sales/returns/cancellations under RLS.
- Runtime API requests reject a2,001-line PDF and a one-summary-row/50,001-source XLSX using only
  scalar preflight results, with zero detail readers. An accepted49,999-line XLSX consumes exactly
  50,000 source records; every reader returns at most1,000 records and rendering occurs after dispose.
- 10,000 customers and10,000 suppliers, each with20,000 ledger entries: page100 reaches the late
  names; a late-only active search returns10 items and the complete50-match count. Global cards
  retain exact10,000-party totals, positive balances30,000, customer outstanding count5,000 and credit
  limits50,000, supplier active5,000/preferred3,334. Commands are exactly9/customer page and5/supplier
  page, unchanged across first/last/filtered pages. Ledger readers return aggregates; maximum reader
  cardinality is100. The all-entry-type test also asserts bounded reader rows; the unknown-type
  test proves rejection of a persisted future type under the restricted role.

## Local measurements

Measurements exclude fixture seeding and include database preflight/materialization. XLSX includes
rendering as indicated. Allocations use the process-wide GC cumulative allocated-byte counter;
background work can contribute. A50ms timer samples managed heap estimates and process resident
memory during measured operations; these are sampled observations, not exact maximum live-object
sizes. Runtime PeakWorkingSet64 returned0 on this macOS runner and is not used as a memory claim.

| Synthetic operation | Business commands | Actual rows read | Max reader rows | Elapsed | Allocated MiB | Sampled managed / resident MiB |
| --- | ---: | ---: | ---: | --- | ---: | ---: |
|100 invoices +100 returns|26|614|100|18ms|2.3|277.0 /388.4|
|1,000 invoices +1,000 returns|36|6,014|1,000|471ms|25.7|265.3 /386.8|
|49,999-line XLSX,50,000 source records|74|50,014|1,000|1,599ms read;6,017ms including render|3,353.3|502.3 /627.5|
|10,000 customers /20,000 ledger rows|9 per page|205 /205 /25 across first/last/filtered|100|323ms for3 requests|25.8|513.4 /1,049.4|
|10,000 suppliers /20,000 ledger rows|5 per page|203 /203 /23 across first/last/filtered|100|253ms for3 requests|23.5|536.0 /1,194.7|

The near-limit file was2,907,143 bytes. Measurements are from `/tmp/task4-memory-final.log` and
`/tmp/task4-memory-final.trx` (4 checks passed). Memory samples include the process's retained
fixture/runtime state and earlier tests; they are not incremental per-request memory. Fixture
insertion time is excluded from the elapsed/allocated intervals. Cumulative allocations are not
simultaneously resident memory. The50ms sampler can miss shorter peaks and adds diagnostic overhead.

The synthetic work completed within this runner's memory envelope. The near-limit sampled resident
peak of627.5MiB is below the repository-configured API container limits (dev2Gi/prod1.5Gi in
`.tofu/modules/environment-infrastructure/main.tf`); this is one local process observation, not a
container/concurrency test or confirmation of live infrastructure. Concurrent exports, production
heap/traffic and different data may need a lower engineering cap or background jobs. The3.516GB
cumulative allocation is distinct from the sampled resident peak. No index change or query-plan
remedy was indicated by these read measurements, so no speculative index/EXPLAIN-driven migration
was added. Representative production data/distributions, concurrent load, deployed memory and
rendering-library behavior still need environment-specific measurement.

## Verification and review evidence

- Full settled backend on6617e690: Domain468 / Application1802 / Api587 / Integration1136 passed;
  `/tmp/intelibill-bounded-backend-final.log`. Later backend changes are test/docs only, so this green
  full suite was retained rather than duplicated.
- Final adversarial plus return/cancellation accounting integration run:48 passed,0 failed,
  `/tmp/task4-bounded-final.log`, `/tmp/task4-bounded-final.trx`. Four supplemental measurement checks passed after adding the sampler and exact9/5
  directory assertions: `/tmp/task4-memory-final.log`, `/tmp/task4-memory-final.trx`. Existing dashboard/P&L regression coverage is included in the full backend run.
- EF `has-pending-model-changes --no-build --project src/backend/Intelibill.Infrastructure
  --startup-project src/backend/Intelibill.Api`:exit0, no changes; `/tmp/task4-pending-model.log`.
- Final frontend1278fe06:294 files /2,918 tests plus59 worker/style checks passed;
  `/tmp/intelibill-bounded-final-fix-frontend.log`. Production build passed,
  `/tmp/intelibill-bounded-final-fix-build.log`, retaining existing bundle/stylesheet budget warnings.
- Independent Task1/Task2 reviews approved. Task3's four P2 findings were fixed and re-reviewed:
  intake restore ownership, PO supplier-only restoration, supplier payment current-page refresh and
  offline transition during POS debounce. Their focused regression run passed1,715 tests.
- Independent whole-branch review then found one P2: stock intake inferred a supplier ID from a bare
  name appearing unique within only the20-result page. Fix1278fe06 removes that fallback while
  preserving explicit ID-bearing labels and selected/restored/product-default identities. The
  real21-party/20-result regression failed1/28 before the fix;70 focused tests and the final full
  frontend/build passed after it. Scoped re-review approved the correction and branch code,
  with all findings addressed. Its final release-audit/evidence condition was satisfied below.
- Release diagnosis separated a real stale widget timer/loading bug from a full-page axe scan whose
  post-interaction scroll left a control partly under the sticky header. The widget fix has2 red
  behavioral tests followed by12 green; the audit now awaits real popup closure and normalizes the
  full-page viewport, retaining all axe rules. Telugu repeated3/3 passed across desktop/mobile/reflow;
  `/tmp/intelibill-bounded-browser-{red,green,te-repeat}.log`.
- Final production release audit on rebuilt1278fe06 passed58/58 in6.8minutes, exit0;
  `/tmp/intelibill-bounded-release-final.log`. This supersedes the earlier2052873c browser result.
  [Final captures and compact original result excerpts](reviews/2026-11-09-bounded-reads-evidence/README.md)
  preserve the final evidence; all generated changes to prior accessibility screenshots were restored.
  Independent whole-branch review and scoped re-review are approved, with all findings addressed
  and the final browser condition satisfied.

Local `/tmp` logs are execution artifacts; the linked evidence directory retains compact original
summaries and final captures. This document records the reviewable contracts, counts and limitations.
