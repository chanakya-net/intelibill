import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/app/theme/app_theme.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/auth/domain/entities/auth_session.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:intelibill_mobile/src/features/dashboard/presentation/controllers/dashboard_controller.dart';
import 'package:intelibill_mobile/src/features/dashboard/presentation/pages/dashboard_page.dart';
import 'package:intelibill_mobile/src/features/dashboard/presentation/widgets/dashboard_alerts_card.dart';
import 'package:intelibill_mobile/src/features/dashboard/presentation/widgets/dashboard_kpi_grid.dart';
import 'package:intelibill_mobile/src/features/dashboard/presentation/widgets/dashboard_period_selector.dart';
import 'package:intelibill_mobile/src/features/dashboard/presentation/widgets/dashboard_quick_actions.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/pages/purchase_orders_page.dart';

import '../../dashboard_test_fixtures.dart';

class _StubDashboardController extends DashboardController {
  _StubDashboardController(this._state);

  final DashboardState _state;

  @override
  DashboardState build() => _state;
}

class _StubAuthController extends AuthController {
  _StubAuthController(this._state);

  final AuthControllerState _state;

  @override
  Future<AuthControllerState> build() async => _state;
}

Widget _buildApp({
  required DashboardState dashboardState,
  required AuthSession session,
}) {
  return ProviderScope(
    overrides: [
      dashboardControllerProvider.overrideWith(
        () => _StubDashboardController(dashboardState),
      ),
      authControllerProvider.overrideWith(
        () => _StubAuthController(AuthControllerState(session: session)),
      ),
    ],
    child: MaterialApp(
      theme: AppTheme.lightTheme,
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      home: const DashboardPage(),
    ),
  );
}

Widget _buildRouterApp({
  required ProviderContainer container,
  required GoRouter router,
}) {
  return UncontrolledProviderScope(
    container: container,
    child: MaterialApp.router(
      theme: AppTheme.lightTheme,
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      routerConfig: router,
    ),
  );
}

void main() {
  group('DashboardPage', () {
    setUp(TestWidgetsFlutterBinding.ensureInitialized);

    testWidgets('shows loading indicator when dashboard is loading', (
      tester,
    ) async {
      await tester.pumpWidget(
        _buildApp(
          dashboardState: const DashboardState(isLoading: true),
          session: _ownerSession(),
        ),
      );
      await tester.pump();

      expect(find.byType(CircularProgressIndicator), findsOneWidget);
    });

    testWidgets('does not render dashboard content for staff', (tester) async {
      await tester.pumpWidget(
        _buildApp(
          dashboardState: const DashboardState(),
          session: _ownerSession(role: 'Staff'),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.byType(DashboardKpiGrid), findsNothing);
      expect(find.byType(DashboardPeriodSelector), findsNothing);
      expect(find.byType(CircularProgressIndicator), findsNothing);
    });

    testWidgets('shows error state with retry for owners', (tester) async {
      await tester.pumpWidget(
        _buildApp(
          dashboardState: const DashboardState(
            failure: NetworkFailure(),
          ),
          session: _ownerSession(),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.text('Unable to load dashboard'), findsOneWidget);
      expect(find.widgetWithText(FilledButton, 'Retry'), findsOneWidget);
    });

    testWidgets('renders dashboard sections when data is loaded', (
      tester,
    ) async {
      tester.view.physicalSize = const Size(1080, 2400);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);

      await tester.pumpWidget(
        _buildApp(
          dashboardState: DashboardState(dashboard: testDashboard),
          session: _ownerSession(),
        ),
      );
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 100));

      expect(find.textContaining('Primary Shop'), findsOneWidget);
      expect(find.byType(DashboardPeriodSelector), findsOneWidget);
      expect(find.byType(DashboardKpiGrid), findsOneWidget);
      expect(find.text('INV-001'), findsOneWidget);
      expect(find.text('Walk-in Customer'), findsOneWidget);

      await tester.drag(find.byType(ListView), const Offset(0, -600));
      await tester.pump();

      expect(find.text('2 items are running low.'), findsOneWidget);
      expect(find.text('Manage inventory'), findsOneWidget);
      expect(find.byType(DashboardAlertsCard), findsOneWidget);
      expect(find.byType(DashboardQuickActions), findsOneWidget);
    });

    testWidgets('shows RefreshIndicator when dashboard is loaded', (
      tester,
    ) async {
      tester.view.physicalSize = const Size(1080, 2400);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);

      await tester.pumpWidget(
        _buildApp(
          dashboardState: DashboardState(dashboard: testDashboard),
          session: _ownerSession(),
        ),
      );
      await tester.pump();

      expect(find.byType(RefreshIndicator), findsOneWidget);
    });

    testWidgets('shows no-data message when dashboard is null', (tester) async {
      await tester.pumpWidget(
        _buildApp(
          dashboardState: const DashboardState(),
          session: _ownerSession(),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.text('No dashboard data available.'), findsOneWidget);
    });

    testWidgets('navigates pending purchase order alert to directory', (
      tester,
    ) async {
      tester.view.physicalSize = const Size(1080, 2400);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);

      final container = ProviderContainer(
        overrides: [
          dashboardControllerProvider.overrideWith(
            () => _StubDashboardController(
              DashboardState(dashboard: testDashboardWithPO),
            ),
          ),
          authControllerProvider.overrideWith(
            () => _StubAuthController(
              AuthControllerState(session: _ownerSession()),
            ),
          ),
        ],
      );
      addTearDown(container.dispose);
      final router = container.read(goRouterProvider);
      addTearDown(router.dispose);

      await tester.pumpWidget(
        _buildRouterApp(
          container: container,
          router: router,
        ),
      );
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 100));

      await tester.drag(find.byType(ListView), const Offset(0, -600));
      await tester.pump();

      expect(find.text('Pending Purchase Order'), findsOneWidget);
      expect(find.text('PO-001 awaiting approval.'), findsOneWidget);

      final reviewButton = find.widgetWithText(TextButton, 'Review');
      await tester.ensureVisible(reviewButton);
      await tester.tap(reviewButton);
      await tester.pumpAndSettle();

      expect(
        router.state.uri.toString(),
        AppRoutes.purchaseOrders,
      );
      expect(find.byKey(PurchaseOrdersPage.pageKey), findsOneWidget);
    });
  });
}

AuthSession _ownerSession({String role = 'Owner'}) {
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
    activeShopId: 'shop-1',
    shops: [
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
