# Mobile Architecture

> High-level architecture reference for Flutter Android. For build commands and conventions see [`AGENTS.md`](../AGENTS.md) at repo root.

## Scope

**Flutter Android-only** (`src/mobile/android/intelibill_mobile/`). iOS, macOS, web, and desktop are not yet started and no scaffolding is generated for these platforms.

The scaffold contains minimal sample code (status/health slice only) demonstrating the architecture. Real login, inventory, sales, shop workflows, Firebase, push notifications, Play Store signing, and offline database features are not yet implemented.

## Stack

| Concern | Choice |
|---|---|
| Platform | Flutter (Android-only, no iOS/macOS/web/desktop) |
| State management | Riverpod with generated providers (`flutter_riverpod`, `riverpod_annotation`, `riverpod_generator`) |
| Navigation | `go_router` |
| Networking | `dio` with interceptors |
| Code generation | `freezed`, `json_serializable`, `build_runner` |
| Secure storage | `flutter_secure_storage` (auth tokens) |
| Preferences | `shared_preferences` (non-sensitive config) |
| Linting | `very_good_analysis` (strict Dart/Flutter) |
| Testing | Unit, widget, golden, repository/data-source, provider/controller, integration-test scaffolding |

## Architecture Layers

Feature-first clean architecture with domain-independent code:

```
lib/
├── domain/                     # Business logic, entities, contracts (zero Flutter/Riverpod/Dio deps)
│   ├── entities/
│   ├── failures.dart           # Domain-level error types
│   └── repositories/           # Abstract repository interfaces
│
├── data/                       # Implementation: APIs, local storage, repositories
│   ├── datasources/            # Remote (Dio) and local (SecureStorage, SharedPrefs)
│   ├── models/                 # DTO/JSON mappings (Freezed, JsonSerializable)
│   └── repositories/           # Concrete repository implementations
│
├── presentation/              # UI layer: screens, providers, widgets
│   ├── providers/              # Riverpod providers (generated and manual)
│   ├── pages/                  # Feature pages
│   ├── widgets/                # Reusable UI components
│   └── routes/                 # GoRouter route configuration
│
└── app/                        # App initialization & main
    ├── app.dart                # Root MaterialApp/CupertinoApp
    └── main.dart               # Entry point
```

## Dependency Rules

- **Domain** (entities, contracts): zero external deps beyond `equatable`, `freezed_annotation`
- **Data**: domain + `dio`, `freezed`, `json_serializable`, `flutter_secure_storage`, `shared_preferences`
- **Presentation**: data + `riverpod`, `go_router`, Flutter SDK
- **No circular imports** across layers

## Feature-First Structure

Each feature (e.g., `status`) groups domain, data, and presentation:

```
lib/features/status/
├── domain/
│   ├── entities/
│   └── repositories/
├── data/
│   ├── datasources/
│   ├── models/
│   └── repositories/
└── presentation/
    ├── providers/
    ├── pages/
    └── widgets/
```

## API Configuration

### Local Android Emulator

Default backend API base URL for emulator: **`http://10.0.2.2:5277/api`**

(The special IP `10.0.2.2` inside the Android emulator points to the host machine's `localhost`.)

### Configuration

API base URL is configured in data layer:

```
lib/data/datasources/api_client.dart (or similar)
```

Override via environment variables or app configuration for different environments (dev, staging, prod).

## Testing Layers

### Unit Tests
- Test domain entities, use cases, and value objects
- Path: `test/domain/`, `test/data/repositories/`

### Widget Tests
- Test UI components in isolation
- Path: `test/presentation/widgets/`

### Golden Tests
- Verify UI appearance
- Path: `test/presentation/pages/` (golden files in `test/golden/`)

### Provider/Controller Tests
- Test Riverpod providers and business logic
- Path: `test/presentation/providers/`

### Data Source Tests
- Test remote (Dio) and local (storage) layers
- Path: `test/data/datasources/`

### Integration Tests
- End-to-end app flows on real or emulated Android
- Path: `integration_test/`

### Running Tests

```bash
cd src/mobile/android/intelibill_mobile

# All tests with coverage
flutter test --coverage

# Unit & widget tests only (no integration)
flutter test

# Integration tests
flutter drive --target=integration_test/app_test.dart

# Coverage report
lcov --list coverage/lcov.info
```

## CI/CD Workflows

Two GitHub Actions workflows handle mobile CI:

- **`.github/workflows/mobile-pr-ci.yml`**: Runs on pull requests and manual dispatch
  - Checkout, setup Flutter, `flutter pub get`
  - `dart run build_runner build --delete-conflicting-outputs`
  - Check generated code is up-to-date (git diff)
  - `dart format --set-exit-if-changed .`
  - `flutter analyze`
  - `flutter test --coverage` (upload coverage artifact)
  - `flutter build apk --debug`

- **`.github/workflows/mobile-main-ci.yml`**: Runs on pushes to main
  - Same steps as PR workflow

## Development Workflow

1. **Setup**: `flutter pub get`
2. **Generate code**: `dart run build_runner build --delete-conflicting-outputs`
   - Regenerate after adding new Riverpod providers, Freezed models, or JsonSerializable DTOs
3. **Format**: `dart format --set-exit-if-changed .` (pre-commit hook recommended)
4. **Analyze**: `flutter analyze` (must pass with Very Good Analysis rules)
5. **Test**: `flutter test --coverage`
6. **Run**: `flutter run` (on emulator or physical device)
7. **Build**: `flutter build apk --debug` (or `--release` for Play Store)

## Package Location

```
src/mobile/android/intelibill_mobile/
├── lib/                  # Dart/Flutter source
├── android/              # Android native files (namespace: com.intelibill.mobile)
├── test/                 # Unit & widget tests
├── integration_test/     # Integration tests
├── pubspec.yaml          # Flutter/Dart dependencies
├── pubspec.lock          # Locked versions
├── analysis_options.yaml # Very Good Analysis config
└── README.md             # Project-specific notes
```

## Future Work

This scaffold is phase 1 (architecture proof-of-concept only). Future phases will implement:

- **Login & Auth**: Email, phone, OAuth (Google/Facebook), token refresh/revoke
- **Inventory**: Stock inbound (single + batch), batch search, stock level dashboard
- **Sales**: POS sale recording, history view, profit-loss reporting
- **Shops**: Create shop, switch active shop, membership management
- **Offline DB**: Local SQLite sync with backend
- **Firebase**: Push notifications, Crashlytics (not yet started)
- **Play Store**: Signing and release workflows (not yet started)
- **iOS**: After Android feature parity (not yet started)

## References

- [technical_requirements.md](../technical_requirements.md) — full project requirements including mobile phase scope
- [AGENTS.md](../AGENTS.md) — build & test commands, conventions
- [Backend Architecture](backend-architecture.md)
- [Frontend Architecture](frontend-architecture.md)
