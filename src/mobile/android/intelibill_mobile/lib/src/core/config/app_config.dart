class AppConfig {
  /// The address the Android emulator uses to reach the host machine. Correct
  /// for local development, wrong for anything that leaves a developer's
  /// machine — which is what [assertConfiguredForRelease] guards against.
  static const String emulatorDefaultApiBaseUrl = 'http://10.0.2.2:5277/api';

  static const String apiBaseUrl = String.fromEnvironment(
    'API_BASE_URL',
    defaultValue: emulatorDefaultApiBaseUrl,
  );

  static const bool isReleaseBuild = bool.fromEnvironment('dart.vm.product');

  /// A release build without `--dart-define=API_BASE_URL` compiles cleanly,
  /// installs cleanly, and sends every request to an emulator-only address over
  /// cleartext. Failing at startup turns that into a build mistake someone
  /// notices, rather than an app that merely appears to have no network.
  static void assertConfiguredForRelease() {
    if (!isReleaseBuild) {
      return;
    }

    if (apiBaseUrl == emulatorDefaultApiBaseUrl) {
      throw StateError(
        'API_BASE_URL was not supplied to this release build. Build with '
        '--dart-define=API_BASE_URL=https://<api-host>/api '
        '(see tool/build-release.sh).',
      );
    }

    if (!apiBaseUrl.startsWith('https://')) {
      throw StateError(
        'API_BASE_URL must be https in a release build; got "$apiBaseUrl". '
        'An access token travels on every request.',
      );
    }
  }
}
