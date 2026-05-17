import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/app/theme/app_theme.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/auth/domain/entities/auth_session.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/pages/profile_settings_page.dart';

const _editProfileDestinationText = 'Edit Profile Destination';
const _changePasswordDestinationText = 'Change Password Destination';

class _StubAuthController extends AuthController {
  _StubAuthController(this._state);

  final AuthControllerState _state;

  @override
  Future<AuthControllerState> build() async {
    return _state;
  }
}

class _SwitchTrackingAuthController extends _StubAuthController {
  _SwitchTrackingAuthController(super._state);

  String? switchedShopId;

  @override
  Future<void> switchShop({required String shopId}) async {
    switchedShopId = shopId;
  }
}

AuthSession _sessionFixture() => AuthSession(
  accessToken: 'access_token',
  refreshToken: 'refresh_token',
  accessTokenExpiresAt: DateTime.utc(2026, 5, 15, 10),
  refreshTokenExpiresAt: DateTime.utc(2026, 6, 15, 10),
  user: const AuthUser(
    id: 'user-1',
    email: 'alex@example.com',
    phoneNumber: '9876543210',
    firstName: 'Alex',
    lastName: 'Johnson',
    language: 'en-IN',
  ),
  activeShopId: 'shop-1',
  shops: const [
    UserShop(
      shopId: 'shop-1',
      shopName: 'North Shop',
      role: 'Owner',
      isDefault: true,
      lastUsedAt: null,
    ),
    UserShop(
      shopId: 'shop-2',
      shopName: 'South Shop',
      role: 'Owner',
      isDefault: false,
      lastUsedAt: null,
    ),
  ],
  rememberMe: false,
);

GoRouter _routerForProfileSettings() {
  return GoRouter(
    initialLocation: AppRoutes.profile,
    routes: [
      GoRoute(
        path: AppRoutes.profile,
        builder: (context, state) => const ProfileSettingsPage(),
      ),
      GoRoute(
        path: AppRoutes.profileEdit,
        builder: (context, state) => const Scaffold(
          body: Center(child: Text(_editProfileDestinationText)),
        ),
      ),
      GoRoute(
        path: AppRoutes.profileChangePassword,
        builder: (context, state) => const Scaffold(
          body: Center(child: Text(_changePasswordDestinationText)),
        ),
      ),
    ],
  );
}

Widget _buildPage(ProviderContainer container) {
  return UncontrolledProviderScope(
    container: container,
    child: MaterialApp.router(
      theme: AppTheme.lightTheme,
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      locale: const Locale('en', 'IN'),
      routerConfig: _routerForProfileSettings(),
    ),
  );
}

void main() {
  group('ProfileSettingsPage', () {
    testWidgets('displays user name and email from session', (tester) async {
      final session = _sessionFixture();
      final state = AuthControllerState(session: session);
      final container = ProviderContainer(
        overrides: [
          authControllerProvider.overrideWith(() => _StubAuthController(state)),
        ],
      );

      await tester.pumpWidget(_buildPage(container));
      await tester.pumpAndSettle();

      expect(find.text('Alex Johnson'), findsOneWidget);
      expect(find.text('alex@example.com'), findsOneWidget);
    });

    testWidgets('displays all shops in dropdown menu', (tester) async {
      final session = _sessionFixture();
      final state = AuthControllerState(session: session);
      final container = ProviderContainer(
        overrides: [
          authControllerProvider.overrideWith(() => _StubAuthController(state)),
        ],
      );

      await tester.pumpWidget(_buildPage(container));
      await tester.pumpAndSettle();

      final dropdownFinder = find.byType(DropdownButtonFormField<String>);
      expect(dropdownFinder, findsOneWidget);
      expect(find.text('North Shop'), findsOneWidget);

      await tester.tap(dropdownFinder);
      await tester.pumpAndSettle();

      expect(find.text('South Shop'), findsOneWidget);
    });

    testWidgets('calls switchShop on dropdown selection', (tester) async {
      final session = _sessionFixture();
      final state = AuthControllerState(session: session);
      final controller = _SwitchTrackingAuthController(state);
      final container = ProviderContainer(
        overrides: [authControllerProvider.overrideWith(() => controller)],
      );

      await tester.pumpWidget(_buildPage(container));
      await tester.pumpAndSettle();

      final dropdown = tester.widget<DropdownButtonFormField<String>>(
        find.byType(DropdownButtonFormField<String>),
      );
      dropdown.onChanged?.call('shop-2');
      await tester.pumpAndSettle();

      expect(controller.switchedShopId, equals('shop-2'));
    });

    testWidgets('navigates to edit profile when tile is tapped', (
      tester,
    ) async {
      final session = _sessionFixture();
      final state = AuthControllerState(session: session);
      final container = ProviderContainer(
        overrides: [
          authControllerProvider.overrideWith(() => _StubAuthController(state)),
        ],
      );

      await tester.pumpWidget(_buildPage(container));
      await tester.pumpAndSettle();

      await tester.tap(find.text('Edit Profile'));
      await tester.pumpAndSettle();

      expect(find.text(_editProfileDestinationText), findsOneWidget);
    });

    testWidgets('navigates to change password when tile is tapped', (
      tester,
    ) async {
      final session = _sessionFixture();
      final state = AuthControllerState(session: session);
      final container = ProviderContainer(
        overrides: [
          authControllerProvider.overrideWith(() => _StubAuthController(state)),
        ],
      );

      await tester.pumpWidget(_buildPage(container));
      await tester.pumpAndSettle();

      await tester.tap(find.text('Change Password'));
      await tester.pumpAndSettle();

      expect(find.text(_changePasswordDestinationText), findsOneWidget);
    });
  });
}
