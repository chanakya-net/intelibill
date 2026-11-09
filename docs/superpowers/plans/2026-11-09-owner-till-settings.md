# Owner Till Settings Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Let an eligible shop's owner enable or safely disable till tracking from Manage shop.

**Architecture:** Persist a versioned per-shop setting behind the existing rollout gate. Serialize settings and opening with the existing till catalog lock. Keep settings UI as a focused component using the existing Manage shop cards and scoped requests.

**Tech Stack:** .NET 10, EF Core/PostgreSQL, Angular 21, Bun, PrimeNG/Transloco.

**Spec:** docs/superpowers/specs/2026-11-09-owner-till-settings-design.md

## Global Constraints

Owner only; active selected shop only; no live enablement or application DB migration. Preserve all historical till facts and keyed source recovery. Default off for new shops, backfill prior till users. Existing rollout flag plus allowlist remain necessary. No new dependencies, mixed tender or mobile.

## Review Focus

- A manager or stale owner must not alter setup; exercise live role authorization and tenant denial.
- Concurrent opening and disabling must not leave an open disabled drawer; use PostgreSQL lock tests.
- Unresolved offline evidence must block disable; explain unknowable other-device queues and require confirmation.
- Late HTTP responses after a shop transition must not update another shop's UI; test scope changes.
- Losing a PUT response must recover via authoritative GET rather than claim success or overwrite newer state.

### Task 1: Persisted settings and source enforcement

**Files:** New setting/audit entities, EF mappings/migration, domain repository contract and implementation; shop settings request/handler and Tills policy/services; relevant backend unit/integration tests.
**Produces:** GET/PUT /api/shops/{shopId}/till-settings using the spec contract.

- [x] Write and run failing owner/settings/gate/blocker tests.
- [x] Implement additive persisted setting and audit with live authority, explicit version and shared catalog lock.
- [x] Update all till-enabled decisions to include persisted preference and preserve saved operation recovery.
- [x] Generate migration with existing-fact backfill, tenant constraints/RLS and safe rollback policy.
- [x] Run focused unit and real PostgreSQL integration tests including concurrency and source enforcement.

### Task 2: Manage shop UI

**Files:** New shop-till-settings component and tests under features/shops/components/manage-shop; ShopService request/response types; Manage shop overlay; till disabled-state guidance; nine locale catalogs.
**Consumes:** Task 1 HTTP contract. PUT body { enabled, expectedVersion, reason, confirmed }.

- [x] Write and run failing settings component/service tests for ownership, gating and confirmation.
- [x] Build matching card with explicit setup consequences, reason and acknowledgement, blockers and authoritative save/recovery.
- [x] Integrate inside Manage shop operational settings and add owner navigation guidance from Tills.
- [x] Add localized copy for every supported language and test scope/error states.
- [x] Run focused frontend tests and production build.

### Task 3: Integration, visual QA and review

**Files:** Release browser audit scenario, rollout/architecture documentation and this plan.

- [x] Exercise owner enable → open till → blocked disable → close/review → disable with real or mocked API contract.
- [x] Inspect desktop/mobile cards, keyboard focus, text wrapping and unavailable/confirmation/error states; retain screenshots.
- [x] Verify migration model consistency, focused backend/frontend regressions and production build.
- [x] Independent review of authorization, locking, backward compatibility and UI scope; address findings.
- [x] Record exact test results and rollout requirements; leave application databases and deployed configuration untouched.

## Verification result

Implemented in the existing current PR checkout, baseline `ca9d5585`; no application database migration, deployment or live enablement was performed.

- Backend solution build: 0 warnings/errors. EF model matches `20261109171054_AddOwnerTillSettings`.
- All backend unit suites passed: Domain468, Application1,802, API588.
- Expanded till/cancellation PostgreSQL regressions202 passed; dedicated settings/API/race/upgrade9, schema/RLS8 and offline-arrival transition1 also passed (some dedicated checks overlap the broad regression filter).
- Final full frontend:296 files /2,936 tests plus59 script checks passed.
- Production web/worker/server build passed with existing bundle/style budget warnings.
- Six release browser scenarios passed, including desktop/mobile, Tamil, axe checks, unavailable deployment, manager read-only state, disable blockers, and exact retry after lost response/early GET.
- Independent review's two findings were reproduced red and fixed green: offline evidence invalidated while selection persistence waits, and unresolved legacy pre-envelope holds missing from disable blockers. Scoped re-review cleared both.
- Synthetic screenshots were inspected in the task visualization output under `till-settings/`; no customer data was used.

Implementation details: frontend uses a dedicated `ShopTillSettingsService` rather than expanding general `ShopService`, and a small cross-tab `TillSettingsEvents` service to invalidate verified capability. The first Manage shop step groups this setup with GST and expiry policies. An arriving offline envelope after disable's readiness check is retained, then held for TillReview without posting money or changing any close. The owner acknowledgement covers browser-local queues that no server can enumerate.
