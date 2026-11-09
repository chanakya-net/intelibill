# Accessible and localized web workflows: browser evidence

Validation date: 9 November 2026. Scope: shipped Angular web application only; no backend, schema, mobile, deployment or shop enablement.

The release test serves the production build through `startPurchaseReceivingServer`, retains the built protected service worker, supplies API fixtures and uses real browser IndexedDB. Fixture POST counts establish browser interaction/recovery behavior, not backend financial posting correctness.

## Coverage

Headless Chromium **151.0.7922.34**, Playwright **1.62.0**, pinned `@axe-core/playwright` **4.11.1**. Browser timezone: Asia/Kolkata; browser locale: en-IN. Each supported application language is independently loaded from its saved preference and verified through the document `lang` and translated controls: en-IN, hi-IN, ta-IN, te-IN, bn-IN, ml-IN, kn-IN, mr-IN and gu-IN.

Each language exercises product search and its localized empty state, lot picker, committed cart quantity, customer selection, customer account, purchase receiving, offline review with a held server invoice, issued A4 and thermal receipts. The viewport matrix is 1280×900, 320×900 and 640×900 CSS pixels. The 640px case is the CSS reflow equivalent of a 1280px window at 200% browser zoom; it does **not** claim manual browser zoom, device magnification or a native screen-reader session.

Axe scans the visible production journey/dialog roots using `wcag2a`, `wcag2aa`, `wcag21a`, `wcag21aa` and `wcag22aa`. The assertion requires **zero violations**, including serious/critical, with no disabled rules or global exclusions. Explicit assertions separately check document overflow, clipped action/error boxes, translated accessible names, initial/return focus, native error descriptions, live statuses and keyboard-scrollable named table regions.

Keyboard cases cover scanner Enter without a sale POST, quantity/customer selection, paid/due values, one in-flight checkout, success focus and the actual issued print popup, Done focus on Sales History, same-key unknown checkout recovery after reload, guarded account Escape during a payment, field feedback without raw server copy, unknown payment reopen/retry, receiving rejection/unknown reopen/retry, and independent controls for identical product/lot text. Existing release receiving cases additionally cover failed storage flush refusing header/Escape close and lost-response recovery after reload.

## Demonstrated fixes

- Small orange checkout/confirmation/lot-price text and primary actions failed contrast; the scoped colors now meet the scanned thresholds. Account avatar, offline review labels and A4 partial-payment status were also corrected.
- The customer account title was hidden by CSS, preventing useful initial focus; it is visible and focusable, with a translated Close name. Receiving likewise focuses its named title.
- Disabling/removing a focused financial action could drop focus to the document body. The existing dialog focus helper rehomes only that lost in-dialog focus, preserving intentional outside focus and the existing PrimeNG trap. Closing with a detached opener prioritizes the page fallback, and explicit Done focuses the destination heading.
- Receiving native controls now have explicit row-specific IDs/labels and linked error IDs; invalid number/amount state is passed to PrimeNG's native input. Repeated row errors have distinct IDs.
- Customer statement and cart/offline table containers are named, keyboard-focusable scroll regions. Financial identities and operations remain unchanged.
- Long translated credit-note verification and offline support-export actions wrap at 320px. Thermal screen preview padding fits 80mm at 320px. The A4 screen header/padding reflows and its named items table scrolls locally, avoiding one-character seller names; print paper rules remain unchanged.
- Product autocomplete empty feedback is translated in all nine bundles. Cart expiry dates use strict locale-aware business-date display; the stored ISO date and quantity/payment parsing are unchanged.
- A rapid scanner/edit followed by Enter could search the previous parent input value. Enter now reads the current native input while retaining suggestion selection consumption, IME handling and form-default prevention. Its focused regression failed before the fix and passed afterward.

## Post-review regressions

Independent review found that the mobile A4 minimum item-table width also applied to the unwrapped tax-summary table. A balanced regular-tax v2 fixture (CGST ₹9 + SGST ₹9, invoice ₹118, paid ₹68/due ₹50) reproduced 376px document overflow. The minimum width now applies only to the named item-table wrapper. The taxed 320px browser case passes and its screenshot was inspected.

Native receiving probes also confirmed that the selected PO-line combobox was named only “Widget”, and line/date API field errors lacked some native invalid/description attributes. The expanded browser regression failed six native assertions before the fix. A stable visible-label ID now names the actual combobox; supported Select label and DatePicker InputText pass-through attributes expose linked invalid feedback. Repeated row IDs remain distinct, and the test verifies feedback clears before unknown-outcome recovery. The receiving and taxed-print regression cases pass 2/2; full units/build pass again.

A transient unknown outcome can coexist briefly with a separate saving/checking status. The recovery test now targets the actual unknown message and waits for the existing retry control to become enabled before Escape. It passed five repeated focused runs without reducing its exact-key/payload/focus assertions. The final complete release run is pending.

## Commands and results

Commands run from `src/frontend`; the final full release gate is still running:

| Command | Result |
| --- | --- |
| `bun run test --watch=false` | Exit 0: 292 files / 2,887 Angular tests; 59 Bun script tests |
| `bun run build` | Exit 0; production budgets and worker finalization retained |
| `bun x playwright test --config tests/ui-audit/release/playwright.release.config.ts accessibility-localization.spec.ts` | Exit 0: 13 cases, all nine locales; 194 scoped axe scans per accessibility run, zero violations (3.9 min) |
| `bun run audit:ui:release` | First full run: 56/57; stale sending-close expectation corrected, affected receiving/keyboard cases 12/12 green; later native/status and taxed-print regressions fixed with focused checks; final 58-case rerun pending |

The first full release run exposed a stale receiving test that expected Cancel to hide an actively sending dialog. The approved guard correctly refused it. The test now asserts disabled Cancel/Escape refusal, then uses real SPA browser Back to detach the page while the root effect survives. Its original late response still cannot close or overwrite the distinct PO draft. All 12 affected receiving/keyboard cases pass; the complete rerun is pending.

The build retains the repository's warning thresholds. The initial bundle is 743.63kB (650kB warning threshold); existing component-style warning categories remain. No budgets, warning limits, service-worker behavior or financial guards were relaxed.

## Screenshots and limits

Representative Tamil screenshots are stored in [the evidence directory](2026-11-09-accessible-localized-workflows-evidence/). They show long mixed-script names, desktop and 320px POS/account/receiving/offline review, and 320px A4/thermal issued receipts; an additional English taxed A4 screenshot covers the tax-summary regression. Screenshots are inspected visually as supporting evidence; assertions and keyboard actions establish the tested behavior. Named data tables deliberately retain local horizontal scrolling.

No screen-reader listening session, native-speaker translation approval, manual browser zoom, physical printer/paper measurement, popup-blocker/offline-print fallback browser scenario, or cross-browser testing was performed in this task. The real allowed popup path is tested; existing offline fallback unit/release guards remain. Automated scoped results are not universal WCAG conformance. No production enablement or deployment occurred.
