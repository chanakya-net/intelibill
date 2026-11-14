# P01-04 shared web browser scenarios

Run from an installed workspace using Bun 1.3.11, supported Node 24 or 25, and the Chromium binary pinned by the merchant's Playwright version.

| Scenario | Command | Expected observation |
| --- | --- | --- |
| Clean dependency restore | `bun install --frozen-lockfile` at the repository root, then `cd src/frontend && bun install --frozen-lockfile` | The same workspace lockfile supports both directories. |
| Consumer dependency boundary | `cd src/frontend && bun run test --run scripts/tests/web-workspace.spec.ts` | Both applications and the shared packages resolve the same physical Angular core. The shared peers resolve consistently. Admin and shared sources have no merchant or POS dependencies. |
| App-local builds | `cd src/frontend && bun run build` and `cd src/admin-frontend && bun run build` from the root | Separate browser output for INVENTORY and ADMIN. The merchant build publishes its protected worker and full and lite offline manifests. |
| App-local regression suites | `bun run test --watch=false` in each app directory | Merchant and admin component tests run. The admin test supplies the empty, unauthenticated session and verifies denial without reading browser storage. |
| Merchant style ceiling | `cd src/frontend && bun run audit:styles:duplicates` | At most 27 repeated blocks. The audit resolves token values from the shared stylesheet. |
| Merchant production Chromium | `cd src/frontend && bun run build && bun run audit:ui:release` | Runs the merchant scenarios below, including the protected-cache and offline-pack release suites. Needs no admin build output. |
| Admin production Chromium | `cd src/admin-frontend && bun run audit:release` | Builds the admin app, then runs the admin access scenario below from `src/admin-frontend/tests/release`. |
| Merchant image | `docker build -f src/frontend/Dockerfile .` at the repository root | Frozen workspace install and merchant build succeed. The existing runtime entry point is still present. |

## Production Chromium scenarios

- **Admin access** (admin suite). Start at the merchant route `/inventory` on the separate admin origin. The landing shows “No provider session” and “No provider permissions.” Cancel “Hide access help?” to keep the help visible. Confirm to hide the help while retaining the access denial. Show the help again. The page uses the shared form-section and error styles and PrimeNG confirmation component. No API, hub, or service-worker requests originate from the admin shell.
- **Merchant login** (merchant suite). Open `/login` with an empty browser session and phone sign-in disabled in the fixture. Observe the 416px auth card, IBM Plex Sans body font, and `#f97316` brand token. The auth kit loads. The shell kit and its overlay styles are absent from the page and its requested application JavaScript.
- **Merchant Owner** (merchant suite). Open inventory with a non-production shop Owner session. The existing Add New Product editor has a grid field section with its sand border. A fixture rejection displays the red error banner. A successful retry opens the barcode confirmation. Rejecting it leaves the barcode print dialog closed. The shared confirmation component preserves the merchant's own wording and callbacks.
- **Merchant worker** (merchant suite). The production build has the protected wrapper, Angular worker, matching full and lite build IDs, and eight prefetched local fonts in the full pack. The worker release tests exercise production Chromium with separate origins and non-production session data, including tenant cache isolation and offline enablement, update and recovery.

## Behavior-first evidence

Before implementation, the workspace check failed because the root manifest was missing. The style-audit test failed with zero duplicates where shared-token normalization should yield one. The admin browser check failed because there was no built admin entry point. The same merchant login and inventory observations passed against the original merchant build.

This slice exercises browser UI, dependency resolution and existing worker behavior with test fixtures. PostgreSQL isolation, server session races and fixed-clock server boundaries do not apply. The admin session fixture grants zero capabilities and makes no licensing API calls. The tests use no live provider.
