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
import 'package:intelibill_mobile/src/features/credit_notes/domain/entities/credit_note_print.dart';
import 'package:intelibill_mobile/src/features/credit_notes/presentation/controllers/credit_notes_controller.dart';
import 'package:intelibill_mobile/src/features/dashboard/presentation/controllers/dashboard_controller.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_detail.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sales_history_query.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sales_history_summary.dart';
import 'package:intelibill_mobile/src/features/sales/domain/use_cases/get_sale_detail.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/controllers/sales_history_controller.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/controllers/sales_providers.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:mocktail/mocktail.dart';

class MockGetSaleDetail extends Mock implements GetSaleDetail {}

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
      expect(AppRoutes.salesReceipt, equals('/sales/:saleId/receipt'));
      expect(AppRoutes.profitLoss, equals('/sales/profit-loss'));
      expect(AppRoutes.customers, equals('/customers'));
      expect(AppRoutes.suppliers, equals('/suppliers'));
      expect(AppRoutes.creditNoteReceipt, equals('/credit-notes/:code/print'));
      expect(AppRoutes.services, equals('/services'));
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

    testWidgets('opens credit note receipt route', (tester) async {
      final container = ProviderContainer(
        overrides: [
          authControllerProvider.overrideWith(
            () => _TestAuthController(
              AuthControllerState(session: _sessionForRole('Owner')),
            ),
          ),
          dashboardControllerProvider.overrideWith(
            _StubDashboardController.new,
          ),
          salesHistoryControllerProvider.overrideWith(
            _StubSalesHistoryController.new,
          ),
          creditNotePrintByCodeProvider.overrideWith(
            (ref, _) => Future.value(_fakeCreditNotePrint),
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

      final route = AppRoutes.creditNoteReceiptFor('CN-001');
      router.go(route);
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 100));

      expect(
        router.routeInformationProvider.value.uri.toString(),
        equals(route),
      );
    });

    testWidgets('owner can navigate to sales receipt route', (tester) async {
      final controller = _TestAuthController(
        AuthControllerState(session: _sessionForRole('Owner')),
      );
      final getSaleDetail = MockGetSaleDetail();
      when(() => getSaleDetail(any())).thenAnswer((_) async => _fakeSaleDetail);
      final container = ProviderContainer(
        overrides: [
          authControllerProvider.overrideWith(() => controller),
          dashboardControllerProvider.overrideWith(
            _StubDashboardController.new,
          ),
          salesHistoryControllerProvider.overrideWith(
            _StubSalesHistoryController.new,
          ),
          getSaleDetailUseCaseProvider.overrideWithValue(getSaleDetail),
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

      final route = AppRoutes.salesReceiptFor('sale-100');
      router.go(route);
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 100));

      expect(
        router.routeInformationProvider.value.uri.toString(),
        equals(route),
      );
      expect(find.text('INV-REC-001'), findsOneWidget);
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

    testWidgets('staff is redirected from services route', (tester) async {
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

      router.go(AppRoutes.services);
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

CreditNotePrint _fakeCreditNotePrint = CreditNotePrint(
  creditNoteId: 'cn-print',
  code: 'CN-PRINT',
  status: 'active',
  isUsable: true,
  originalAmount: 1000,
  availableBalance: 1000,
  issuedAt: DateTime.utc(2026, 6, 18, 10),
  expiresAt: DateTime.utc(2026, 7, 1, 10),
  saleId: 'sale-1',
  invoiceNumber: 'INV-001',
  saleReturnId: 'ret-1',
  returnNumber: 'RET-001',
  customerDisplayName: 'John Doe',
  reason: 'Damaged',
  voidReason: null,
);

SaleDetail _fakeSaleDetail = SaleDetail(
  saleId: 'sale-100',
  invoiceNumber: 'INV-REC-001',
  paymentMethod: 1,
  soldAt: DateTime.utc(2026, 6, 10, 10),
  paidAmount: 300,
  dueAmount: 0,
  totalBeforeDiscount: 300,
  totalDiscountAmount: 0,
  totalAmount: 300,
  totalTaxAmount: 0,
  customerName: 'Alice',
  customerPhone: '9999999999',
  items: const [
    SaleDetailItem(
      saleItemId: 'item-1',
      lineType: 'Goods',
      lineCode: 'NB-1',
      itemName: 'Notebook',
      quantity: 2,
      salesPrice: 50,
      originalSalesPrice: 50,
      finalSalesPrice: 50,
      preTaxAmountBeforeDiscount: 100,
      itemDiscountAmount: 0,
      saleDiscountAmount: 0,
      taxableAmount: 100,
      taxAmount: 0,
      totalAmount: 100,
      savingsAmount: 0,
      taxRatePercent: 0,
      isPriceIncludingTax: false,
      hasPriceMismatch: false,
      returnedQuantity: 0,
      returnableQuantity: 0,
      returnStatus: 'none',
    ),
  ],
  settlements: [
    SaleDetailSettlement(
      settlementId: 'settlement-1',
      method: 'Cash',
      amount: 300,
      settledAt: DateTime.utc(2026, 6, 10, 10),
    ),
  ],
  discounts: const [
    SaleDetailDiscount(
      discountId: 'discount-1',
      type: 'Flat',
      value: '₹20',
      amount: 0,
    ),
  ],
  returns: const [],
  creditNoteRedemptions: const [],
  warnings: const [],
);

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
