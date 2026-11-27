# Shared web workspace packages

Two private Bun workspace packages consumed by both Angular apps: the merchant web (`src/frontend/intelibill`) and the admin web (`src/frontend/licensing`). They are source-only: no build step, no published artefact, no scripts of their own.

| Package | Path | Contents |
|---|---|---|
| `@intelibill/api` | `api/` | API contract types and small pure helpers shared by both apps |
| `@intelibill/ui` | `ui/` | PrimeNG theme preset, two standalone Angular components, global stylesheets, self-hosted fonts |

## Exports

`@intelibill/api` (`api/src/index.ts`, the only entry point):

- `ProblemDetails`, `problemCode(body)` — the API error body shape and its machine code (`code`, falling back to `title`).
- `SESSION_PORT`, `SessionPort`, `SessionState` — injection token and interface for an app's session (state and capabilities as signals).
- `EntitlementSynchronization` — type for the entitlement synchronisation facts of `subscription-status-v1`.
- `RefundOutcome`, `validRefundOutcome(row)` — refund outcome type and its consistency check.

`@intelibill/ui` (`ui/package.json` `exports`):

| Import | File | Provides |
|---|---|---|
| `@intelibill/ui` | `ui/src/index.ts` | `FormSectionStylesComponent` (`ib-form-section-styles`) |
| `@intelibill/ui/theme` | `ui/src/theme.ts` | `INTELIBILL_PRIME_PRESET`, `INTELIBILL_PRIME_CSS_LAYER` |
| `@intelibill/ui/confirmation-dialog` | `ui/src/confirmation-dialog.component.ts` | `ConfirmationDialogComponent` (`ib-confirmation-dialog`) |
| `@intelibill/ui/styles/*` | `ui/styles/*` | `fonts.css`, `tokens.css`, `base.css`, `ui-kit-layer-order.css`, `ib-page-heading.css`, `ib-panel.css`, `ib-form-section.css` |

`ui/fonts/` (Manrope and IBM Plex Sans WOFF2 files plus their `LICENSE`) is not a package export; each app copies it as a build asset.

## How the apps resolve them

- The root `package.json` lists `src/frontend/shared/*` as workspaces; both apps depend on the packages as `workspace:*`, and the single root `bun.lock` records them. `bun install` (run at the repository root or in an app) links them into each app's `node_modules/@intelibill/`.
- TypeScript imports go through `paths` in each app's `tsconfig.json`, which map the four TypeScript entry points above straight to the source files, so the app's own Angular build and test builder compile them.
- CSS `@import '@intelibill/ui/styles/…'` resolves through the package `exports`.
- Fonts are copied by an `assets` entry in each app's `angular.json` (`../shared/ui/fonts` to `fonts/`).
- Both app Dockerfiles copy the two `package.json` files before `bun install --frozen-lockfile` and then the whole `src/frontend/shared/` tree.

## Build and test

The packages have no `scripts`, no tests and no `tsconfig` of their own. They are verified only through the consuming apps (Bun `1.3.11`, never `npm` or `yarn`):

```bash
(cd src/frontend/intelibill && bun run build && bun run test --watch=false)
(cd src/frontend/licensing && bun run build && bun run test --watch=false)
```

The merchant full test run also executes `src/frontend/intelibill/scripts/tests/web-workspace.spec.ts`, which enforces the dependency rules for these packages. To run it alone: `(cd src/frontend/intelibill && bun run test --run scripts/tests/web-workspace.spec.ts)`.

## Rules for changing them

- A change here changes both apps: build and test both before merging. The merchant release workflow (`.github/workflows/deploy.yml`) is triggered by `src/frontend/shared/**`.
- Add a new TypeScript entry point in three places: the package `exports`, and `paths` in both apps' `tsconfig.json`.
- Shared code must not import from either app or from merchant-only libraries; dependencies are declared as `peerDependencies` only. See the architecture document for the full list.
- After touching `ui/styles/`, run `(cd src/frontend/intelibill && bun run audit:styles:duplicates)`; the audit reads `ui/styles/tokens.css`.
- Keep `ui/fonts/LICENSE` in step with the font files.

## Documentation

- [Architecture](architecture_doc/architecture.md) — responsibilities, public surface, dependency rules, style and font delivery.
- [Merchant web architecture](../intelibill/architecture_doc/architecture.md) — how the merchant app delivers the UI kit (§ Shared Styles).
