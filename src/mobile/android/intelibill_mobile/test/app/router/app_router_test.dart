import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/auth/domain/entities/auth_session.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:intelibill_mobile/src/features/dashboard/presentation/controllers/dashboard_controller.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sales_history_query.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sales_history_summary.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/controllers/sales_history_controller.dart';
import 'package:shared_preferences/shared_preferences.dart';

void main() {
  group('AppRouter', () {
    test('goRouterProvider returns a GoRouter instance', () {
      final container = ProviderContainer();
      final router = container.read(goRouterProvider);
      expect(router, isA<GoRouter>());
    });

    test('AppRoutes constants are defined correctly', () {
      expect(AppRoutes.root, equals('/'));
      expect(AppRoutes.login, equals('/login'));
      expect(AppRoutes.forgotPassword, equals('/forgot-password'));
      expect(AppRoutes.register, equals('/register'));
      expect(AppRoutes.dashboard, equals('/dashboard'));
      expect(AppRoutes.inventory, equals('/inventory'));
      expect(AppRoutes.inventoryBatch, equals('/inventory/batch'));
      expect(AppRoutes.inventoryBatches, equals('/inventory/batches'));
      expect(AppRoutes.inventoryAdjustments, equals('/inventory/adjustments'));
      expect(AppRoutes.salesNew, equals('/sales/new'));
      expect(AppRoutes.salesHistory, equals('/sales/history'));
      expect(AppRoutes.profitLoss, equals('/sales/profit-loss'));
      expect(AppRoutes.customers, equals('/customers'));
      expect(AppRoutes.suppliers, equals('/suppliers'));
      expect(AppRoutes.expenses, equals('/expenses'));
      expect(AppRoutes.users, equals('/users'));
      expect(AppRoutes.discounts, equals('/discounts'));
      expect(AppRoutes.bankAccounts, equals('/bank-accounts'));
      expect(AppRoutes.language, equals('/language'));
      expect(AppRoutes.placeholders, equals('/placeholder'));
    });

    test('router includes authenticated shell', () {
      final container = ProviderContainer();
      final router = container.read(goRouterProvider);

      expect(
        router.configuration.routes.any((route) => route is ShellRoute),
        isTrue,
      );
    });

    testWidgets('logout from shell redirects to login', (tester) async {
      SharedPreferences.setMockInitialValues({});

      final controller = _TestAuthController(
        AuthControllerState(session: _sessionForRole('Owner')),
      );
      final container = ProviderContainer(
        overrides: [
          authControllerProvider.overrideWith(() => controller),
          dashboardControllerProvider.overrideWith(
            _StubDashboardController.new,
          ),
          salesHistoryControllerProvider.overrideWith(
            _StubSalesHistoryController.new,
          ),
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
              GlobalMaterialLocalizations.delegate,
              GlobalWidgetsLocalizations.delegate,
              GlobalCupertinoLocalizations.delegate,
            ],
            routerConfig: router,
          ),
        ),
      );
      await tester.pump();

      expect(controller.state.value?.isAuthenticated, isTrue);

      await tester.tap(find.text('More'));
      await tester.pump();

      await tester.scrollUntilVisible(find.text('Logout'), 200);
      await tester.tap(find.text('Logout'));
      await tester.pump();

      expect(controller.signOutCalls, equals(1));
      expect(controller.state.value?.isAuthenticated, isFalse);
      expect(
        router.routeInformationProvider.value.uri.toString(),
        equals(AppRoutes.login),
      );
    });

    testWidgets('language selection returns to dashboard', (tester) async {
      SharedPreferences.setMockInitialValues({});

      final controller = _TestAuthController(
        AuthControllerState(session: _sessionForRole('Owner')),
      );
      final container = ProviderContainer(
        overrides: [
          authControllerProvider.overrideWith(() => controller),
          dashboardControllerProvider.overrideWith(
            _StubDashboardController.new,
          ),
          salesHistoryControllerProvider.overrideWith(
            _StubSalesHistoryController.new,
          ),
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
              GlobalMaterialLocalizations.delegate,
              GlobalWidgetsLocalizations.delegate,
              GlobalCupertinoLocalizations.delegate,
            ],
            routerConfig: router,
          ),
        ),
      );
      await tester.pumpAndSettle();

      await tester.tap(find.text('More'));
      await tester.pumpAndSettle();

      await tester.scrollUntilVisible(find.text('Language'), 200);
      await tester.tap(find.text('Language'));
      await tester.pumpAndSettle();

      expect(
        router.routeInformationProvider.value.uri.toString(),
        equals(AppRoutes.language),
      );

      await tester.scrollUntilVisible(find.text('Hindi'), 200);
      await tester.tap(find.text('Hindi'));
      await tester.pumpAndSettle();

      expect(
        router.routeInformationProvider.value.uri.toString(),
        equals(AppRoutes.dashboard),
      );
    });

    testWidgets('owner can navigate to discounts route', (tester) async {
      SharedPreferences.setMockInitialValues({});

      final controller = _TestAuthController(
        AuthControllerState(session: _sessionForRole('Owner')),
      );
      final container = ProviderContainer(
        overrides: [
          authControllerProvider.overrideWith(() => controller),
          dashboardControllerProvider.overrideWith(
            _StubDashboardController.new,
          ),
          salesHistoryControllerProvider.overrideWith(
            _StubSalesHistoryController.new,
          ),
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
              GlobalMaterialLocalizations.delegate,
              GlobalWidgetsLocalizations.delegate,
              GlobalCupertinoLocalizations.delegate,
            ],
            routerConfig: router,
          ),
        ),
      );
      await tester.pumpAndSettle();

      router.go(AppRoutes.discounts);
      await tester.pump();

      expect(
        router.routeInformationProvider.value.uri.toString(),
        equals(AppRoutes.discounts),
      );
    });

    testWidgets('staff is redirected from discounts route', (tester) async {
      SharedPreferences.setMockInitialValues({});

      final controller = _TestAuthController(
        AuthControllerState(session: _sessionForRole('Staff')),
      );
      final container = ProviderContainer(
        overrides: [
          authControllerProvider.overrideWith(() => controller),
          dashboardControllerProvider.overrideWith(
            _StubDashboardController.new,
          ),
          salesHistoryControllerProvider.overrideWith(
            _StubSalesHistoryController.new,
          ),
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
              GlobalMaterialLocalizations.delegate,
              GlobalWidgetsLocalizations.delegate,
              GlobalCupertinoLocalizations.delegate,
            ],
            routerConfig: router,
          ),
        ),
      );
      await tester.pump();

      router.go(AppRoutes.discounts);
      await tester.pump();

      expect(
        router.routeInformationProvider.value.uri.toString(),
        equals(AppRoutes.salesHistory),
      );
    });

    testWidgets(
      'owner deep link to discounts waits for auth bootstrap resolution',
      (tester) async {
        SharedPreferences.setMockInitialValues({});

        final completer = Completer<AuthControllerState>();
        final controller = _DelayedAuthController(completer.future);
        final container = ProviderContainer(
          overrides: [
            authControllerProvider.overrideWith(() => controller),
            dashboardControllerProvider.overrideWith(
              _StubDashboardController.new,
            ),
            salesHistoryControllerProvider.overrideWith(
              _StubSalesHistoryController.new,
            ),
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
                GlobalMaterialLocalizations.delegate,
                GlobalWidgetsLocalizations.delegate,
                GlobalCupertinoLocalizations.delegate,
              ],
              routerConfig: router,
            ),
          ),
        );

        router.go(AppRoutes.discounts);
        await tester.pump();
        completer.complete(
          AuthControllerState(session: _sessionForRole('Owner')),
        );
        await tester.pump();
        await tester.pump(const Duration(milliseconds: 100));
        await tester.pump(const Duration(milliseconds: 100));

        expect(
          router.routeInformationProvider.value.uri.toString(),
          equals(AppRoutes.discounts),
        );
      },
    );
  });
}

class _StubDashboardController extends DashboardController {
  @override
  DashboardState build() => const DashboardState();
}

class _StubSalesHistoryController extends SalesHistoryController {
  @override
  SalesHistoryState build() {
    return SalesHistoryState(
      query: SalesHistoryQuery(
        from: DateTime.utc(2026, 4, 1),
        to: DateTime.utc(2026, 5, 15, 23, 59, 59),
      ),
      sales: const [],
      summary: const SalesHistorySummary(
        periodSales: 0,
        invoiceCount: 0,
        refundAmount: 0,
      ),
      isLoading: false,
      isLoadingMore: false,
      totalCount: 0,
      pageNumber: 1,
      hasMore: false,
      searchQuery: '',
      statusFilter: 'all',
    );
  }
}

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

class _DelayedAuthController extends AuthController {
  _DelayedAuthController(this._future);

  final Future<AuthControllerState> _future;

  @override
  Future<AuthControllerState> build() => _future;
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
