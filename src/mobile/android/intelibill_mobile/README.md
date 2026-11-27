# intelibill_mobile

Flutter Android client for the Intelibill merchant API (`applicationId` `com.intelibill.mobile`). Android is the only generated platform. The app signs a merchant user in and covers dashboard, inventory, purchase orders, sales, customers, credit notes, suppliers, services, expenses, discounts, bank accounts, users and shops.

Architecture, layering rules and test layout: [architecture_doc/architecture.md](architecture_doc/architecture.md).

All commands below run from this directory (`src/mobile/android/intelibill_mobile`).

## Flutter version

Mobile CI pins Flutter through `.flutter-version` (currently **3.44.2**, stable channel) and fails if the installed version differs. `tool/codegen.sh` makes the same check locally. Dart SDK constraint: `^3.11.5` (`pubspec.yaml`).

Install Flutter **3.44.2** stable and verify with `flutter --version`.

FVM is optional. `tool/codegen.sh` uses `fvm flutter` whenever `fvm` is on the `PATH`, and exits if that resolves to a version other than `.flutter-version`. The committed `.fvm/fvm_config.json` currently names `3.41.9`, so with FVM installed select the pinned version first:

```bash
fvm install 3.44.2
fvm use 3.44.2
fvm flutter --version
```

## Setup and code generation

```bash
flutter pub get
./tool/codegen.sh
```

`tool/codegen.sh` runs `flutter pub get`, `flutter gen-l10n`, `dart run build_runner build` and `dart format .`. Run it after changing Riverpod providers, Freezed or JSON models, or the ARB files in `lib/l10n/`, and commit the results: `lib/**/*.g.dart`, `lib/**/*.freezed.dart` and `lib/src/core/localization/`. CI runs the same script and fails when those files are out of date.

## Verify

The checks CI runs, in order:

```bash
dart format --set-exit-if-changed .
flutter analyze
flutter test --coverage
flutter build apk --debug
```

`flutter test` runs everything under `test/`. The one integration test is not run by CI; run it on an emulator or device:

```bash
flutter test integration_test/app_smoke_test.dart
```

## Running against a local backend

Start the merchant API from the repository root (it listens on `http://localhost:5277`):

```bash
dotnet run --project src/backend/intelibill/Intelibill.Api
```

Database, migrations and the local stack are described in the [repository README](../../../../README.md).

The app has a sign-in screen only; `/register` and `/forgot-password` are placeholder pages. Create the account outside the app and sign in with its identifier and password (`POST /api/auth/login`).

### Android emulator

Without `API_BASE_URL` the app uses `http://10.0.2.2:5277/api`, the emulator's address for the host machine's `localhost`:

```bash
flutter run
```

### Physical device

`10.0.2.2` does not exist on a real device. Pass the machine's LAN address:

```bash
flutter run --dart-define=API_BASE_URL=http://<machine-lan-ip>:5277/api
```

## Release builds

`API_BASE_URL` is compiled in at build time. The build script refuses to run without it or with a non-`https` value, and a release build refuses to start (`AppConfig.assertConfiguredForRelease`) if it was built with the emulator default or a non-`https` URL:

```bash
API_BASE_URL=https://<api-host>/api ./tool/build-release.sh
```

Extra arguments are passed to `flutter build apk --release` (for example `--split-per-abi`). Changing where the API lives means rebuilding the app.

The `release` build type in `android/app/build.gradle.kts` still signs with the debug keys; there is no release signing configuration or store workflow.

## Dependencies

```bash
flutter pub get        # after editing pubspec.yaml
./tool/codegen.sh      # regenerate if generators or annotations changed
flutter pub outdated   # list newer versions
```

`pdf` and `printing` are pinned to exact versions in `pubspec.yaml`.

## CI

- `.github/workflows/mobile-pr-ci.yml` — pull requests to `main` that touch this directory or either mobile workflow.
- `.github/workflows/mobile-main-ci.yml` — pushes to `main` on the same paths; also updates the mobile badges under `.github/badges/`.
