# Shared Web Packages Architecture

> Scope: `src/frontend/shared/` only (`@intelibill/api`, `@intelibill/ui`). For commands see the [README](../README.md); for how each app uses these packages, see that app's own documentation.

## Purpose

The merchant web (`src/frontend/intelibill`) and the admin web (`src/frontend/licensing`) are separate Angular applications. These two packages hold the small amount of code and styling both must agree on: the API error contract, the session port, the PrimeNG theme, the design tokens, the fonts and a few UI kit style families. Everything else stays in the app that owns it.

Both packages are `private`, version `0.0.0`, and export TypeScript and CSS source directly. Nothing is compiled or bundled here; each app compiles the source as part of its own build.

## `@intelibill/api`

One entry point, `api/src/index.ts`. Every module is a type or a pure function with no HTTP, no state and no app imports; the only runtime dependency is `@angular/core` (for `InjectionToken` and `Signal`).

| Module | Exports | Role |
|---|---|---|
| `problem-details.ts` | `ProblemDetails`, `problemCode` | Shape of an API error body (`type`, `title`, `status`, `code`, `detail`, `traceId`, `errors`). `problemCode` returns `code`, else `title`, else `null`, and tolerates any input. |
| `session-port.ts` | `SESSION_PORT`, `SessionPort`, `SessionState` | Port for an app session: `state` (`'unauthenticated' \| 'authenticated'`) and `capabilities` as signals. The package defines the token only; an app provides the implementation (the admin app binds it in `app.config.ts`). |
| `entitlement-synchronization.ts` | `EntitlementSynchronization` | Read-only type for the entitlement synchronisation facts of `subscription-status-v1` (state, delivery, saved and applied account/shop revisions). |
| `refund-outcome.ts` | `RefundOutcome`, `validRefundOutcome` | Refund outcome type and a guard that checks an untyped row's `providerState`, `status`, `accessDisposition` and amounts are mutually consistent. |

There are no HTTP clients, services or generated API clients in this package. Each app owns its own `HttpClient` services, interceptors and feature models, and imports only these contract pieces.

## `@intelibill/ui`

### TypeScript surface

| Entry point | Export | Role |
|---|---|---|
| `@intelibill/ui/theme` | `INTELIBILL_PRIME_PRESET` | PrimeNG preset: Aura with the orange primary scale (`definePreset` from `@primeuix/themes`). |
| `@intelibill/ui/theme` | `INTELIBILL_PRIME_CSS_LAYER` | PrimeNG `cssLayer` option: layer `primeng`, order `theme, base, primeng`. Both apps pass both constants to PrimeNG in `app.config.ts`. |
| `@intelibill/ui/confirmation-dialog` | `ConfirmationDialogComponent` | `ib-confirmation-dialog`: a `p-confirmDialog` appended to `body`. The calling page owns its `ConfirmationService`, message and callbacks. |
| `@intelibill/ui` | `FormSectionStylesComponent` | `ib-form-section-styles`: renders nothing; with `ViewEncapsulation.None` it adds the layer order and the page heading, panel and form section families to the document. |

### Stylesheets (`ui/styles/`, exported as `@intelibill/ui/styles/*`)

| File | Contents |
|---|---|
| `fonts.css` | `@font-face` rules for Manrope (400-800) and IBM Plex Sans (400-600), each `src: url('/fonts/<file>.woff2')`. |
| `tokens.css` | `--app-*` custom properties on `:root`: colour scales (orange, sand/brown/bronze, stone, slate, red, amber), radii, shadows, font families. `color-scheme: light`. |
| `base.css` | `box-sizing`, `html`/`body` defaults, and the `h1`-`h6`/`p` resets inside `@layer ib-base`. |
| `ui-kit-layer-order.css` | The single statement `@layer primeng, ib-base, ib-kit;`. |
| `ib-page-heading.css`, `ib-panel.css`, `ib-form-section.css` | UI kit families. All rules sit in `@layer ib-kit` and use the `ib-` class prefix. |

Cascade contract (documented in `ui-kit-layer-order.css`): `theme, base, primeng, properties, components, utilities, ib-base, ib-kit`, then unlayered styles. A kit rule therefore beats PrimeNG, Tailwind utilities and the element resets, and loses to any unlayered component style. The contract is pinned by a test in the merchant app (`src/frontend/intelibill/tests/ui-audit/mock/ui-kit-cascade.spec.ts`).

### How the apps consume styles

- **Global sheets.** Each app's `src/styles.css` imports, in this order: `fonts.css`, `tailwindcss`, `primeicons`, `tokens.css`, `base.css`. These ship with the first load. An app may add its own tokens after them (the admin app does).
- **Kit families.** Two delivery routes exist for the same files:
  - the admin app renders `<ib-form-section-styles />` in its root component;
  - the merchant app `@import`s each shared family from a file of the same name in its own `shared/styles/ui-kit/` and delivers them with its own styles component, alongside families that exist only in the merchant app.
- The kit families reference tokens (`var(--app-…)`), so `tokens.css` must be loaded globally by any app that uses them.

### Fonts

`ui/fonts/` holds eight WOFF2 files and a `LICENSE` (both families are SIL OFL 1.1). The folder is not exported by the package. Each app's `angular.json` copies `../shared/ui/fonts` to the `fonts/` output folder, which is what makes the root-absolute `/fonts/…` URLs in `fonts.css` resolve. Both apps set `optimization.fonts` to `false`. Adding or renaming a font file means updating `fonts.css` and `LICENSE` together.

## Resolution

| Kind | Mechanism |
|---|---|
| Package linking | Root `package.json` workspaces (`src/frontend/shared/*`), `workspace:*` dependencies in both apps, one root `bun.lock`. |
| TypeScript | `compilerOptions.paths` in each app's `tsconfig.json` maps `@intelibill/api`, `@intelibill/ui`, `@intelibill/ui/theme` and `@intelibill/ui/confirmation-dialog` to the source files. |
| CSS | `@import '@intelibill/ui/styles/<file>.css'`, resolved through the package `exports` map. |
| Fonts | `angular.json` asset copy in each app. |

The `exports` map and the two `paths` blocks are three copies of the same list of TypeScript entry points and must stay identical.

## Dependency rules

- **Direction.** Apps depend on shared; shared depends on neither app. `@intelibill/api` and `@intelibill/ui` do not import each other.
- **Peer dependencies only.** `@intelibill/api` declares `@angular/core`; `@intelibill/ui` declares `@angular/core`, `@primeuix/themes`, `primeng` and `tslib`. Neither has `dependencies` or `devDependencies`; the versions come from the apps.
- **Enforced by** `src/frontend/intelibill/scripts/tests/web-workspace.spec.ts` (run with `bun test`, as part of the merchant app's full test run):
  - both apps and both shared packages resolve the same single installed `@angular/core`, at the exact version the test names;
  - every shared peer range is satisfied by the installed version, and both apps resolve that same installation;
  - no shared manifest dependency or import specifier matches `inventory`, `@angular/service-worker`, `@ngrx/*`, `@microsoft/signalr` or `@zxing/*`;
  - no import in `src/**/*.ts` of a shared package resolves into `src/frontend/intelibill` or `src/frontend/licensing`.

## What must not go into shared

- Anything used by only one app. The merchant-only kit families and the admin-only tokens live in their apps for this reason.
- App state, routing, HTTP services, interceptors or guards. `@intelibill/api` carries contracts; the session is a port, not an implementation.
- Merchant runtime concerns: NgRx, the service worker, SignalR, barcode scanning (rejected by the test above).
- Translations. The packages contain no i18n keys or user-facing copy.
- Unlayered kit rules, or kit classes without the `ib-` prefix: either breaks the cascade contract for both apps.
- Token changes that alter a value in place without checking both apps; `tokens.css` states that each token is the exact literal it replaced.

## Verifying a change

There is nothing to build or test inside `src/frontend/shared/`. A change is verified by building and testing both consumers:

1. `bun run build` and `bun run test --watch=false` in `src/frontend/intelibill` (the full run includes `web-workspace.spec.ts`).
2. `bun run build` and `bun run test --watch=false` in `src/frontend/licensing`.
3. For `ui/styles/` changes, `bun run audit:styles:duplicates` in `src/frontend/intelibill` (it resolves `var(--app-…)` against `ui/styles/tokens.css`).
4. For cascade or layer changes, the merchant UI audit spec named above.

CI: `.github/workflows/deploy.yml` lists `src/frontend/shared/**` among its trigger paths and builds and tests both web apps.

## References

- [README](../README.md)
- [Merchant web architecture](../../intelibill/architecture_doc/architecture.md) — § Shared Styles describes the merchant UI kit delivery.
