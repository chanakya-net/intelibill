import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/app/pages/placeholder_page.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/auth/domain/entities/auth_session.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:intelibill_mobile/src/features/shops/presentation/controllers/shop_controller.dart';
import 'package:intelibill_mobile/src/features/shops/presentation/pages/create_shop_page.dart';
import 'package:intelibill_mobile/src/features/shops/presentation/pages/manage_shop_page.dart';

void main() {
  group('Shop routes', () {
    testWidgets('navigating to create shop route renders CreateShopPage', (
      tester,
    ) async {
      final container = ProviderContainer(
        overrides: [
          authControllerProvider.overrideWith(
            () => _StubAuthController(AuthControllerState(session: _session())),
          ),
          shopControllerProvider.overrideWith(_StubShopController.new),
        ],
      );
      addTearDown(container.dispose);
      final router = container.read(goRouterProvider);

      await tester.pumpWidget(
        UncontrolledProviderScope(
          container: container,
          child: MaterialApp.router(
            locale: const Locale('en', 'IN'),
            supportedLocales: const [Locale('en', 'IN')],
            localizationsDelegates: const [
              AppLocalizations.delegate,
              ...AppLocalizations.localizationsDelegates,
            ],
            routerConfig: router,
          ),
        ),
      );

      await tester.pumpAndSettle();
      router.go(AppRoutes.createShop);
      await tester.pumpAndSettle();

      expect(find.byType(CreateShopPage), findsOneWidget);
      expect(find.byType(PlaceholderPage), findsNothing);
    });

    testWidgets('navigating to manage shop route renders ManageShopPage', (
      tester,
    ) async {
      final container = ProviderContainer(
        overrides: [
          authControllerProvider.overrideWith(
            () => _StubAuthController(AuthControllerState(session: _session())),
          ),
          shopControllerProvider.overrideWith(_StubShopController.new),
        ],
      );
      addTearDown(container.dispose);
      final router = container.read(goRouterProvider);

      await tester.pumpWidget(
        UncontrolledProviderScope(
          container: container,
          child: MaterialApp.router(
            locale: const Locale('en', 'IN'),
            supportedLocales: const [Locale('en', 'IN')],
            localizationsDelegates: const [
              AppLocalizations.delegate,
              ...AppLocalizations.localizationsDelegates,
            ],
            routerConfig: router,
          ),
        ),
      );

      await tester.pumpAndSettle();
      router.go(AppRoutes.manageShop);
      await tester.pumpAndSettle();

      expect(find.byType(ManageShopPage), findsOneWidget);
      expect(find.byType(PlaceholderPage), findsNothing);
    });
  });
}

class _StubAuthController extends AuthController {
  _StubAuthController(this._initialState);

  final AuthControllerState _initialState;

  @override
  Future<AuthControllerState> build() async => _initialState;
}

class _StubShopController extends ShopController {
  @override
  Future<void> build() async {}
}

AuthSession _session() {
  const sessionShops = <UserShop>[];
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
    activeShopId: null,
    shops: sessionShops,
    rememberMe: false,
  );
}
