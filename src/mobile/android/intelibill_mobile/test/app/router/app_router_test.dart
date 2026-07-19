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
import 'package:intelibill_mobile/src/features/bank_accounts/domain/use_cases/get_bank_accounts.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/presentation/controllers/bank_accounts_controller.dart';
import 'package:intelibill_mobile/src/features/credit_notes/domain/entities/credit_note_print.dart';
import 'package:intelibill_mobile/src/features/credit_notes/presentation/controllers/credit_notes_controller.dart';
import 'package:intelibill_mobile/src/features/dashboard/presentation/controllers/dashboard_controller.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount_rule_query.dart';
import 'package:intelibill_mobile/src/features/discounts/presentation/controllers/discounts_controller.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expense_list_item.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expenses_page.dart';
import 'package:intelibill_mobile/src/features/expenses/presentation/controllers/expenses_controller.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_line.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_status.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/get_purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_providers.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_orders_controller.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/pages/purchase_orders_page.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_detail.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sales_history_query.dart';
import 'package:intelibill_mobile/src/features/sales/domain/use_cases/get_sale_detail.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/controllers/new_sale_controller.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/controllers/sales_history_controller.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/controllers/sales_providers.dart';
import 'package:intelibill_mobile/src/features/services/presentation/controllers/services_controller.dart';
import 'package:intelibill_mobile/src/shared/documents/document_page_format.dart';
import 'package:intelibill_mobile/src/shared/documents/document_preview_scaffold.dart';
import 'package:mocktail/mocktail.dart';
import 'package:pdf/pdf.dart';
import 'package:shared_preferences/shared_preferences.dart';

class MockRouterGetBankAccounts extends Mock implements GetBankAccounts {}

class MockGetSaleDetail extends Mock implements GetSaleDetail {}

class _MockGetPurchaseOrder extends Mock implements GetPurchaseOrder {}

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
      expect(AppRoutes.creditNotes, equals('/credit-notes'));
      expect(AppRoutes.suppliers, equals('/suppliers'));
      expect(AppRoutes.creditNoteReceipt, equals('/credit-notes/:code/print'));
      expect(AppRoutes.services, equals('/services'));
      expect(AppRoutes.expenses, equals('/expenses'));
      expect(AppRoutes.users, equals('/users'));
      expect(AppRoutes.discounts, equals('/discounts'));
      expect(AppRoutes.bankAccounts, equals('/bank-accounts'));
      expect(AppRoutes.purchaseOrders, equals('/inventory/purchase-orders'));
      expect(AppRoutes.language, equals('/language'));
      expect(AppRoutes.placeholders, equals('/placeholder'));
    });

    test('AppRoutes purchase-order detail route constants are valid', () {
      expect(
        AppRoutes.purchaseOrderDetail,
        equals('/inventory/purchase-orders/:purchaseOrderId'),
      );
      expect(
        AppRoutes.purchaseOrderDetailFor('po-1'),
        equals('/inventory/purchase-orders/po-1'),
      );
      expect(
        AppRoutes.purchaseOrderPrint,
        equals('/inventory/purchase-orders/:purchaseOrderId/print'),
      );
      expect(
        AppRoutes.purchaseOrderPrintFor('po-1'),
        equals('/inventory/purchase-orders/po-1/print'),
      );
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
          discountsControllerProvider.overrideWith(
            _StubDiscountsController.new,
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
      final requestedCodes = <String>[];
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
            (ref, code) {
              requestedCodes.add(code);
              return Future.value(_fakeCreditNotePrint);
            },
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
      expect(requestedCodes, ['CN-001']);
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
      expect(find.byType(DocumentPreviewScaffold), findsOneWidget);
      final scaffold = tester.widget<DocumentPreviewScaffold>(
        find.byType(DocumentPreviewScaffold),
      );
      expect(scaffold.descriptor.pageFormat, DocumentPageFormat.mm80);
      expect(scaffold.descriptor.title, 'Receipt');
      expect(scaffold.descriptor.filename, 'sale-receipt-INV-REC-001.pdf');

      final bytes = await scaffold.onBuild(PdfPageFormat.roll80);
      expect(bytes.take(4), orderedEquals('%PDF'.codeUnits));
    });

    testWidgets(
      'owner can navigate to sales receipt route with initialSale extra',
      (tester) async {
        final getSaleDetail = MockGetSaleDetail();
        final completer = Completer<SaleDetail>();
        when(() => getSaleDetail(any())).thenAnswer((_) => completer.future);

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
        router.go(route, extra: _fakeSaleDetailFromInitialExtra);
        await tester.pump();
        await tester.pump(const Duration(milliseconds: 100));

        expect(
          router.routeInformationProvider.value.uri.toString(),
          equals(route),
        );
        expect(find.byType(DocumentPreviewScaffold), findsOneWidget);

        final scaffoldBefore = tester.widget<DocumentPreviewScaffold>(
          find.byType(DocumentPreviewScaffold),
        );
        expect(
          scaffoldBefore.descriptor.filename,
          'sale-receipt-INV-INIT-001.pdf',
        );

        completer.complete(_fakeSaleDetail);
        await tester.pump();
        await tester.pump(const Duration(milliseconds: 100));

        final scaffoldAfter = tester.widget<DocumentPreviewScaffold>(
          find.byType(DocumentPreviewScaffold),
        );
        expect(
          scaffoldAfter.descriptor.filename,
          'sale-receipt-INV-REC-001.pdf',
        );
      },
    );

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

    testWidgets('owner can navigate to bank accounts route', (tester) async {
      SharedPreferences.setMockInitialValues({});
      final getBankAccounts = MockRouterGetBankAccounts();
      when(getBankAccounts.call).thenAnswer((_) async => []);
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
          getBankAccountsUseCaseProvider.overrideWithValue(getBankAccounts),
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

      router.go(AppRoutes.bankAccounts);
      await tester.pumpAndSettle();

      expect(
        router.routeInformationProvider.value.uri.toString(),
        AppRoutes.bankAccounts,
      );
      expect(find.text('Bank Accounts'), findsOneWidget);
    });

    for (final role in ['Manager', 'Staff']) {
      testWidgets('$role is redirected from bank accounts route', (
        tester,
      ) async {
        SharedPreferences.setMockInitialValues({});
        final container = ProviderContainer(
          overrides: [
            authControllerProvider.overrideWith(
              () => _TestAuthController(
                AuthControllerState(session: _sessionForRole(role)),
              ),
            ),
            dashboardControllerProvider.overrideWith(
              _StubDashboardController.new,
            ),
            salesHistoryControllerProvider.overrideWith(
              _StubSalesHistoryController.new,
            ),
            purchaseOrdersControllerProvider.overrideWith(
              _StubPurchaseOrdersController.new,
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

        router.go(AppRoutes.bankAccounts);
        await tester.pump();

        expect(
          router.routeInformationProvider.value.uri.toString(),
          AppRoutes.salesHistory,
        );
      });
    }

    for (final role in ['Owner', 'Manager']) {
      testWidgets('$role can navigate to expenses and render first page', (
        tester,
      ) async {
        SharedPreferences.setMockInitialValues({});
        final container = ProviderContainer(
          overrides: [
            authControllerProvider.overrideWith(
              () => _TestAuthController(
                AuthControllerState(session: _sessionForRole(role)),
              ),
            ),
            dashboardControllerProvider.overrideWith(
              _StubDashboardController.new,
            ),
            salesHistoryControllerProvider.overrideWith(
              _StubSalesHistoryController.new,
            ),
            purchaseOrdersControllerProvider.overrideWith(
              _StubPurchaseOrdersController.new,
            ),
            expensesControllerProvider.overrideWith(
              _StubExpensesController.new,
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

        router.go(AppRoutes.expenses);
        await tester.pumpAndSettle();

        expect(
          router.routeInformationProvider.value.uri.toString(),
          AppRoutes.expenses,
        );
        expect(find.text('Travel'), findsOneWidget);
        expect(find.text('Taxi'), findsOneWidget);
      });
    }

    testWidgets('staff is redirected from expenses route', (tester) async {
      SharedPreferences.setMockInitialValues({});
      final container = ProviderContainer(
        overrides: [
          authControllerProvider.overrideWith(
            () => _TestAuthController(
              AuthControllerState(session: _sessionForRole('Staff')),
            ),
          ),
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

      router.go(AppRoutes.expenses);
      await tester.pump();

      expect(
        router.routeInformationProvider.value.uri.toString(),
        AppRoutes.salesHistory,
      );
    });

    for (final role in ['Owner', 'Manager']) {
      testWidgets('$role can navigate to purchase orders route', (
        tester,
      ) async {
        final container = ProviderContainer(
          overrides: [
            authControllerProvider.overrideWith(
              () => _TestAuthController(
                AuthControllerState(session: _sessionForRole(role)),
              ),
            ),
            dashboardControllerProvider.overrideWith(
              _StubDashboardController.new,
            ),
            salesHistoryControllerProvider.overrideWith(
              _StubSalesHistoryController.new,
            ),
            purchaseOrdersControllerProvider.overrideWith(
              _StubPurchaseOrdersController.new,
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

        router.go(AppRoutes.purchaseOrders);
        await tester.pumpAndSettle();

        expect(
          router.routeInformationProvider.value.uri.toString(),
          AppRoutes.purchaseOrders,
        );
        expect(find.byKey(PurchaseOrdersPage.pageKey), findsOneWidget);
      });
    }

    for (final role in ['Owner']) {
      testWidgets('$role can deep-link to purchase order detail route', (
        tester,
      ) async {
        final getPurchaseOrder = _MockGetPurchaseOrder();
        when(
          () => getPurchaseOrder(any()),
        ).thenAnswer((_) async => _fakePurchaseOrder);

        final container = ProviderContainer(
          overrides: [
            authControllerProvider.overrideWith(
              () => _TestAuthController(
                AuthControllerState(session: _sessionForRole(role)),
              ),
            ),
            getPurchaseOrderProvider.overrideWithValue(getPurchaseOrder),
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

        final detailRoute = AppRoutes.purchaseOrderDetailFor('po-1');
        router.go(detailRoute);
        await tester.pump();

        expect(
          router.routeInformationProvider.value.uri.toString(),
          equals(detailRoute),
        );
      });
    }

    for (final role in ['Staff', 'Owner']) {
      testWidgets(
        '$role without purchase-order access is redirected from nested route',
        (tester) async {
          final session = role == 'Owner'
              ? _sessionForRole('Owner', shops: [])
              : _sessionForRole('Staff');
          final container = ProviderContainer(
            overrides: [
              authControllerProvider.overrideWith(
                () => _TestAuthController(
                  AuthControllerState(session: session),
                ),
              ),
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

          router.go(AppRoutes.purchaseOrderDetailFor('po-1'));
          await tester.pump();

          expect(
            router.routeInformationProvider.value.uri.toString(),
            AppRoutes.salesHistory,
          );
        },
      );
    }

    for (final role in ['Owner', 'Manager']) {
      testWidgets(
        '$role without an active shop is redirected from purchase-order routes',
        (tester) async {
          final container = ProviderContainer(
            overrides: [
              authControllerProvider.overrideWith(
                () => _TestAuthController(
                  AuthControllerState(
                    session: _sessionForRole(role, activeShopId: null),
                  ),
                ),
              ),
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

          for (final route in [
            AppRoutes.purchaseOrders,
            AppRoutes.purchaseOrderDetailFor('po-1'),
          ]) {
            router.go(route);
            await tester.pump();

            expect(
              router.routeInformationProvider.value.uri.toString(),
              AppRoutes.salesHistory,
            );
          }
        },
      );
    }

    testWidgets('owner can navigate through sales-flow and management routes', (
      tester,
    ) async {
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
          newSaleControllerProvider.overrideWith(_StubNewSaleController.new),
          creditNotesControllerProvider.overrideWith(
            _StubCreditNotesController.new,
          ),
          servicesControllerProvider.overrideWith(_StubServicesController.new),
          discountsControllerProvider.overrideWith(
            _StubDiscountsController.new,
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

      for (final route in [
        AppRoutes.salesNew,
        AppRoutes.salesHistory,
        AppRoutes.creditNotes,
        AppRoutes.services,
        AppRoutes.discounts,
      ]) {
        router.go(route);
        await tester.pumpAndSettle();
        expect(
          router.routeInformationProvider.value.uri.toString(),
          equals(route),
        );
      }
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

PurchaseOrder _fakePurchaseOrder = PurchaseOrder(
  purchaseOrderId: 'po-1',
  purchaseOrderNumber: 'PO-po-1',
  status: PurchaseOrderStatus.placed,
  supplierId: 'supplier-1',
  orderDate: DateTime(2026, 7, 11),
  expectedDeliveryDate: DateTime(2026, 7, 14),
  supplierReferenceNumber: 'SRN-77',
  notes: 'Urgent',
  lines: const [
    PurchaseOrderLine(
      lineId: 'line-1',
      itemId: 'item-1',
      description: 'Widget A',
      expectedQuantity: 10,
      receivedQuantity: 5,
      remainingQuantity: 5,
      unitCost: 75,
      lineTotal: 750,
    ),
  ],
  expectedTotal: 750,
  createdAt: DateTime(2026, 7, 1, 8, 30),
  supplierName: 'Fresh Grocers',
  supplierReference: 'RG-77',
  receivedQuantity: 7,
);

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
);

SaleDetail _fakeSaleDetailFromInitialExtra = SaleDetail(
  saleId: 'sale-100',
  invoiceNumber: 'INV-INIT-001',
  paymentMethod: 1,
  soldAt: DateTime(2026, 6, 10, 10),
  paidAmount: 300,
  dueAmount: 0,
  totalBeforeDiscount: 100,
  totalDiscountAmount: 0,
  totalAmount: 100,
  totalTaxAmount: 0,
  customerName: 'Alice',
  customerPhone: '9999999999',
  items: const [
    SaleDetailItem(
      saleItemId: 'item-initial',
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
  status: 'paid',
);

class _StubDashboardController extends DashboardController {
  @override
  DashboardState build() => const DashboardState();
}

class _StubNewSaleController extends NewSaleController {
  @override
  NewSaleState build() => const NewSaleState();
}

class _StubCreditNotesController extends CreditNotesController {
  @override
  CreditNotesState build() => const CreditNotesState();
}

class _StubSalesHistoryController extends SalesHistoryController {
  @override
  SalesHistoryState build() {
    return SalesHistoryState(
      query: SalesHistoryQuery(
        from: DateTime.utc(2026, 4),
        to: DateTime.utc(2026, 5, 15, 23, 59, 59),
      ),
    );
  }
}

class _StubPurchaseOrdersController extends PurchaseOrdersController {
  @override
  PurchaseOrdersState build() => const PurchaseOrdersState();
}

class _StubServicesController extends ServicesController {
  @override
  ServicesState build() => const ServicesState();
}

class _StubDiscountsController extends DiscountsController {
  @override
  DiscountsState build() {
    return const DiscountsState(
      query: DiscountRulesQuery(),
    );
  }
}

class _StubExpensesController extends ExpensesController {
  @override
  ExpensesState build() {
    return ExpensesState(
      page: ExpensePage(
        items: [
          ExpenseListItem(
            id: 'expense-1',
            amount: 300,
            categoryName: 'Travel',
            paidTo: 'Taxi',
            expenseDate: DateTime(2026, 7),
            isVoided: false,
          ),
        ],
        totalCount: 1,
        pageNumber: 1,
        pageSize: 20,
      ),
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
  String? activeShopId = 'shop-1',
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
