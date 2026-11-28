# Admin Web Architecture

Scope: the licensing Admin Angular app in `src/frontend/licensing` (Angular project `ADMIN`, the
"Provider Console"). Other projects are referenced by link only:

- Shared web packages: [`../../shared/architecture_doc/architecture.md`](../../shared/architecture_doc/architecture.md)
- Licensing backend (the API this app calls): [`../../../backend/licensing/architecture_doc/architecture.md`](../../../backend/licensing/architecture_doc/architecture.md)
- Merchant web app: [`../../intelibill/architecture_doc/architecture.md`](../../intelibill/architecture_doc/architecture.md)

Setup, scripts and serving are in the [README](../README.md).

## Folder structure

```text
src/
├── index.html                 # <adm-root>, lang="en", <base href="/">
├── main.ts                    # bootstrapApplication(App, appConfig)
├── styles.css                 # global styles and admin design tokens
└── app/
    ├── app.ts / app.css       # root shell (console shell or auth shell)
    ├── app.config.ts          # providers and the whole route table
    ├── landing-page.component.*
    ├── core/auth/
    │   ├── admin-refresh-coordinator.ts          # cross-tab session coordination
    │   ├── admin-session.interceptor.ts          # the only HTTP interceptor
    │   └── admin-browser-coordination.testing.ts # test stubs for localStorage / Web Locks
    └── features/
        ├── form-page.css      # shared stylesheet for the auth form pages
        ├── auth/              # session service, guard, sign-in, bootstrap, mfa-enrollment/, recovery/
        ├── security/sessions/ # device sessions
        ├── admin-users/       # capabilities, invitations
        ├── audit/             # identity audit log
        ├── accounts/          # status, courtesy grants, trial extensions, device permits
        ├── restrictions/      # suspensions, reopening review
        ├── coverage-exceptions/
        ├── payments/          # manual payments, review/, refunds/ (intake)
        ├── refunds/           # refund approval
        ├── documents/         # billing documents
        └── catalogue/         # plans, trial policy, publication, retirement
```

Conventions visible in the code:

- Every component is standalone, `ChangeDetectionStrategy.OnPush`, selector prefix `adm-`.
- A feature folder holds page components (`*.component.ts/.html/.css`), one or more
  `*-api.service.ts` wrappers over `HttpClient`, optional `*-drafts.ts` persistence helpers, and
  `*.spec.ts` tests beside the code. Services are `providedIn: 'root'`.
- There are no feature route files; all routes are declared inline in `app.config.ts`.

## Bootstrap and providers

`app.config.ts` registers:

- `provideBrowserGlobalErrorListeners()`, `provideAnimationsAsync()`
- `provideHttpClient(withInterceptors([adminSessionInterceptor]))`
- `providePrimeNG` with `INTELIBILL_PRIME_PRESET` and `INTELIBILL_PRIME_CSS_LAYER` from
  `@intelibill/ui/theme`, ripple on, `darkModeSelector: false`
- `{ provide: SESSION_PORT, useExisting: AdminSessionService }`, binding the shared session port
  from `@intelibill/api` to this app's session service
- `provideRouter([...])`

## Routing

All routes are lazy `loadComponent` entries. `**` redirects to `''`.

| Path | Component | Guarded by `requireAdminSession` |
|---|---|---|
| `` | `LandingPageComponent` | yes |
| `security/sessions` | `SessionsPageComponent` | yes |
| `security/recovery` | `ReviewRecoveryPageComponent` | yes |
| `audit` | `AuditPageComponent` | yes |
| `accounts` | `AccountStatusPageComponent` | yes |
| `accounts/:accountId/coverage-exceptions`, `coverage-exceptions` | `CoverageExceptionPageComponent` | yes |
| `accounts/:accountId/coverage-transfers` | `TransferReviewPageComponent` | yes |
| `restrictions` | `SuspensionPageComponent` | yes |
| `restrictions/reopening` | `ReopeningReviewComponent` | yes |
| `payments` | `ManualPaymentPageComponent` | yes |
| `payments/review` | `PaymentReviewPageComponent` | yes |
| `payments/refunds` | `RefundIntakePageComponent` | yes |
| `payments/refunds/:account/:request/approve` | `RefundApprovalPageComponent` | yes |
| `payments/documents` | `BillingDocumentPageComponent` | yes |
| `catalogue` | `CataloguePageComponent` | yes |
| `admin-users` | `AdminCapabilitiesPageComponent` | yes |
| `admin-users/invite` | `InviteAdminPageComponent` | no |
| `auth/sign-in` | `SignInPageComponent` | no |
| `auth/recovery` | `RecoverAccessPageComponent` | no |
| `auth/bootstrap`, `auth/bootstrap/enrollment` (`data.enrollment`) | `BootstrapPageComponent` | no |
| `auth/invitation` | `AcceptInvitationPageComponent` | no |
| `auth/mfa-enrollment` | `MfaEnrollmentPageComponent` | no |

The root component (`app.ts`) renders one of two shells:

- **Console shell** (sidebar, top bar with sign-out) when the session state is `authenticated` and
  the URL does not start with `/auth/`.
- **Auth shell** (brand bar and a centred stage) otherwise.

Sidebar groups (`Workspace`, `Operations`, `Billing & Plans`, `Governance`) and the landing page
cards are computed from the session's capability list (for example `support`, `billing`,
`licensing-manager`, `security-administrator`, `admin.access.manage`, `admin.invitations.manage`,
`admin.recovery.review`, `admin.audit.identity.read`). This only hides navigation; the route guard
checks for a live session, not for individual capabilities.

## Authentication and session flow

### Session model

The app holds no token. `AdminRefreshCoordinator` documents that the session lives in HttpOnly
cookies; the browser only reads `GET /api/admin/auth/session`, which returns
`{ capabilities, adminId, sessionId, accessExpiresAt }`.

`AdminSessionService` (`features/auth/admin-session.service.ts`) exposes read-only signals: `state`
(`authenticated` / `unauthenticated`), `capabilities`, `notice` (`none`, `expired`, `ended`,
`uncertain`), `signOutNotice`, `sessionId`, `adminId`.

- `check(explicit)` reads the session endpoint and resolves to `active`, `ended` or `uncertain`.
  Network failures, `408`, `429` and `5xx` count as `uncertain`, not as a sign-out.
- On a successful read it sets a timer for `accessExpiresAt`; when it fires, grants are cleared and
  `notice` becomes `expired`. There is no background renewal (the code comment states that renewing
  without user activity would defeat the idle limit). Pages offer a user-triggered `check(true)`.
- `settle(...)` clears capabilities, ids and state whenever a session is ended or unconfirmed.

### Guard

`requireAdminSession` calls `check()` and redirects to `/auth/sign-in` only when the result is
`ended`. An `uncertain` result lets navigation proceed so the page can offer a retry.

### Sign-in and MFA

`SignInPageComponent` fetches a request token from `GET /api/admin/auth/sign-in/context`, then
submits email, password and the authenticator code in one `POST /api/admin/auth/sign-in` with the
token in `X-Admin-CSRF` (through `AdminRefreshCoordinator.signIn`). Password and code inputs are
cleared before the request is sent. After sign-in the page navigates to `/`, or to a payment,
review, refund or invoice page when the matching query parameters are present (`paymentAccount` +
`paymentOrder`, `reviewAccount`, `refundAccount` + `refundRequest`, `invoiceAccount` +
`invoiceOrder`).

Enrollment paths, all under `/api/admin/auth/...` and using an `X-Bootstrap-CSRF` request token:

- **First owner bootstrap** (`auth/bootstrap`): reads `bootstrap/context`, submits a setup proof and
  password, then moves to `auth/bootstrap/enrollment`, which can also resume a pending enrollment
  with email and password.
- **Invitation** (`auth/invitation`): the invitation token is read from the URL fragment and the
  address bar is immediately replaced with `/auth/invitation`; the token is sent only in a POST body.
- **Authenticator enrollment** (`auth/mfa-enrollment`): `bootstrap/enrollment/mfa` supplies the
  token, `/start` returns the setup secret, `/verify` returns one-time recovery codes. Secret, codes
  and token are held in component signals/fields and cleared on `pagehide`.
- **Recovery** (`auth/recovery`): redeems a recovery code, a reset token or a continuation key
  together with the account password at `recovery/{code|reset|continue}` (`X-Admin-CSRF`), then
  navigates to `auth/mfa-enrollment`. `security/recovery` is the signed-in reviewer page that issues
  a reset through `POST /api/admin/recovery/resets`.

### Interceptor and cross-tab coordination

`adminSessionInterceptor` acts only on URLs matching
`/api/admin/(auth/session|invitations|access|audit|sessions|recovery|users)`; all other requests
pass through untouched. For matching requests it:

1. Refuses immediately with `AdminSessionChangedError` (a synthetic `401`) if a sign-out is recorded.
2. On a `401`, asks the coordinator to renew, then retries the request once under a shared lock.
3. Ends the session locally when renewal is rejected or the retry is `401`; reports status `0`
   ("Session renewal outcome unknown") and marks the session `uncertain` when the outcome is unknown.
4. Discards a successful response if the session generation changed while it was in flight.

`AdminRefreshCoordinator` serialises every change to the cookie pair across tabs:

- Uses the Web Locks API (`navigator.locks`, lock `intelibill-admin-session-renewal`); if the API is
  missing, coordination fails and outcomes are treated as unknown.
- Stores `{ generation, state }` in `localStorage` under
  `intelibill.admin.session-coordination.v1`, with states `ready`, `pending`, `replacing`,
  `uncertain`, `ended`, `signed-out`, plus a per-generation sign-out intent key. This is coordination
  metadata only, never a credential.
- Uses its own `HttpClient` built on `HttpBackend`, so its calls bypass the interceptor.
- Refresh: `GET sign-in/context` then `POST /api/admin/auth/refresh` with `X-Admin-CSRF`.
- Sign-out: `GET sign-in/context` then `POST /api/admin/auth/sign-out`. An unconfirmed sign-out
  stays visible as `unconfirmed` and can be retried.
- Other tabs learn of changes through `storage` events; `App` navigates to `/auth/sign-in` when a
  sign-out notice appears outside `/auth/`.

## State management

There is no global store. State is held in:

- **Signals in services and components** (`signal`, `computed`, `effect`, `toSignal`). The session
  service is the only app-wide state.
- **Route and query parameters** for the account, order, request or operation being worked on.
  Catalogue publication and retirement keep their operation key in the `publication` / `retirement`
  query parameter via `Location.replaceState`.
- **Durable drafts for pending writes.** A command with an `operationKey` is persisted before the
  request is sent, so a reload, second tab or lost response resumes the original operation:
  - IndexedDB with `durability: 'strict'`: `PaymentDraftStore` (`payments/manual-payment-drafts.ts`,
    revision compare-and-set) and its subclasses for manual payments, payment reviews, refund
    approvals, coverage exceptions and billing documents; separate stores for suspensions,
    reopenings and trial extensions. Database names start with `intelibill.admin.`.
  - `localStorage` for complimentary grants (`intelibill.admin.complimentary-grant.v1.*`).
  - `sessionStorage` for a pending plan retirement (`catalogue.retirement.*`).
  IndexedDB and `localStorage` drafts are keyed by administrator id plus account (and order where
  relevant); the retirement record is accepted only for the administrator id that wrote it.

## Feature areas

| Folder | Pages / purpose | API prefix (under `/api/admin`) |
|---|---|---|
| `auth` | Sign-in, bootstrap, MFA enrollment, recovery, session service and guard | `auth/*`, `recovery/resets` |
| `security/sessions` | List device sessions, revoke one with a reason | `sessions` |
| `admin-users` | Administrator capabilities, invite, accept invitation | `users`, `invitations`, `auth/invitation` |
| `audit` | Provider-identity audit history with filters and cursor paging | `audit` |
| `accounts` | Account status, closures, cancellation detail, complimentary grants, trial extensions, device permits | `accounts/{id}/status`, `closures`, `subscriptions/{term}/cancellation`, `grants/complimentary`, `shops/{shop}/trial-extensions`, `device-permits` |
| `restrictions` | Suspension preview/apply/release, reopening review | `accounts/{id}/restrictions`, `accounts/{id}/closures/{closure}/shops/{shop}/reopening` |
| `coverage-exceptions` | Coverage replacement exceptions, transfer review | `accounts/{id}/coverage-replacement-exceptions` |
| `payments` | Manual payments, confirmations, payment routing, funds review, refund intake | `accounts/{id}/orders/{order}/manual-payments`, `payment-confirmations`, `payment-routing`, `accounts/{id}/payment-reviews`, `refund-requests` |
| `refunds` | Refund approval | `accounts/{id}/refund-requests/{request}` |
| `documents` | Billing document upload and supersede (SHA-256 digest via `crypto.subtle`) | `accounts/{id}/orders/{order}/billing-documents` |
| `catalogue` | Plan drafts, trial policy, publication, retirement, history | `plans`, `trial-policy/draft`, `catalogue/*` |

## API access and proxying

- All calls use same-origin relative URLs under `/api/admin`. There is no environment file or API
  base URL setting in this app.
- Writes fetch a request token first (`GET /api/admin/auth/sign-in/context`, or the bootstrap or
  invitation context for enrollment flows) and send it as `X-Admin-CSRF` or `X-Bootstrap-CSRF`.
- Account-scoped operational services also send the current session id as `X-Admin-Session`.
- List endpoints are cursor paged (`nextCursor`); account status and device permits read every page
  before returning.
- Error handling reads the problem `code` with `problemCode` from `@intelibill/api` and maps it to
  fixed English messages.
- In production `server.mjs` proxies `/api/admin` to `API_ORIGIN` (see below). `angular.json` has no
  dev-server proxy configuration.

## Shared packages

Resolved by `tsconfig.json` path aliases to source under `src/frontend/shared` (details in the
[shared packages doc](../../shared/architecture_doc/architecture.md)):

- `@intelibill/api`: `SESSION_PORT` / `SessionPort` / `SessionState`, `problemCode`,
  `EntitlementSynchronization`, `validRefundOutcome` / `RefundOutcome`.
- `@intelibill/ui`: `FormSectionStylesComponent` (mounted once in the root template).
- `@intelibill/ui/theme`: the PrimeNG preset and CSS layer name.
- `@intelibill/ui/styles/{fonts,tokens,base}.css`, imported by `src/styles.css`.
- Font files are copied from `../shared/ui/fonts` to `/fonts` by the `assets` entry in `angular.json`.

## Styling and i18n

- `src/styles.css` imports the shared fonts, Tailwind, PrimeIcons and the shared tokens/base, then
  defines admin tokens (`--adm-*`) and utility classes (`.adm-page`, `.adm-pill`, `.adm-callout`, ...)
  in the `ib-base` and `ib-kit` cascade layers, including element-level form styling under `main`.
- Component styles are plain CSS files; the auth form pages share `features/form-page.css`.
- No translation library is used. Copy is English in templates and TypeScript. Dates are formatted
  with `Intl.DateTimeFormat('en-IN', { timeZone: 'Asia/Kolkata' })` helpers in
  `accounts/account-status-api.service.ts`.

## Runtime server and security-relevant behaviour

`server.mjs` (`createAdminServer`, `parseApiOrigin`):

- **Proxy allow-list**: only `/api/admin` and descendants are forwarded. Paths containing a
  backslash, encoded `.`/`/`/`\` (`%2e`, `%2f`, `%5c`) or dot segments are refused, and the resolved
  target must stay on the `API_ORIGIN` origin. Any other `/api` or `/hubs` path answers `404`.
- **Forwarding**: hop-by-hop headers are stripped, the browser-facing `Host` is preserved,
  `x-forwarded-proto` is set to `http` and `x-forwarded-for` to the socket address. Upstream timeout
  is 15 s; failures answer `502` with no detail.
- **Caching**: proxied responses are always `Cache-Control: no-store` (the upstream value is
  dropped). HTML is `no-cache`; other static files are `public, max-age=3600`. Plain error answers
  are `no-store`.
- **Headers** on every response: `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`,
  `Referrer-Policy: no-referrer`. The server sets no `Content-Security-Policy` header.
- **Static files**: `GET`/`HEAD` only (`405` otherwise); paths are confined to the web root; a
  missing path with a file extension is `404`, one without falls back to `index.html`.
- **`API_ORIGIN`** must be a bare `http(s)` origin; the server throws at start-up otherwise.

Browser side:

- No access or refresh token is stored in JavaScript-reachable storage. `localStorage`,
  `sessionStorage` and IndexedDB hold only session coordination metadata and pending-operation
  drafts as described above.
- No service worker is registered; the release suite asserts zero registrations.
- Secrets handled in the page (password, OTP, setup secret, recovery codes, invitation token) are
  cleared from inputs or signals after use and are sent in POST bodies, not URLs.

## Tests

- **Unit** (`bun run test`): Vitest through `@angular/build:unit-test`; `tsconfig.spec.json` includes
  `src/**/*.spec.ts` with `vitest/globals`. Specs sit beside the code;
  `admin-browser-coordination.testing.ts` stubs `localStorage` and `navigator.locks`, and
  `*.test-utils.ts` files hold feature fixtures.
- **Runtime server** (`node --test tests/server.test.mjs`): starts `createAdminServer` against a
  temporary web root and a stub upstream.
- **Release** (`bun run audit:release`): `tests/release/playwright.release.config.ts` runs Chromium
  with one worker, no retries, locale `en-IN`, time zone `Asia/Kolkata`, traces kept on failure.
  `static-server.ts` serves `dist/ADMIN/browser`, answers `404` for `/api` and `/hubs`, and records
  every requested path; specs stub `/api/admin/**` with `page.route`. Suites: `admin-landing`,
  `device-permits`, `reopening`, `shop-closure`, `suspensions`, several at both 1440 px and 390 px
  widths.

## Build output

- `ng build` writes to `dist/ADMIN`; the deployable files are in `dist/ADMIN/browser`.
- Production configuration: `outputHashing: all`, scripts and styles optimised, font inlining off.
- Budgets: initial bundle warning at 650 kB and error at 1 MB; any component style warning at 7 kB
  and error at 12 kB.
- The `development` configuration (used by `ng serve`) disables optimisation and enables source maps.

## Scheduled notification preview (P07-01)

`NotificationPreviewComponent` (`adm-notification-preview`, `OnPush`) renders read-only paged results from `GET /api/admin/accounts/{accountId}/notifications` inside `AccountStatusPageComponent` when the live session holds `support`, `billing`, or `licensing-manager`. Support sees redacted operational rows only; Billing and Licensing Manager see evidenced term prices, bounded shop-name samples, and masked destination hints. A monotonic request sequence across account/capability changes, refresh, focus revalidation, and pagination ensures older commercial responses can never overwrite newer redacted or denied responses, and mid-pagination visibility downgrades clear prior commercial rows before reloading page 1. Exact due and deadline instants preserve microsecond precision identically across UTC and IST, and the panel ships localized assets across all nine Indian locales via `@ngneat/transloco`.

Email detail uses the existing account preview request, pagination and capability lifecycle.
The view requires a commercial capability and the server-provided detail. Links allow only a loopback Mailpit search
for one opaque notification Message-ID and open with referrer and opener isolation.
Suppressed deliveries show a localized, allowlisted reason inside the same keyboard-accessible disclosure.
The component uses a neutral label for an absent or unrecognized code and hides the capture link for suppressed jobs.
