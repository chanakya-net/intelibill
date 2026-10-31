**Executed validation — 21 October 2026**

Repository: `/Users/chanakya/projects/intelibill`, commit `5485507ef743818b36d751262ca85591f0c0c10f`, branch `main`.

The assessment changed no application, test, migration or infrastructure source. New review documents/evidence are under `docs/reviews/`. The pre-existing untracked `debug_human_report.md` and `debug_llm_context.md` were left untouched.

Environment observed: macOS; .NET SDK 10.0.402 (repository requests 10.0.105 with latestMinor roll-forward); Bun 1.3.14 (packageManager declares 1.3.11); Node v26.12.0. The frontend declares Node >=24 <26, so these local frontend checks are not a substitute for reproducing the supported/pinned CI environment. Docker was initially unavailable; OrbStack was started and Docker reported 29.8.2. PostgreSQL tests use `postgres:17-alpine`. Missing Playwright Chromium was installed through the repository's Bun toolchain; no package manifest or lockfile was changed.

| Check | Result | Meaning |
|---|---|---|
| Normal backend solution build | FAIL, exit 1, 12 NU1902/NU1903 errors | Vulnerable resolved dependencies break the existing warnings-as-errors gate. |
| Backend Domain unit tests | 201 passed | Diagnostic run only; vulnerability warnings explicitly retained as warnings. |
| Backend Application unit tests | 931 passed | Same diagnostic caveat. |
| Backend API unit tests | 419 passed | Same diagnostic caveat. |
| Backend PostgreSQL integration suite | 344 passed, 0 failed/skipped; 17m 29s | Diagnostic run completed with exit 0. Tests use real disposable PostgreSQL but an elevated test principal and replaced services. |
| Production frontend build | PASS, exit 0 | Initial bundle 695.82 kB exceeds 650 kB warning budget; five component-style budget warnings. |
| Frontend unit suite | 1,547 passed, 4 skipped; 213 files passed, 1 skipped | Skipped file: ShopUpdatesSignalRService. Script-support Bun tests are not part of this `--watch=false` invocation. |
| Selected Chromium web/print suite | 37 passed, 5 skipped, 1 failed | Failure is login screenshot raw-byte baseline comparison; preceding layout/visible-control assertions passed. Not a demonstrated broken login operation. |
| Focused dashboard role browser check | 1 passed, exit 0 | Selected separately because the suite gates representative role cases to its narrow-viewport project. This remains web testing, not mobile-app testing. |
| Production service-worker identity probe | FAIL isolation; reproduced | Shop B authorization receives cached Shop A response after 8,005 ms network delay. |
| Fresh migrated PostgreSQL RLS catalog | Incomplete core-table policies | Full table flags and superuser test-role flags are saved in fresh-schema-rls.txt. |

**Commands**

From repository root:

```sh
dotnet build src/backend/Intelibill.slnx
dotnet test src/backend/Intelibill.slnx -p:WarningsNotAsErrors=NU1902%3BNU1903 --logger trx --results-directory /tmp/intelibill-review-test-results
```

The second command is a diagnostic exception on that invocation, not a change to the project or a clean release build. It keeps the warnings visible. `System.Security.Cryptography.Pkcs 10.0.0` occurs in runtime/application dependency graphs; `SSH.NET 2024.1.0` is reported in the integration-test graph. No endpoint-level exploitability claim was made for those dependency advisories.

From `src/frontend`:

```sh
bun run build
CI=true FORCE_COLOR=0 bun run test --watch=false
bun x playwright install chromium
bun x playwright test --config playwright.config.ts tests/ui-audit/login-page.spec.ts tests/ui-audit/mock/new-sale-pos.spec.ts tests/ui-audit/mock/dashboard.spec.ts tests/ui-audit/mock/sale-detail.spec.ts tests/ui-audit/print --project=chromium-desktop --max-failures=5
bun x playwright test --config playwright.config.ts tests/ui-audit/mock/dashboard.spec.ts --project=chromium-mobile --grep 'redirects staff' --output=/tmp/intelibill-dashboard-ui
```

The first broad mocked UI attempt was stopped after immediate environment failures caused by the missing Playwright browser. A one-failure probe established the cause. Those pre-install failures are not product-test failures and are not included in the completed selected-suite counts. No screenshot baselines were accepted/rewritten. The screenshot mismatch uses byte equality of PNG files in `tests/ui-audit/support/audit-page.ts:701`; browser/platform raster differences or a real visual change require separate triage. Current and baseline images were visually inspected; the current login controls are visible and unclipped.

**Production service-worker reproduction**

```sh
node /Users/chanakya/projects/intelibill/docs/reviews/2026-10-21-market-readiness-evidence/service-worker-cache-probe.mjs
```

The harness serves existing production-build assets on a loopback scratch server, registers the actual Angular production worker, and supplies a harmless synthetic `/api/review-tenant` endpoint. It first returns `{tenant: 'Shop-A'}` for Authorization Shop-A. It then delays the network response and sends the same URL with Authorization Shop-B. The worker returns Shop A's cached object after its configured eight-second timeout. This proves cache-key/isolation behavior for the actual worker/configuration; it is not a live-tenant penetration test. Browser and server close at the end.

[Result JSON](/Users/chanakya/projects/intelibill/docs/reviews/2026-10-21-market-readiness-evidence/service-worker-cache-result.json)

**Database observation**

Read-only query against the disposable integration container while tests were running:

```sql
SELECT tablename, rowsecurity
FROM pg_tables WHERE schemaname='public' ORDER BY tablename;
SELECT rolname, rolsuper, rolbypassrls
FROM pg_roles WHERE rolname=current_user;
```

It returned false for core shop-owned tables, true for several later-added tables, and `integration|t|t` for the test principal. This is live schema evidence consistent with the checked-in migrations. It is not a query against deployed Azure PostgreSQL. No writes were made to the test database by this inspection.

**Coverage boundaries**

Unit and mocked browser success does not establish real browser→API→database flow, OAuth/SMTP delivery, financial arithmetic policy, concurrent return safety, production-role RLS, production cache isolation or load/recovery guarantees. General integration tests replace distributed cache and run with elevated database privileges. The report records these limits rather than treating its total test count as market-readiness evidence.

Completed output is preserved with this review:

- [Normal backend build log](/Users/chanakya/projects/intelibill/docs/reviews/2026-10-21-market-readiness-evidence/logs/backend-build.log)
- [Diagnostic backend test log](/Users/chanakya/projects/intelibill/docs/reviews/2026-10-21-market-readiness-evidence/logs/backend-tests.log) and [TRX result summary](/Users/chanakya/projects/intelibill/docs/reviews/2026-10-21-market-readiness-evidence/logs/backend-trx-summary.json)
- [Production frontend build log](/Users/chanakya/projects/intelibill/docs/reviews/2026-10-21-market-readiness-evidence/logs/frontend-build.log)
- [Frontend unit-test log](/Users/chanakya/projects/intelibill/docs/reviews/2026-10-21-market-readiness-evidence/logs/frontend-tests.log)
- [Selected web/print browser-test log](/Users/chanakya/projects/intelibill/docs/reviews/2026-10-21-market-readiness-evidence/logs/web-ui-validation.log)
- [Focused dashboard role-test log](/Users/chanakya/projects/intelibill/docs/reviews/2026-10-21-market-readiness-evidence/logs/dashboard-ui-validation.log)

Backend total: 1,895 passed, comprising 1,551 unit and 344 integration tests. The raw TRX files remain in `/tmp/intelibill-review-test-results`; the compact checked-in evidence records their suite names, counters and timestamps. All backend functional counts carry the diagnostic vulnerability-warning caveat above.
