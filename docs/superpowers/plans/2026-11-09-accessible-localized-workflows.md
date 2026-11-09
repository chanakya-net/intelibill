# Accessible and localized web workflows Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make core retail web journeys keyboard operable, meaningfully named and localized without changing transaction facts or recovery.

**Architecture:** Repair existing standalone components and PrimeNG control semantics. Centralize safe code-to-key resolution and display-only Intl formatting; retain NgRx keys and existing durable operation owners. Verify the built application using existing release fixtures.

**Tech Stack:** Angular 21, PrimeNG 21, NgRx, Transloco, Bun, Angular/Vitest test runner, Playwright and axe.

**Spec:** `docs/superpowers/specs/2026-11-09-accessible-localized-workflows-design.md`.

## Global Constraints

- Angular web only; no backend/schema/mobile, canonical money/date, document snapshot or financial recovery changes.
- Work serially on the existing `codex/accessible-localized-workflows` branch; no new worktree/branch. User authorized plan → Sol implementation → thorough review → PR.
- Use Bun only. Commands below run in `src/frontend` unless stated otherwise. `bun run test --run <path>` invokes the project's Angular test runner, not raw `bun test` for component specs.
- All nine bundles: `src/frontend/public/assets/i18n/{en-IN,hi-IN,ta-IN,te-IN,bn-IN,ml-IN,kn-IN,mr-IN,gu-IN}.json`. Each task owns its new keys in all nine files; no placeholder English copies or weakened parity tests.
- Preserve clientLineKey, quantity scale/parser, frozen operation keys/payloads, shop epochs, till/expiry evidence, same-origin print opener, offline guards and protected worker behavior.
- No claim of screen-reader listening, native-speaker signoff or full WCAG conformance from automated results.

## Review Focus

- Identical product and lot labels across repeated lines/two component instances still have distinct accessible names and IDs (Task 2).
- Delayed POST, lost response, unknown lookup404, reload and scope change retain the same intent; Escape/focus cannot imply cancellation or allow a fresh financial key (Tasks 3–5).
- Malformed/hostile server errors, both validation shapes and human-readable titles never leak raw prose; known fields and safe trace references remain useful (Tasks 1 and 4).
- Leap days, impossible dates, negative/zero money and language switches preserve canonical values across time zones (Tasks 1, 4 and 5).
- Regional scripts and long names at 320px and 200% reflow leave actions/errors visible; detached dialog openers have a reachable focus fallback (Tasks 3 and 5).

---

### Task 1: Safe error and locale display contracts

**Files:** Create `src/frontend/src/app/core/i18n/api-error-message.service.ts`, `locale-formatting.service.ts`, and colocated `.spec.ts`; modify `localization.service.ts`, create `localization.service.spec.ts`; create `src/frontend/src/app/shared/pipes/locale-money.pipe.ts`, `locale-business-date.pipe.ts`, `locale-instant.pipe.ts` and their specs. Update `core/i18n/i18n-coverage.spec.ts` only to add requirements, and all nine bundles.

**Interfaces:**

```ts
export interface LocalizedFieldError { readonly field: string; readonly messageKey: string; readonly params: Readonly<Record<string, string | number>>; }
export interface LocalizedError { readonly messageKey: string; readonly params: Readonly<Record<string, string | number>>; readonly fieldErrors: readonly LocalizedFieldError[]; readonly correlationId?: string; }
export interface ApiErrorContext { readonly fallbackKey: string; readonly codeMap?: Readonly<Record<string, string>>; readonly fieldMap?: Readonly<Record<string, string>>; }
// ApiErrorMessageService:
resolve(problem: unknown, context: ApiErrorContext): LocalizedError;
// LocaleFormattingService:
formatMoney(value: number | null | undefined): string;
formatBusinessDate(value: string | null | undefined): string;
formatInstant(value: string | null | undefined, timeZone?: string): string;
```

Pipes are named `localeMoney`, `localeBusinessDate`, `localeInstant`; they delegate to the service and refresh when language changes. They are presentation only. `fieldMap` maps recognized server field paths to local form names; explicit known code maps override general mappings. Do not create a dependency from core into feature modules: feature callers pass their existing maps.

- [ ] Write failing service tests: code beats title; allowlisted legacy title works; unknown HTML/detail/description is absent; null/nonobject bodies safe; both dictionary and `{code,description}[]` field errors resolve only recognized fields; trace ID accepted only with `/^[A-Za-z0-9._:-]{1,128}$/`. Assert no invented reference when absent/unsafe.
- [ ] Write failing formatting/language tests: INR zero/negative/123456.78 equals Intl output for all nine locales; null/NaN/invalid date → em dash; `2024-02-29` keeps day, `2026-02-29` rejected; date-only stable in UTC/Asia/Kolkata/America/Los_Angeles; language update changes live pipe output and document lang after loading, failed load leaves prior lang. No input object mutation.
- [ ] Run `bun run test --run 'src/app/core/i18n/*.spec.ts'` and `bun run test --run 'src/app/shared/pipes/locale-*.spec.ts'`; confirm intended red tests.
- [ ] Implement contracts, trusted common codes (`Validation.Failed`, `Request.Invalid`, `Auth.Unauthorized`, `Concurrency.Conflict`, `Internal.Unexpected`, `Operation.PayloadMismatch`, posting-period and transport fallbacks) and localized generic field/reference copy. Preserve selected-language persistence and legacy `DateOnlyPipe` callers; adopt the new business-date pipe only in scoped views.
- [ ] Run the same focused tests green and commit `feat(web): add safe localized error and display services`.

### Task 2: Checkout semantic controls

**Files:** Modify TS/HTML/spec for `src/frontend/src/app/features/sales/components/new-sale/{sale-payment-section,cart-table,cart-checkout-summary,sale-customer-section,batch-search-bar,batch-picker-dialog}.component`; modify colocated SCSS where present; modify `src/frontend/src/app/shared/quantity/quantity-field.component.{ts,html,spec.ts}` only for additive disabled/description semantics; modify `src/frontend/src/app/features/sales/pages/new-sale-page.component.html` for input wiring; all nine bundles.

**Interfaces:** Consume Task 1 pipes. Keep all existing output event signatures and cart identity. Add optional payment inputs `paymentSplitError: string = ''`, `isSubmitting: boolean = false`; add optional quantity `disabled` input default false if needed. Component IDs are stable per instance, field and `clientLineKey`; cart readable context is product/service + goods lot + translated one-based line ordinal (never raw UUID).

- [ ] Add rendered-component tests `payment_controls_have_associated_names` and `duplicate_product_lots_have_distinct_control_names`: query native input/select/button roles, not host attributes. Render two payment instances, all cart branches including duplicate identical lot text and service rows. Assert paid/due IDs differ, labels target inputs, descriptions target existing errors, all remove/advance/price controls have distinct translated names, disabled state reaches native controls.
- [ ] Run `bun run test --run 'src/app/features/sales/components/new-sale/*.spec.ts'` red for new expectations.
- [ ] Wire PrimeNG `inputId`/ARIA through its installed APIs or native pass-through; label credit-note amounts, search/customer controls and advanced HSN/tax/discount fields. Pass split errors down from the page. Preserve fractional quantity parser and available/quarantine bounds. Add visible focus/target styling without hiding content or enlarging data tables beyond their scroll container.
- [ ] Rerun component and `src/app/shared/quantity/quantity-field.component.spec.ts` tests green; include language switch and unchanged emitted quantities. Commit `fix(web): name checkout controls and validation fields`.

### Task 3: Keyboard focus, feedback and guarded overlays

**Files:** Modify `src/frontend/src/app/features/sales/pages/new-sale-page.component.{ts,html,scss}` and add `new-sale-page.component.spec.ts`; modify `features/sales/components/new-sale/{batch-search-bar,batch-picker-dialog,sale-confirmation-dialog,cart-checkout-summary}.component` TS/HTML/spec/SCSS; modify `src/frontend/src/app/shared/components/barcode-scanner-dialog.component.ts` and `.spec.ts`; modify `src/frontend/src/app/features/customers/pages/customers-page.component.{ts,html,scss,spec.ts}`; modify `src/frontend/src/app/features/purchase-orders/components/receive-purchase-order-dialog.component.{ts,spec.ts}` (inline template); create `src/frontend/src/app/shared/accessibility/dialog-focus.ts` and `.spec.ts` if focus restoration must be shared; all nine bundles.

**Interfaces:** Shared helper, if used: `captureFocus(document: Document): HTMLElement | null` and `restoreFocus(opener: HTMLElement | null, fallback: HTMLElement | null): void`, which focuses only connected, enabled elements. Existing financial services and `hide()`/account close owners remain authoritative; no change to durable intent records or failure classification.

- [ ] Add red tests `scanner_enter_only_searches`, `dialog_close_restores_opener_or_fallback`, `invalid_payment_has_one_summary`, `pending_receiving_escape_preserves_intent`, `account_pending_close_preserves_recovery`. Verify Enter prevents form submission without breaking autocomplete selection; invalid explicit submission focuses a referenced field/summary, async retries do not steal focus. Test safe unknown close/reopen separately from blocked in-flight close.
- [ ] Run relevant component specs using `bun run test --run <exact spec path>`.
- [ ] Use existing PrimeNG modal trap; establish translated header, initial focus and on-hide restoration. Route Escape through the same policy as close; receiving must await its existing draft flush and can refuse closure. Account dialog gets a real accessible name even during loading. Scanner initialization/failure gets concise translated status, and raw detector diagnostics become a safe localized fallback.
- [ ] Add restrained settled-total status and failure summary; existing unknown/recovery region remains authoritative, avoid duplicate announcement. Make print actions keyboard reachable and restore focus without changing popup/session handling. Do not change money/cart submission services to implement UI focus.
- [ ] Run focused specs plus existing `new-sale-page.cart-submit.service.spec.ts`, `new-sale-page.lifecycle.service.spec.ts`, `receiving-draft.service.spec.ts`, and `customers-page.component.spec.ts` green. Commit `fix(web): preserve focus and recovery in keyboard workflows`.

### Task 4: Apply localized feedback and display to scoped journeys

**Files:** Modify `src/frontend/src/app/features/{sales,expenses,customers,purchase-orders}/state/{sales,expenses,customers,purchase-orders}.effects.ts` and matching specs (matching feature basename, not cross product). Add optional rich error metadata through each feature's same-basename `actions.ts`, `reducer.ts`, `selectors.ts`, `facade.ts` and existing specs as needed. Modify `features/sales/utils/{invoice-error-keys,customer-credit-error-keys,sale-quantity-error}.ts` only to expose existing mappings without losing behavior. Modify `features/purchase-orders/services/receiving-errors.ts` and `.spec.ts` only for safe presentation reuse, never classification.

Also own TS/HTML/spec/SCSS for `features/sales/pages/{sales-page,new-sale-page}.component`, `features/expenses/pages/expenses-page.component`, `features/customers/pages/customers-page.component`, `features/purchase-orders/pages/{purchase-order-detail-page,purchase-order-builder-page,purchase-orders-list-page}.component` (some inline templates), `features/purchase-orders/components/{receive-purchase-order-dialog,purchase-order-receipt-history,purchase-order-print-view}.component`, `features/sales/components/{sale-invoice-a4,sale-invoice-thermal}.component`, `features/sales/pages/sale-invoice-print-page/sale-invoice-print-page.component`, and `features/sales/components/new-sale/{cart-table,cart-checkout-summary,sale-payment-section,sale-confirmation-dialog}.component`. Existing files only, plus new focused specs when absent. All nine bundles. Use Task 3's edited versions.

**Interfaces:** Consume Task 1 `resolve` and pipes. Existing `errorMessage` stays a translation key. Failure actions may carry `localizedError?: LocalizedError`; matching state retains it and clears with request/success/reset, selectors/facades expose it. Forms render `messageKey` with params and a translated plain-text reference; field errors attach only to known controls. No code checks translated text to decide recovery.

- [ ] Add effects tests for known new `code` and legacy title mappings, hostile unknown payload fallback, trace metadata preservation and language-switch retranslation. Add rendering tests showing translated fallback/reference and known field association. Preserve existing invoice/credit/quantity/PO specialized mappings, including receiving line identity; verify unavailable metadata does not hide generic errors.
- [ ] Run `bun run test --run 'src/app/features/*/state/*.effects.spec.ts'` red for changed behaviors.
- [ ] Replace raw server fallbacks in all four effects and scoped direct account/receiving/print failure paths. Preserve specific uncertainty keys and `classifyCheckoutSendFailure`/`classifyReceivingFailure`. Use shared mapping only for presentation. Remove obsolete map copies only after their tests are preserved.
- [ ] Use locale pipes for displayed amounts/business dates/instants in scoped views; keep input parsing, date-picker models and API payloads untouched. Audit translated A4/thermal headers, print controls, settlement labels and warnings; frozen document fields and numeric totals are byte-for-byte unchanged. Preserve actual print widths; screen wrappers may scroll intentionally.
- [ ] Run effects, customer/receiving, invoice A4/thermal/print specs and `bun run test --run src/app/core/i18n/i18n-coverage.spec.ts`. Assert payloads match canonical baseline after switching languages. Commit `feat(web): localize workflow feedback and printed display`.

### Task 5: Built-browser accessibility and layout proof

**Files:** Create `src/frontend/tests/ui-audit/release/accessibility-localization.spec.ts`; create `accessibility-localization-fixtures.ts` for API route fixtures if needed, reusing `startPurchaseReceivingServer`, `session`, `SHOP_A`, `USER` from `purchase-receiving-server.ts`. Modify that server only for additive fixture capabilities; preserve existing tests. Modify `src/frontend/package.json` and `src/frontend/bun.lock` together to pin the selected exact `@axe-core/playwright` dev version. Modify touched component SCSS and `src/frontend/src/styles.scss` only for demonstrated layout/focus issues. Create `docs/reviews/2026-11-09-accessible-localized-workflows.md` and screenshot evidence under its sibling `2026-11-09-accessible-localized-workflows-evidence/` directory.

**Interfaces:** Built Angular application, existing session/API fixture contracts, real IndexedDB; UI-only financial fixture counts are not backend posting evidence. Fixtures provide duplicate lots, long names, canonical money/date examples, unknown/delayed requests and offline-review data. Do not add a synthetic UI standing in for production components.

- [ ] Pin/install axe with Bun; write browser tests before fixing observed remaining layout failures. `keyboard_checkout_returns_focus`: keyboard search/select lot/customer, quantity, paid/due, submit once, success dialog, print/fallback, done; scanner Enter sends no sale POST. Assert pending/retry does not generate a new key. Test account and receiving initial/return focus, Escape policies, linked validation and an unknown outcome retained across reopen.
- [ ] `nine_locales_fit_core_journeys`: use `SUPPORTED_LANGUAGES`, load each via saved preference or real switch, assert document lang and translated labels on POS, account, receiving, offline review, A4 and thermal print. Exercise 1280px desktop, 320px viewport and a documented 200% reflow/zoom simulation. Assert no document-level horizontal overflow, no clipped error/action boxes, and intentional table regions remain keyboard scrollable. Do not use screenshots alone as assertions.
- [ ] Run axe on each scoped visible journey/dialog with WCAG 2.2 A/AA tags; require zero serious/critical violations and resolve scoped actionable findings. Add explicit assertions for names/focus/live regions and prevent global violation exclusions. Save representative regional-script/long-name screenshots and inspect them.
- [ ] Run `bun run test --watch=false`, `bun run build`, `bun x playwright test --config tests/ui-audit/release/playwright.release.config.ts accessibility-localization.spec.ts`, then `bun run audit:ui:release`. Each must exit 0; preserve production budgets and worker behavior. Rerun only affected checks after fixes. No backend tests are required absent backend changes.
- [ ] Write review evidence with exact commands/results, browser/version, locales, viewport/zoom method, screenshots and remaining manual screen-reader/native-speaker gaps. Do not mark those manual checks passed without actual evidence. Commit `test(web): verify accessible localized retail journeys`.

## Handoff and review

Dependencies are serial: 1 → 2 → 3 → 4 → 5. Each task is independently testable at its commit. Review the final branch for native PrimeNG semantics, malformed-error safety, locale reactivity, strict date validation, input/payload invariance, repeated identity, dynamic focus and financial intent preservation. Address findings before PR creation. Full baseline was green before work; report new test counts and any genuine limitations separately. User already authorized execution; no additional plan approval gate.
