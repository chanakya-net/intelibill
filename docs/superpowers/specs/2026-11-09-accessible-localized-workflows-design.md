# Accessible and localized web workflows

## Scope and source baseline

Implement the supplied backlog 35 scope on `37b2422e` (2026-11-09): keyboard and assistive-technology semantics for checkout, customer account and purchase receiving; safe localized API feedback; locale-aware display; nine-language layout and print verification. Angular web only. No backend, schema, money arithmetic, canonical date, document snapshot, financial recovery or mobile changes.

The supplied outline was verified against October code. Current-source differences matter:

- `CartTableComponent` already uses `QuantityFieldComponent`, which validates precision and commits on blur/Enter. Goods increment/decrement names include the product, but omit lot context; remove, service price and advanced editing controls still need names. Preserve `clientLineKey` and quantity parsing.
- Payment method, paid/due and applied-credit inputs lack complete associations on PrimeNG's actual inner controls. Setting ARIA only on the Angular host is insufficient.
- Checkout has durable `CheckoutIntentService` recovery. Receiving has persisted row identities, field errors, guarded `hide()` and unknown-outcome recovery. Till and expiry evidence are now included. Accessibility changes must not bypass these owners.
- Sales, expenses and customers effects still fall back to raw `detail`/`message`. PO effects already return translation keys but primarily inspect `title`; receiving has its own stable row mapper. Retain useful existing invoice/credit/quantity/PO translations.
- `LocalizationService` loads and persists all nine languages but does not update document `lang`. `DateOnlyPipe` protects date-only parsing but uses static Angular `LOCALE_ID`; new formatting must react to selected language without changing its canonical input.
- A4/thermal invoices already use issued snapshots and translated labels. Improve display and missing accessible names without replacing legal/frozen facts with live shop/customer values.
- Release tests serve the built app with `startPurchaseReceivingServer`, real IndexedDB, API fixtures and service workers. Reuse this fixture and extend routes rather than create a parallel application harness.

The coordinator's baseline passed: 286 frontend spec files / 2,813 tests and 59 Bun script tests (`bun run test --watch=false`, 2026-11-09). This is baseline evidence, not evidence for the planned change.

## Semantic and interaction contract

Every touched input has a visible translated label linked to the actual native input by a component-instance/field ID. PrimeNG components use their supported `inputId`, `ariaLabel`, `ariaLabelledBy`, `ariaDescribedBy` or pass-through input attributes as appropriate to the installed version. Rendered tests assert the native control's accessible name, descriptions and invalid state. IDs remain unique when two component instances or repeated lots are rendered.

Cart names include action, product/service, lot (goods), and a displayed line ordinal. The ordinal disambiguates identical product/lot text; identity and emitted operations still use `clientLineKey`. Give remove, quantity, service-price and advanced controls meaningful names in grouped and ungrouped branches. Decorative icons are hidden. Focus indicators remain visible; touched targets are at least 24 CSS pixels, with 44 pixels preferred where layout permits.

Keyboard search and scanner Enter perform only their search/selection intent; they never bubble into a sale submission. Modals use PrimeNG's existing focus trap, named headers, predictable initial focus and restoration to a still-connected initiating element. Do not add a second trap. If the opener was removed, focus a stable page heading or search control. Escape follows the same guarded close path as the visible close action. A receiving flush may refuse closure; an in-flight financial write cannot be presented as cancelled. An unknown outcome can retain the existing safe close/reopen behavior because its durable intent survives; it must remain explicit and recoverable. Printing preserves the existing same-origin opener/session and popup fallback behavior.

Use one concise polite status for a settled total/committed cart change and one error summary per failed action. Do not announce individual scanner characters or every numeric keypress, duplicate the same message through nested live regions, or move focus on background retries/language changes. On explicit invalid submit, mark fields and focus the first invalid control or the summary linked to it. Keep errors associated with paid/due, credit-note and receiving-row controls.

## Localized errors and formatting

`ApiErrorMessageService.resolve(problem: unknown, context: ApiErrorContext): LocalizedError` accepts an HTTP error wrapper or problem body. `ApiErrorContext` contains `fallbackKey`, optional trusted `codeMap`, and optional trusted `fieldMap`; `LocalizedError` contains `messageKey`, `params`, `fieldErrors`, optional `correlationId`. A field error contains `field`, `messageKey`, and `params`.

Prefer a nonempty stable `code`; accept legacy `title` only when it exactly matches a known code. Unknown codes use the caller's trusted fallback. Never translate or display arbitrary server title/detail/message/description or accept them as translation keys/HTML. Normalize both dictionary validation errors and `SafeErrorFactory`'s `{code,description}[]`; its `code` may be a field path. Map only recognized fields or existing receiving row paths, ignore their descriptions, and use translated generic invalid-field copy when no more specific stable code exists. Trace references must be bounded safe identifiers, shown as text with a translated support-reference label. No server body is logged.

Keep error keys/parameters in state so existing visible errors retranslate when language changes. Add optional rich metadata to relevant failure actions/state without changing existing `errorMessage` key contracts. Clear it with the same request/success/reset lifecycle. Expose fields/support reference at the owning form/page; receiving retains its persisted row-error and recovery classification logic. Presentation never decides whether an operation committed.

`LocaleFormattingService` reads the selected language and provides `formatMoney(value: number | null | undefined): string`, `formatBusinessDate(value: string | null | undefined): string`, and `formatInstant(value: string | null | undefined, timeZone = 'Asia/Kolkata'): string`. INR uses `Intl.NumberFormat` with two fractional digits. Strictly validate `YYYY-MM-DD`, construct UTC calendar components and format with UTC to preserve that day in every host time zone; do not reinterpret it as an instant. Format actual timestamps separately. Missing/invalid values display an em dash, never zero or an invented date. Angular template adapters must rerender on language changes. Existing quantity input parsers and outbound numeric/ISO values remain unchanged.

Update document `lang` only after a supported language loads successfully; retain `en-IN` fallback and saved preference. Supply every new label/message and matching interpolation placeholders in `en-IN`, `hi-IN`, `ta-IN`, `te-IN`, `bn-IN`, `ml-IN`, `kn-IN`, `mr-IN`, `gu-IN` bundles. Do not weaken coverage tests or fill regional bundles with English copies.

## Acceptance and limits

Test the actual built checkout, customer account, receiving, offline review and A4/thermal print views across the nine locales, with long names, desktop, 320 CSS-pixel layout, and 200% zoom/reflow. Horizontal scrolling is acceptable within an explicitly named data-table region; essential actions, error text and dialog controls must remain reachable. Explicitly distinguish CSS reflow/zoom emulation from a manual browser zoom test.

Use pinned `@axe-core/playwright` for scoped WCAG 2.2 A/AA scans; no serious/critical violations in the exercised journeys, and fix scoped actionable issues found at lower severity. Assert keyboard focus and recovery behavior separately because axe does not prove them. Record screenshots and exact browser/locale/journey coverage in the review evidence. Automated DOM/keyboard tests are not a screen-reader listening session or native-speaker review: those remain named release validation gaps unless actually performed. No claim of universal WCAG conformance.

No migration or enablement is required. Rollback is a frontend revert retaining canonical facts and saved intents. No release may weaken protected API caching or offline/financial ownership guards.
