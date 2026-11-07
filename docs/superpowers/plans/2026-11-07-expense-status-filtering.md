# Server-side Expense Status Filtering Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. The user selected Astra analysis, Sol implementation, then thorough independent review and fixes followed by a PR; implementation is already authorized.

**Goal:** Expense status/search filters select the complete matching dataset, with correct counts, stable pages, filter-preserving correction reloads, and explicit page summaries.

**Architecture:** Validate an optional HTTP status string, map it to a domain filter, and apply it to the same EF query used for count and page retrieval. Keep one canonical web query in NgRx; immutable request identities, existing `switchMap`, and existing shop-scope isolation prevent obsolete responses from replacing it. Refresh the current query after mutations and request the last valid page when its count shrinks.

**Tech Stack:** ASP.NET Core 10, ErrorOr, FluentValidation/Wolverine, EF Core/PostgreSQL; Angular 21, NgRx, PrimeNG, Transloco, Bun; xUnit/Testcontainers, Vitest, Playwright.

**Spec:** The user-supplied attachment is the historical source; the portable requirements are recorded here: apply status and search before count/paging, retain all by default, reject unsupported status/paging deterministically, keep ordering stable, preserve filters after corrections while recovering valid pages, exclude obsolete query/shop responses, and label loaded-row amounts/counts as page summaries. Historical rationale: `docs/reviews/2026-10-21-market-readiness.md` and its web-workflows/backend-business evidence. This document refreshes that plan against main `62d235a52f9fc6c82a39311ea2d90834bf931c10` on 2026-11-07.

## Global Constraints

- Web and API only; mobile excluded. No schema migration, backfill, index, or profit-definition changes.
- Keep default status `all`, page `1`, page size `20`; supported page size is `1–100`.
- HTTP `all`, `active`, `voided` are case-insensitive. Omitted/blank status means `all`; surrounding whitespace is trimmed. Unknown strings, including numeric enum strings, return HTTP 400 with stable `Expense.InvalidStatus`.
- Preserve Domain ← Application ← Infrastructure ← Api, repository abstractions, authenticated active-shop scope, and existing RLS.
- Bun only for frontend; C# warnings remain errors. No new dependency required.
- Preserve merged `ExpenseTreatment`, supplier-settlement provenance, correction restrictions, and existing active-only page amount arithmetic. This list does not define accounting profit.

## Review Focus

- RF1: The only voided match is outside the unfiltered first page; status filtering still finds it (Task 1).
- RF2: Search plus status plus pagination share predicates; enumerated stable pages contain exactly the reported unique matching IDs (Task 1).
- RF3: Correction changes search/status membership on the final page; reload preserves filters and moves back only when necessary (Task 3).
- RF4: Invalid status, malformed/unbounded paging, overflow, and identical timestamps produce deterministic errors or stable pages (Task 1).
- RF5: Rapid filter changes, old failures, and scope resets cannot replace the current query/result or initiate a fallback request for a departed shop (Task 2).

## Current source and decisions

CodeGraph was used first and for symbol/impact exploration; its returned source is current on disk. Additional direct reads filled records, templates, tests, and configuration not surfaced by the graph.

- `ExpensesPageComponent.filteredExpenses` filters already loaded rows. Status change only updates a signal. Search/page calls omit status; page sizes are not retained explicitly.
- `ExpenseService.getExpenses(search?, page=1, pageSize=20)` and list actions/facade/effect carry no status. The list effect already uses `switchMap`.
- `ExpenseRepository.GetPagedByShopAsync` filters shop and ILike search across paid-to/description/category before count, but not status. Ordering is expense date DESC, created-at DESC without an ID tie-breaker. Paging is unvalidated.
- Both record and correction overlay success subscriptions call `loadExpenses()` without filters. NgRx has paging but no canonical search/status/request identity.
- `ShopContextService.transition` increments the epoch and resets feature stores; `authInterceptor` suppresses old-scope success AND failure. Reuse this infrastructure, do not rebuild tenancy or add a separate shop store.
- Wolverine registers all validators. **Its exception path loses custom validation codes:** `SafeErrorFactory` maps to `Validation.Failed` and field property names. Therefore the expenses controller must validate the query before dispatch, map failures to `Error.Validation(failure.ErrorCode, failure.ErrorMessage)`, and use existing `ToProblemResult()`. Leave global exception/error mapping unchanged. The registered validator remains defense for direct bus callers.
- Existing page amount sums only nonvoided loaded rows; relabel it “Active amount (this page)”. Counts of active/voided rows are page counts; `totalCount` is the matching dataset count.
- Baselines reported by the coordinator: query-handler tests 1/1 and expense web specs 81/81 passed. These are baseline evidence only, not feature verification.

### Task 1: Validated dataset filtering through the real API

**Files:**
- Create `src/backend/Intelibill.Domain/Enums/ExpenseStatusFilter.cs`.
- Create `src/backend/Intelibill.Application/Features/Expenses/Queries/GetExpenses/GetExpensesQueryValidator.cs`.
- Modify `src/backend/Intelibill.Api/Controllers/ExpensesController.cs`.
- Modify `src/backend/Intelibill.Application/Features/Expenses/Queries/GetExpenses/GetExpensesQuery.cs` and `GetExpensesQueryHandler.cs`.
- Modify `src/backend/Intelibill.Domain/Interfaces/Repositories/IExpenseRepository.cs` and `src/backend/Intelibill.Infrastructure/Repositories/ExpenseRepository.cs`.
- Tests: existing `tests/backend/unit/Intelibill.Api.Unit.Tests/Controllers/ExpensesControllerTests.cs`, `tests/backend/unit/Intelibill.Application.Unit.Tests/Features/Expenses/Queries/GetExpenses/GetExpensesQueryHandlerTests.cs`, and `tests/backend/integration/Intelibill.Integration.Tests/ExpensesControllerTests.cs`; add validator tests beside query-handler tests.

**Interfaces:**
- Domain enum `ExpenseStatusFilter { All, Active, Voided }`.
- Query `GetExpensesQuery(Guid UserId, Guid ShopId, string? SearchTerm, int PageNumber=1, int PageSize=20, string? Status=null)`; append optional status to preserve existing callers.
- Repository `Task<(IReadOnlyList<Expense> Items, int TotalCount)> GetPagedByShopAsync(Guid shopId, string? searchTerm, ExpenseStatusFilter status, int pageNumber, int pageSize, CancellationToken cancellationToken)`.
- Public `GetExpensesQueryValidator : AbstractValidator<GetExpensesQuery>`; status accepted only by named values after trim/case normalization, never permissive enum numeric parsing. Page errors use `Expense.InvalidPageNumber` / `Expense.InvalidPageSize`.
- HTTP response remains existing `PaginatedList<ExpenseListItemDto>`; status changes no DTO fields.

- [x] Add regression tests before production edits: `voided_filter_finds_match_beyond_first_page` seeds 25 active plus one older voided and asserts status=voided returns precisely that ID/count=1. `status_search_pages_share_predicate` covers mixed matching/nonmatching paid-to, description, and category values and verifies IDs/count for active, voided, and all. Include another shop's matching rows and assert exclusion.
- [x] Add RF4 tests: omitted/blank/all and mixed-case statuses work; unknown/`1` status returns 400 with `title == "Expense.InvalidStatus"` and `errors` containing that code. Page 0/negative, sizes 0/101, and an offset exceeding Int32 bounds return 400, never 500 or wrapped negative offset; nonnumeric paging uses MVC's existing 400. Page size 100 works. Seed tied dates AND created-at timestamps; repeated page enumeration has stable ID ordering without duplicates and totals match.
- [x] Run `dotnet test tests/backend/integration/Intelibill.Integration.Tests --filter FullyQualifiedName~ExpensesControllerTests` and the new validator/handler unit tests. Record the expected regression failures, not just compilation failures; new HTTP tests can fail against the unchanged endpoint first.
- [x] Implement controller query prevalidation and unchanged active-shop extraction, register automatically through existing assembly scanning, map validated status in the handler, and update repository/call-site mocks. Reject `(long)(PageNumber - 1) * PageSize > int.MaxValue` with safe arithmetic and positive-page validation before repository use. Use one filtered IQueryable for both CountAsync and Skip/Take; add `ThenByDescending(e => e.Id)` after existing descending timestamps. Do not modify report repository methods.
- [x] Run application expense query tests, API controller tests, and integration expense tests until passing. Verify omitted status remains backward compatible and treatment still maps in DTOs. Commit this coherent backend change.

### Task 2: One web query, server filtering, and obsolete-response protection

**Files:**
- Modify `src/frontend/src/app/features/expenses/services/expense.service.ts` and `.spec.ts`.
- Modify `src/frontend/src/app/features/expenses/state/expenses.{actions,effects,facade,reducer,selectors}.ts` and their existing `.spec.ts` files.
- Modify `src/frontend/src/app/features/expenses/pages/expenses-page.component.{ts,html}` and `.spec.ts`.
- Modify `src/frontend/src/app/features/expenses/components/expenses-filter-bar.component.ts` to import the shared type.
- Extend `src/frontend/src/app/core/context/scope-reset.store.spec.ts` and/or add expense-specific real-store/HTTP tests to the existing effects spec.

**Interfaces:**
- Shared service types: `ExpenseStatusFilter = 'all' | 'active' | 'voided'`; `ExpenseListQuery { readonly search?: string; readonly status: ExpenseStatusFilter; readonly page: number; readonly pageSize: number }`.
- `ExpenseService.getExpenses(query: ExpenseListQuery): Observable<PaginatedExpenses>` sends status/search/page/pageSize.
- `ExpensesFacade.loadExpenses(query?: ExpenseListQuery): void` supplies full default query if absent; `query$` exposes canonical query. New `refreshExpenses(): void` snapshots that query and dispatches a fresh request identity.
- Requested action `{ query: ExpenseListQuery; requestId: string }`; success keeps existing result props plus `requestId`; failure has `{ errorMessage: string; requestId: string }`.
- State adds `query: ExpenseListQuery` and `requestId: string | null`; reset uses all/page1/20 and null. Unique request IDs are generated at dispatch, not a counter reset to zero with store state.

- [x] Add service/facade/effect/reducer/page regressions: status from active page3 to voided requests page1 and retained search/page size; searching preserves status and resets page; page changes preserve both. Stub server rows to prove the page renders the response without local status filtering. Assert status parameter and accurate returned totalCount.
- [x] Add `rapid_changes_accept_latest_response`: controlled observables complete A after B and verify only B wins; also emit A failure after B. Reducer tests explicitly ignore stale success/failure IDs. Add scope-reset test: query/results/requestId reset; old completion cannot restore them. Include a pending out-of-range response at scope transition so it cannot trigger a new-shop reload with old filters.
- [x] Run `bun run test --run 'src/app/features/expenses/**/*.spec.ts'` from `src/frontend`, demonstrating new failing expectations.
- [x] Implement interfaces above. Use immutable query snapshots and preserve `switchMap`; terminate pending list work on `ShopContextActions.scopeChanged` using `takeUntil`. Accept result/failure only when IDs match current request. Keep existing global epoch/interceptor protection. The component reads canonical query, renders server rows directly, and dispatches a full copied query for each interaction. Remove duplicated local status/search ownership. Clear or hide old page summary/results while a different query is pending so they are not presented as the new filter's data.
- [x] Render list failure separately from an empty successful result; an error must not present “no matching expenses” as if the request succeeded. Keep filter controls usable during loading so another interaction can supersede the pending request.
- [x] Run focused expense specs and `bun run test --run src/app/core/context/scope-reset.store.spec.ts`; commit after green.

### Task 3: Mutation refresh, valid-page recovery, honest summaries, and browser regression

**Files:**
- Modify list effects/facade and tests from Task 2.
- Modify `src/frontend/src/app/features/expenses/components/{record-expense-overlay,correct-expense-overlay}.component.ts` and corresponding specs.
- Modify page TS/HTML/spec and `src/frontend/tests/ui-audit/mock/expenses.spec.ts`.
- Modify all nine `src/frontend/public/assets/i18n/{en-IN,hi-IN,bn-IN,gu-IN,kn-IN,ml-IN,mr-IN,ta-IN,te-IN}.json` bundles.
- Update expense list contract in `docs/backend-architecture.md` and behavior in `docs/frontend-architecture.md`; update the expenses endpoint bullet in `AGENTS.md` if needed.

**Interfaces:**
- Overlays call `ExpensesFacade.refreshExpenses()` once on successful mutation instead of resetting query with `loadExpenses()`; retain existing close and mutation-status handling.
- List effect computes `lastPage = Math.max(1, Math.ceil(response.totalCount / response.pageSize))`. When requested page exceeds lastPage, dispatch a new load request with same search/status/pageSize, page=lastPage, and a new ID instead of publishing the invalid page. This remains inside the latest-request/scope cancellation chain. Do not loop when count=0/page1; publish the valid empty page.
- Existing locale keys remain, with precise English copy: `totalExpenses: "Matching expenses"`, `pageAmount: "Active amount (this page)"`, `activeCount: "Active expenses (this page)"`, `voidedCount: "Voided expenses (this page)"`, `filteredRows: "Expenses on this page"`. Translate equivalent meaning in every locale.

- [ ] Add `correction_reloads_current_query`: selected active/search filter/page2/size20 survives correction and valid page2 stays page2. Add `correction_removes_last_matching_row`: original is voided and replacement no longer matches search, server total falls from21 to20, then only page1 is requested with identical filters. **Do not assume every correction reduces active count:** a matching active replacement keeps it unchanged. Cover voided filter gaining a row, all filter, page1/count0, and a newer filter selected before correction success.
- [ ] Add overlay regression proving successful record/correction calls refresh rather than default load; effects regression proves invalid-page data is never installed and late recovery is canceled by a new query/scope. Add summary assertions showing matching dataset count versus page counts, and zero active amount for an all-voided page without changing accounting definitions.
- [ ] Run focused expense specs to establish red; implement refresh and page recovery, labels, and error/empty behavior; rerun to green.
- [ ] Update mocked browser route to apply search AND status before count/order/pagination. Add a realistic >20-row journey: voided record beyond original first page, status/search reset, filtered pagination, correction membership change/fallback, empty result versus API error, and page labels. Existing mocks that ignore status must be updated, not used as evidence that local filtering works. Keep desktop/mobile viewport coverage (mobile browser is web scope).
- [ ] Run from `src/frontend`: `bun x playwright test --config playwright.config.ts tests/ui-audit/mock/expenses.spec.ts`; confirm both configured viewports pass. Update architecture contract and commit.

## Final verification and handoff

Run commands sequentially where .NET build outputs overlap; do not duplicate a coordinator's running check. No migration is required.

- [ ] From repository root: `dotnet build src/backend/Intelibill.slnx` and `dotnet test src/backend/Intelibill.slnx`. Integration requires Docker, which the coordinator confirmed available.
- [ ] From `src/frontend`: `bun run test --watch=false`, `bun run build`, then `bun run audit:ui:release` (repository-required built worker isolation check). Targeted mocked expense Playwright coverage from Task 3 must also pass.
- [ ] Inspect diff for unrelated schema/mobile/profit/treatment changes, run `git diff --check`, and record actual commands/results and any environmental limitations.
- [ ] Independent whole-branch review against RF1–RF5 and the validation-code, scope-reset, recovery, and translation risks. Fix actionable findings and rerun only affected checks, then create the requested PR with concrete behavior and validation evidence.

API deployment precedes or accompanies UI deployment; omitted status remains supported. Rollback needs no data changes. Large concurrent external writes can shift offset pages between separate requests; deterministic tie-breaking does not promise a cross-request snapshot, and this task does not add snapshot pagination. No blocking product decision remains.
