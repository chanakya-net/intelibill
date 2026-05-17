import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/app/theme/app_theme.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/auth/domain/entities/auth_session.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/entities/inventory_batch.dart';
import 'package:intelibill_mobile/src/features/inventory/presentation/controllers/inventory_batches_controller.dart';
import 'package:intelibill_mobile/src/features/inventory/presentation/pages/inventory_batches_page.dart';
import 'package:intelibill_mobile/src/features/inventory/presentation/widgets/adjust_batch_sheet.dart';

class _StubInventoryBatchesController extends InventoryBatchesController {
  _StubInventoryBatchesController(this._state);

  final InventoryBatchesState _state;

  @override
  InventoryBatchesState build() => _state;
}

class _StubAuthController extends AuthController {
  _StubAuthController(this._state);

  final AuthControllerState _state;

  @override
  Future<AuthControllerState> build() async => _state;
}

final _testBatch = InventoryBatch(
  batchId: 'batch-1',
  itemId: 'item-1',
  itemName: 'Rice Premium',
  itemBarcode: 'BAR001',
  itemUom: 'kg',
  batchNumber: 'BN-001',
  quantity: 100,
  costPrice: 45,
  mrp: 60,
  salesPrice: 55,
  taxRate: 0,
  taxIncluded: false,
  isVoided: false,
  createdAt: DateTime(2026),
);

final _voidedBatch = InventoryBatch(
  batchId: 'batch-2',
  itemId: 'item-2',
  itemName: 'Wheat Flour',
  itemBarcode: 'BAR002',
  itemUom: 'kg',
  batchNumber: 'BN-002',
  quantity: 0,
  costPrice: 30,
  mrp: 40,
  salesPrice: 38,
  taxRate: 0,
  taxIncluded: false,
  isVoided: true,
  createdAt: DateTime(2026, 1, 2),
);

final _loadedState = InventoryBatchesState(batches: [_testBatch]);

Widget _buildApp({
  required InventoryBatchesState state,
  String role = 'owner',
}) {
  return ProviderScope(
    overrides: [
      inventoryBatchesControllerProvider.overrideWith(
        () => _StubInventoryBatchesController(state),
      ),
      authControllerProvider.overrideWith(
        () => _StubAuthController(AuthControllerState(session: _session(role))),
      ),
    ],
    child: MaterialApp(
      theme: AppTheme.lightTheme,
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      home: const InventoryBatchesPage(),
    ),
  );
}

void main() {
  group('InventoryBatchesPage', () {
    testWidgets('shows loading indicator when loading', (tester) async {
      await tester.pumpWidget(
        _buildApp(state: const InventoryBatchesState(isLoading: true)),
      );

      expect(find.byType(CircularProgressIndicator), findsOneWidget);
    });

    testWidgets('shows empty state when batch list is empty', (tester) async {
      await tester.pumpWidget(_buildApp(state: const InventoryBatchesState()));
      await tester.pumpAndSettle();

      expect(find.text('No batches found'), findsOneWidget);
    });

    testWidgets('shows error state with retry button', (tester) async {
      await tester.pumpWidget(
        _buildApp(
          state: const InventoryBatchesState(failure: NetworkFailure()),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.text('Unable to load batches'), findsOneWidget);
      expect(
        find.text('Unable to connect. Please check your network.'),
        findsOneWidget,
      );
      expect(find.widgetWithText(FilledButton, 'Retry'), findsOneWidget);
    });

    testWidgets('renders batch item name in batch list', (tester) async {
      await tester.pumpWidget(_buildApp(state: _loadedState));
      await tester.pumpAndSettle();

      expect(find.text('Rice Premium'), findsOneWidget);
      expect(find.text('BN-001'), findsOneWidget);
    });

    testWidgets('shows voided badge for voided batches', (tester) async {
      await tester.pumpWidget(
        _buildApp(state: InventoryBatchesState(batches: [_voidedBatch])),
      );
      await tester.pumpAndSettle();

      expect(find.text('Voided'), findsOneWidget);
    });

    testWidgets('Adjust button absent for voided batch', (tester) async {
      await tester.pumpWidget(
        _buildApp(state: InventoryBatchesState(batches: [_voidedBatch])),
      );
      await tester.pumpAndSettle();

      expect(find.widgetWithText(TextButton, 'Adjust'), findsNothing);
    });

    testWidgets('Adjust button hidden for Staff role', (tester) async {
      await tester.pumpWidget(_buildApp(state: _loadedState, role: 'staff'));
      await tester.pumpAndSettle();

      expect(find.widgetWithText(TextButton, 'Adjust'), findsNothing);
    });

    testWidgets('Adjust button visible for Owner role', (tester) async {
      await tester.pumpWidget(_buildApp(state: _loadedState));
      await tester.pumpAndSettle();

      expect(find.widgetWithText(TextButton, 'Adjust'), findsOneWidget);
    });

    testWidgets('Adjust button visible for Manager role', (tester) async {
      await tester.pumpWidget(_buildApp(state: _loadedState, role: 'manager'));
      await tester.pumpAndSettle();

      expect(find.widgetWithText(TextButton, 'Adjust'), findsOneWidget);
    });

    testWidgets('tapping Adjust opens AdjustBatchSheet', (tester) async {
      await tester.pumpWidget(_buildApp(state: _loadedState));
      await tester.pumpAndSettle();

      await tester.tap(find.widgetWithText(TextButton, 'Adjust'));
      await tester.pumpAndSettle();

      expect(find.byType(AdjustBatchSheet), findsOneWidget);
      expect(find.text('Adjust Stock'), findsOneWidget);
    });

    testWidgets('shows search field', (tester) async {
      await tester.pumpWidget(_buildApp(state: _loadedState));
      await tester.pumpAndSettle();

      expect(find.byType(TextField), findsOneWidget);
    });

    testWidgets('shows RefreshIndicator in loaded state', (tester) async {
      await tester.pumpWidget(_buildApp(state: _loadedState));
      await tester.pumpAndSettle();

      expect(find.byType(RefreshIndicator), findsOneWidget);
    });
  });
}

AuthSession _session(String role) {
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
