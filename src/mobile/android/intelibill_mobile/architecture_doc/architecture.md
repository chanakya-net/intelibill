# Mobile Architecture

> Scope: the Flutter Android app in `src/mobile/android/intelibill_mobile/` only. Setup, commands and release builds are in the [README](../README.md); repository-wide conventions are in [`AGENTS.md`](../../../../../AGENTS.md).

## Scope

Android is the only platform: `android/` is the only platform folder, and no iOS, macOS, web or desktop scaffolding is generated. Package and `applicationId`: `com.intelibill.mobile`.

The app is an online client of the merchant API. It keeps no local database; the only data stored on the device is the token pair, the remembered sign-in identifier, the chosen locale and purchase order drafts.

Not implemented: registration and password reset (`/register` and `/forgot-password` render `PlaceholderPage`), the profit and loss page (`/sales/profit-loss`, placeholder), offline storage or sync, push notifications, crash reporting, and release signing (the `release` build type signs with the debug keys).

## Stack

Versions are those in `pubspec.yaml`; Flutter is pinned by `.flutter-version` (`3.44.2`), Dart SDK `^3.11.5`.

| Concern | Choice |
|---|---|
| State management | Riverpod 3 with generated providers (`flutter_riverpod`, `riverpod_annotation`, `riverpod_generator`) |
| Navigation | `go_router` |
| Networking | `dio`, wrapped by `ApiClient` |
| Models | `freezed` / `freezed_annotation`, `json_serializable` / `json_annotation`, `equatable` |
| Secure storage | `flutter_secure_storage` (access and refresh tokens) |
| Preferences | `shared_preferences` (locale, remembered identifier, purchase order drafts) |
| Localisation | `flutter_localizations`, `intl`, `flutter gen-l10n` (`l10n.yaml`) |
| Charts | `fl_chart` |
| Barcode scanning | `mobile_scanner` (`CAMERA` permission in the main manifest) |
| Documents | `pdf` and `printing`, pinned to exact versions |
| Linting | `very_good_analysis`, with the exceptions listed in `analysis_options.yaml` |
| Testing | `flutter_test`, `mocktail`, `golden_toolkit`, `integration_test` |

## Source Layout

```
lib/
├── main.dart                 # Entry point: release config check, date formatting, ProviderScope
├── l10n/                     # ARB sources (template: app_en_IN.arb)
└── src/
    ├── app/                  # App composition
    │   ├── app.dart          # IntelibillApp (MaterialApp.router)
    │   ├── router/           # AppRoutes, goRouterProvider, redirects
    │   ├── shell/            # MobileAppShell, menu items, role visibility, More sheet
    │   ├── navigation/       # Landing route for a signed-in user
    │   ├── pages/            # Language page, placeholder page
    │   └── theme/            # AppTheme.lightTheme (Material 3)
    ├── core/                 # Cross-feature infrastructure
    │   ├── config/           # AppConfig (API base URL)
    │   ├── errors/           # Failure (sealed), AppException
    │   ├── network/          # ApiClient, AuthInterceptor, ApiErrorMapper, ProblemDetails
    │   ├── storage/          # SecureStorage, PreferencesStorage
    │   ├── localization/     # Generated AppLocalizations*, LocaleController
    │   ├── formatting/       # Currency formatting
    │   └── utils/            # Wire-format helpers (date/time, payment method)
    ├── features/<feature>/   # One folder per feature, see below
    └── shared/               # UI building blocks used by several features
        ├── barcode_scanner/  # Scanner page and result type
        └── documents/        # PDF theme, fonts, preview scaffold, print/share output
```

Features: `app_status`, `auth`, `bank_accounts`, `credit_notes`, `customers`, `dashboard`, `discounts`, `expenses`, `inventory`, `purchase_orders`, `sales`, `services`, `shops`, `suppliers`, `users`. `app_status` is the original sample slice; it has tests (including the only golden test) but no route.

## Feature Structure

```
lib/src/features/<feature>/
├── domain/
│   ├── entities/         # Plain Dart, Equatable
│   ├── repositories/     # Abstract interfaces
│   └── use_cases/        # One class per operation (auth has none)
├── data/
│   ├── data_sources/     # Remote (ApiClient); purchase_orders also has a local draft store
│   ├── dto/              # Freezed + json_serializable wire models (shops uses dtos/)
│   ├── mappers/          # DTO <-> entity
│   └── repositories/     # Repository implementations
└── presentation/
    ├── controllers/      # Riverpod providers and notifiers
    ├── pages/
    └── widgets/          # Not present in every feature
```

Some features add folders under `presentation/`: `documents/` (PDF builders in `sales`, `credit_notes`, `purchase_orders`), `utils/`, `localization/`; `inventory` has `domain/utils/`.

## Dependency Rules

These describe what the code does today; keep new code within them.

- **Domain** imports only `equatable` and domain code. No Flutter, Riverpod, Dio or `core/`.
- **Data** imports domain, `core/errors`, `core/network`, `core/storage`, `core/utils` and `freezed_annotation`. Data sources call `ApiClient`; they do not import `dio`. The purchase order draft store is the one data file that imports `flutter/foundation` and `equatable`.
- **Presentation** imports domain and data. There is no separate dependency-injection layer: each feature's controller file declares the `@riverpod` providers that build its data source, repository and use cases from `apiClientProvider`, then the notifier that uses them.
- **Features** may import other features. Every feature except `app_status` and `customers` imports from `auth`; `sales` uses `customers`, `discounts` uses `inventory`, and `purchase_orders` uses `inventory`, `suppliers`, `shops` and `dashboard`.
- **`core/` and `shared/`** do not import features, with one exception: `core/network/api_client_provider.dart` uses the auth remote data source to refresh tokens.
- **`app/`** is the composition root and imports everything.
- Third-party packages `dio`, `flutter_secure_storage` and `shared_preferences` are imported only in `core/`, plus `auth_controller.dart`.

## State and Providers

Providers are generated: annotate with `@riverpod` and commit the `.g.dart` part. Two providers are written by hand: `goRouterProvider` and one in `purchase_order_preview_controller.dart`. `main.dart` wraps the app in a single `ProviderScope`.

## Navigation and Access Control

`lib/src/app/router/app_router.dart` holds every path in `AppRoutes` and the `GoRouter` in `goRouterProvider`. The router refreshes whenever `authControllerProvider` changes.

- `/login`, `/forgot-password` and `/register` sit outside the shell. Every other route is a child of one `ShellRoute` that wraps pages in `MobileAppShell`.
- The top-level `redirect` sends a signed-out user to `/login` and a signed-in user away from the auth routes to their landing route (`resolveAuthenticatedHomeRoute`: dashboard for owner or manager, otherwise sales history).
- The same redirect guards the dashboard, discounts, services, expenses, bank accounts and purchase order routes by role and falls back to sales history.
- Role checks live in `app/shell/menu_visibility.dart` and read the role of the session's active shop (`owner`, `manager`, `staff`, compared case-insensitively). The shell uses the same functions to filter the bottom `NavigationBar` and the More sheet, so menu visibility and route guards share one source.

These checks are a convenience for the user interface. The API remains the authority on what a role may do.

## Networking and Errors

- `AppConfig.apiBaseUrl` comes from the compile-time define `API_BASE_URL`, defaulting to `http://10.0.2.2:5277/api` (the Android emulator's route to the host). In a release build `AppConfig.assertConfiguredForRelease()` throws at startup if the default was left in place or the URL is not `https`.
- `ApiClient` wraps one `Dio` instance (JSON headers, 30 s connect/send/receive timeouts) and exposes `get`, `post`, `put`, `patch`, `delete`. Every failure is rethrown as `AppException` carrying a `Failure`.
- `ApiErrorMapper` turns `DioException`s into `Failure` variants (`validation`, `unauthorized`, `forbidden`, `notFound`, `server`, `network`, `timeout`, `serialization`, `unknown`). JSON error bodies are parsed as `ProblemDetails`; a `400` becomes `Failure.validation` with its field errors.
- `AuthInterceptor` adds `Authorization: Bearer <access token>` to every request. On a `401` from anything but the login, refresh and revoke endpoints it refreshes once, saves the new pair and retries the request; if there is no refresh token, the refresh fails, or the retry is again `401`, it clears the stored tokens.
- `apiClientProvider` builds the authenticated client. Refresh calls go through a second `ApiClient` without the interceptor.

## Authentication and Session

- `AuthController` (`features/auth/presentation/controllers/`) is the session owner. On start it loads the remembered identifier and, if a refresh token is stored, exchanges it for a new session; otherwise the user is signed out.
- It also handles sign-in, sign-out (clears the stored tokens and the session), profile update, password change and shop switching (`/shops/default`).
- `AuthSession` carries the token pair with expiries, the user, `activeShopId` and the user's shops with a role per shop.
- Tokens are stored only through `SecureStorage` (`flutter_secure_storage`). Non-sensitive values go through `PreferencesStorage` (`shared_preferences`).

## Localisation

- Sources are the ARB files in `lib/l10n/` for nine languages (`en`, `hi`, `ta`, `te`, `bn`, `ml`, `kn`, `mr`, `gu`), each as a base file and an `_IN` file; `app_en_IN.arb` is the template.
- `flutter gen-l10n` writes `AppLocalizations` into `lib/src/core/localization/`, which is committed.
- `LocaleController` holds the current locale (default `en-IN`, restricted to the nine `*-IN` locales) and persists it under the `intelibillLocale` preference key.
- `test/core/localization/purchase_order_arb_coverage_test.dart` checks ARB coverage for the purchase order messages.

## Documents and Printing

`shared/documents/` builds PDFs with `pdf` and prints or shares them through `printing`, behind `DocumentOutputGateway`. Fonts are bundled, not downloaded: `assets/fonts/` holds Noto Sans regular and bold for Latin, Bengali, Devanagari, Gujarati, Kannada, Malayalam, Tamil and Telugu, with their OFL texts in `assets/licenses/`. `DocumentFontResolver` picks the script from the locale. Feature PDF builders live in each feature's `presentation/documents/`.

## Code Generation

`./tool/codegen.sh` is the only supported entry point. It verifies the Flutter version against `.flutter-version`, then runs `flutter pub get`, `flutter gen-l10n`, `dart run build_runner build` and `dart format .`. Generated files are committed: `lib/**/*.g.dart`, `lib/**/*.freezed.dart` and `lib/src/core/localization/`.

## Testing

`test/` mirrors `lib/src/`:

| Path | Covers |
|---|---|
| `test/app/` | Router and redirects, shell, menu visibility, theme |
| `test/core/` | Config, network (interceptor, error mapping, problem details), storage, localisation, utils |
| `test/features/<feature>/data/` | DTOs, mappers, remote data sources, repositories |
| `test/features/<feature>/domain/` | Use cases and entities |
| `test/features/<feature>/presentation/` | Controllers, pages, widgets, PDF builders |
| `test/shared/` | Barcode scanner page, document primitives and output |
| `test/mocks/` | Shared `mocktail` mocks (`MockApiClient`) |
| `integration_test/app_smoke_test.dart` | Starts the app and expects the sign-in page |

The one golden test is `test/features/app_status/presentation/pages/app_status_page_golden_test.dart`; its images are in the `goldens/` folder beside it, with a separate image for Linux (the CI runner).

`flutter test --coverage` runs everything under `test/`. The integration test needs a device or emulator and is not part of CI.

## CI

Both workflows run in this directory on `ubuntu-latest`, with the same steps:

1. Read `.flutter-version`, install that Flutter (stable) and fail if the installed version differs.
2. `flutter pub get`, then `./tool/codegen.sh`.
3. Fail if `git diff` shows changes in `lib/**/*.g.dart`, `lib/**/*.freezed.dart` or `lib/src/core/localization/`.
4. `dart format --set-exit-if-changed .`
5. `flutter analyze`
6. `flutter test --coverage`, then upload `coverage/` as the `mobile-coverage` artefact.
7. `flutter build apk --debug`

- `.github/workflows/mobile-pr-ci.yml` runs on pull requests to `main` that touch this directory or either mobile workflow, and on manual dispatch.
- `.github/workflows/mobile-main-ci.yml` runs on pushes to `main` for the same paths, and on manual dispatch. It additionally writes the mobile test and build badges to `.github/badges/` and commits them to `main`.

Neither workflow builds a release APK or publishes anything.

## Package Location

```
src/mobile/android/intelibill_mobile/
├── lib/                   # Dart source (see Source Layout)
├── test/                  # Unit, widget and golden tests
├── integration_test/      # app_smoke_test.dart
├── android/               # Android project (Gradle Kotlin DSL)
├── assets/                # Bundled PDF fonts and their licences
├── tool/                  # codegen.sh, build-release.sh
├── architecture_doc/      # This document
├── pubspec.yaml           # Dependencies
├── pubspec.lock           # Locked versions
├── l10n.yaml              # gen-l10n configuration
├── analysis_options.yaml  # very_good_analysis plus local exceptions
├── .flutter-version       # Flutter pin read by CI and codegen.sh
└── README.md              # Setup, run, release
```

## References

- [README](../README.md) — setup, commands, release builds
- [`AGENTS.md`](../../../../../AGENTS.md) — repository-wide build commands and conventions
- [Merchant backend architecture](../../../../backend/intelibill/architecture_doc/architecture.md) — the API this app calls
- [Merchant web architecture](../../../../frontend/intelibill/architecture_doc/architecture.md)
