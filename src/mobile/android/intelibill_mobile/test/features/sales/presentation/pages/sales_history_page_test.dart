import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/app/theme/app_theme.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/auth/domain/entities/auth_session.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_detail.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_list_item.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sales_history_summary.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/controllers/sale_detail_controller.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/controllers/sales_history_controller.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/pages/sales_history_page.dart';

class _StubSalesHistoryController extends SalesHistoryController {
  _StubSalesHistoryController(this._state);

  final SalesHistoryState _state;

  @override
  SalesHistoryState build() => _state;
}

class _StubAuthController extends AuthController {
  _StubAuthController(this._state);

  final AuthControllerState _state;

  @override
  Future<AuthControllerState> build() async => _state;
}

final _sampleSale = SaleListItem(
  saleId: 'sale-1',
  invoiceNumber: 'INV-2026-001',
  customerId: null,
  paymentMethod: 1,
  soldAt: DateTime.utc(2026, 5, 11, 10, 30),
  paidAmount: 500,
  dueAmount: 0,
  totalBeforeDiscount: 500,
  totalDiscountAmount: 0,
  totalAmount: 500,
  totalTaxAmount: 50,
  customerName: 'John Doe',
  customerPhone: '9999999999',
  itemCount: 2,
  returnNumbers: const [],
  status: 'paid',
  refundAmount: 0,
  dueReductionAmount: 0,
);

final _sampleDetail = SaleDetail(
  saleId: 'sale-1',
  invoiceNumber: 'INV-2026-001',
  customerName: 'John Doe',
  customerPhone: '9999999999',
  paymentMethod: 1,
  soldAt: DateTime.utc(2026, 5, 11, 10, 30),
  paidAmount: 500,
  dueAmount: 0,
  totalBeforeDiscount: 500,
  totalDiscountAmount: 0,
  totalAmount: 500,
  totalTaxAmount: 50,
  status: 'paid',
);

AuthSession _session({String role = 'Staff'}) {
  return AuthSession(
    accessToken: 'access-token',
    refreshToken: 'refresh-token',
    accessTokenExpiresAt: DateTime.utc(2026, 5, 15, 10),
    refreshTokenExpiresAt: DateTime.utc(2026, 6, 15, 10),
    user: const AuthUser(
      id: 'user-1',
      email: 'staff@example.com',
      phoneNumber: null,
      firstName: 'Sam',
      lastName: 'Staff',
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

Widget _buildApp(SalesHistoryState state, {String role = 'Staff'}) {
  return ProviderScope(
    overrides: [
      salesHistoryControllerProvider.overrideWith(
        () => _StubSalesHistoryController(state),
      ),
      saleDetailControllerProvider('sale-1').overrideWithValue(
        SaleDetailState(detail: _sampleDetail),
      ),
      authControllerProvider.overrideWith(
        () => _StubAuthController(
          AuthControllerState(session: _session(role: role)),
        ),
      ),
    ],
    child: MaterialApp(
      theme: AppTheme.lightTheme,
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      home: const SalesHistoryPage(),
    ),
  );
}

void main() {
  group('SalesHistoryPage', () {
    testWidgets('shows loading indicator when loading', (tester) async {
      await tester.pumpWidget(
        _buildApp(
          SalesHistoryState(
            isLoading: true,
            query: defaultSalesHistoryQuery(),
          ),
        ),
      );

      expect(find.byType(CircularProgressIndicator), findsOneWidget);
    });

    testWidgets('shows KPI row and sale cards when loaded', (tester) async {
      await tester.pumpWidget(
        _buildApp(
          SalesHistoryState(
            sales: [_sampleSale],
            summary: const SalesHistorySummary(
              periodSales: 500,
              invoiceCount: 1,
              refundAmount: 0,
            ),
            query: defaultSalesHistoryQuery(),
            totalCount: 1,
          ),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.text('Period sales'), findsOneWidget);
      expect(find.text('INV-2026-001'), findsOneWidget);
      expect(find.text('John Doe'), findsOneWidget);
    });

    testWidgets('opens detail sheet when sale card is tapped', (tester) async {
      await tester.pumpWidget(
        _buildApp(
          SalesHistoryState(
            sales: [_sampleSale],
            summary: const SalesHistorySummary(
              periodSales: 500,
              invoiceCount: 1,
              refundAmount: 0,
            ),
            query: defaultSalesHistoryQuery(),
            totalCount: 1,
          ),
        ),
      );
      await tester.pumpAndSettle();

      await tester.tap(find.text('INV-2026-001'));
      await tester.pumpAndSettle();

      expect(find.text('Sale details'), findsOneWidget);
      expect(find.text('Line items'), findsOneWidget);
      expect(find.text('Totals'), findsOneWidget);
    });

    testWidgets('shows new sale FAB for staff, managers, and owners', (
      tester,
    ) async {
      for (final role in ['Staff', 'Manager', 'Owner']) {
        await tester.pumpWidget(
          _buildApp(
            SalesHistoryState(
              sales: [_sampleSale],
              query: defaultSalesHistoryQuery(),
              totalCount: 1,
            ),
            role: role,
          ),
        );
        await tester.pumpAndSettle();
        expect(
          find.byKey(SalesHistoryPage.newSaleFabKey),
          findsOneWidget,
          reason: 'FAB should show for $role',
        );
        await tester.pumpWidget(const SizedBox());
      }
    });

    testWidgets('new sale FAB navigates to create route', (tester) async {
      final router = GoRouter(
        initialLocation: AppRoutes.salesHistory,
        routes: [
          GoRoute(
            path: AppRoutes.salesHistory,
            builder: (_, _) => const SalesHistoryPage(),
          ),
          GoRoute(
            path: AppRoutes.salesNew,
            builder: (_, _) => const Text('new-sale-page'),
          ),
          GoRoute(
            path: AppRoutes.profile,
            builder: (_, _) => const Text('profile-page'),
          ),
        ],
      );

      await tester.pumpWidget(
        ProviderScope(
          overrides: [
            salesHistoryControllerProvider.overrideWith(
              () => _StubSalesHistoryController(
                SalesHistoryState(
                  sales: [_sampleSale],
                  query: defaultSalesHistoryQuery(),
                  totalCount: 1,
                ),
              ),
            ),
            authControllerProvider.overrideWith(
              () => _StubAuthController(
                AuthControllerState(session: _session()),
              ),
            ),
          ],
          child: MaterialApp.router(
            theme: AppTheme.lightTheme,
            localizationsDelegates: AppLocalizations.localizationsDelegates,
            supportedLocales: AppLocalizations.supportedLocales,
            routerConfig: router,
          ),
        ),
      );
      await tester.pumpAndSettle();

      tester
          .widget<FloatingActionButton>(
            find.byKey(SalesHistoryPage.newSaleFabKey),
          )
          .onPressed!();
      await tester.pumpAndSettle();

      expect(
        router.routerDelegate.currentConfiguration.uri.path,
        AppRoutes.salesNew,
      );
      expect(find.text('new-sale-page'), findsOneWidget);
    });
  });
}
