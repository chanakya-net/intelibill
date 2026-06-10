import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';

import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/app/shell/mobile_app_shell.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/auth/domain/entities/auth_session.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';

class _TestAuthController extends AuthController {
  _TestAuthController(this._state);

  AuthControllerState _state;
  int signOutCalls = 0;

  @override
  Future<AuthControllerState> build() async => _state;

  void setState(AuthControllerState next) {
    _state = next;
    state = AsyncData(next);
  }

  @override
  Future<void> signOut() async {
    signOutCalls += 1;
    setState(_state.copyWith(clearSession: true));
  }
}

AuthSession _sessionForRole(
  String role, {
  String? activeShopId,
  List<UserShop>? shops,
}) {
  return AuthSession(
    accessToken: 'access-token',
    refreshToken: 'refresh-token',
    accessTokenExpiresAt: DateTime.utc(2026, 5, 15, 10),
    refreshTokenExpiresAt: DateTime.utc(2026, 6, 15, 10),
    user: const AuthUser(
      id: 'user-1',
      email: 'owner@example.com',
      phoneNumber: null,
      firstName: 'Alex',
      lastName: 'Smith',
      language: 'en-IN',
    ),
    activeShopId: activeShopId,
    shops:
        shops ??
        [
          UserShop(
            shopId: 'shop-1',
            shopName: 'Primary Shop',
            role: role,
            isDefault: true,
            lastUsedAt: DateTime.utc(2026, 5, 12, 10),
          ),
        ],
    rememberMe: false,
  );
}

GoRouter _routerForShell({String initialLocation = AppRoutes.dashboard}) {
  return GoRouter(
    initialLocation: initialLocation,
    routes: [
      ShellRoute(
        builder: (context, state, child) => MobileAppShell(child: child),
        routes: [
          GoRoute(
            path: AppRoutes.dashboard,
            builder: (context, state) =>
                const Scaffold(body: Text('Dashboard')),
          ),
          GoRoute(
            path: AppRoutes.inventory,
            builder: (context, state) =>
                const Scaffold(body: Text('Inventory')),
          ),
          GoRoute(
            path: AppRoutes.salesHistory,
            builder: (context, state) => const Scaffold(body: Text('Sales')),
          ),
          GoRoute(
            path: AppRoutes.customers,
            builder: (context, state) =>
                const Scaffold(body: Text('Customers')),
          ),
          GoRoute(
            path: AppRoutes.language,
            builder: (context, state) =>
                const Scaffold(body: Text('Language Page')),
          ),
        ],
      ),
    ],
  );
}

void main() {
  group('MobileAppShell', () {
    testWidgets('staff does not see customers tab', (tester) async {
      final controller = _TestAuthController(
        AuthControllerState(session: _sessionForRole('Staff')),
      );
      final router = _routerForShell(initialLocation: AppRoutes.salesHistory);

      await tester.pumpWidget(
        ProviderScope(
          overrides: [authControllerProvider.overrideWith(() => controller)],
          child: MaterialApp.router(
            locale: const Locale('en', 'IN'),
            supportedLocales: const [Locale('en', 'IN')],
            localizationsDelegates: const [
              AppLocalizations.delegate,
              GlobalMaterialLocalizations.delegate,
              GlobalWidgetsLocalizations.delegate,
              GlobalCupertinoLocalizations.delegate,
            ],
            routerConfig: router,
          ),
        ),
      );
      await tester.pumpAndSettle();

      expect(
        find.widgetWithText(NavigationDestination, 'Dashboard'),
        findsNothing,
      );
      expect(find.text('Inventory'), findsWidgets);
      expect(find.text('Sales'), findsWidgets);
      expect(find.text('More'), findsWidgets);
      expect(find.text('Customers'), findsNothing);
    });

    testWidgets('more menu triggers logout action', (tester) async {
      final controller = _TestAuthController(
        AuthControllerState(session: _sessionForRole('Owner')),
      );
      final router = _routerForShell();

      await tester.pumpWidget(
        ProviderScope(
          overrides: [authControllerProvider.overrideWith(() => controller)],
          child: MaterialApp.router(
            locale: const Locale('en', 'IN'),
            supportedLocales: const [Locale('en', 'IN')],
            localizationsDelegates: const [
              AppLocalizations.delegate,
              GlobalMaterialLocalizations.delegate,
              GlobalWidgetsLocalizations.delegate,
              GlobalCupertinoLocalizations.delegate,
            ],
            routerConfig: router,
          ),
        ),
      );
      await tester.pumpAndSettle();

      final moreLabel = find.text('More').first;
      await tester.tapAt(tester.getCenter(moreLabel));
      await tester.pumpAndSettle();

      expect(find.byType(BottomSheet), findsOneWidget);
      await tester.scrollUntilVisible(find.text('Logout'), 200);
      await tester.tap(find.text('Logout'));
      await tester.pumpAndSettle();

      expect(controller.signOutCalls, equals(1));
    });

    testWidgets('more menu navigates to language route', (tester) async {
      final controller = _TestAuthController(
        AuthControllerState(session: _sessionForRole('Owner')),
      );
      final router = _routerForShell();

      await tester.pumpWidget(
        ProviderScope(
          overrides: [authControllerProvider.overrideWith(() => controller)],
          child: MaterialApp.router(
            locale: const Locale('en', 'IN'),
            supportedLocales: const [Locale('en', 'IN')],
            localizationsDelegates: const [
              AppLocalizations.delegate,
              GlobalMaterialLocalizations.delegate,
              GlobalWidgetsLocalizations.delegate,
              GlobalCupertinoLocalizations.delegate,
            ],
            routerConfig: router,
          ),
        ),
      );
      await tester.pumpAndSettle();

      final moreLabel = find.text('More').first;
      await tester.tapAt(tester.getCenter(moreLabel));
      await tester.pumpAndSettle();

      expect(find.byType(BottomSheet), findsOneWidget);
      await tester.scrollUntilVisible(find.text('Language'), 200);
      await tester.tap(find.text('Language'));
      await tester.pumpAndSettle();

      expect(find.text('Language Page'), findsOneWidget);
    });
  });
}
