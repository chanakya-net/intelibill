import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/app/theme/app_theme.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/auth/domain/entities/auth_session.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/entities/item.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/use_cases/create_item.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/use_cases/get_items.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/use_cases/update_item.dart';
import 'package:intelibill_mobile/src/features/inventory/presentation/controllers/items_controller.dart';
import 'package:intelibill_mobile/src/features/inventory/presentation/pages/items_page.dart';
import 'package:intelibill_mobile/src/features/inventory/presentation/widgets/create_item_sheet.dart';
import 'package:intelibill_mobile/src/features/inventory/presentation/widgets/edit_item_sheet.dart';
import 'package:intelibill_mobile/src/shared/barcode_scanner/barcode_scan_result.dart';
import 'package:mocktail/mocktail.dart';

class _StubItemsController extends ItemsController {
  _StubItemsController(this._state);

  final ItemsState _state;

  @override
  ItemsState build() => _state;
}

class _StubAuthController extends AuthController {
  _StubAuthController(this._state);

  final AuthControllerState _state;

  @override
  Future<AuthControllerState> build() async => _state;
}

class MockGetItems extends Mock implements GetItems {}

class MockCreateItem extends Mock implements CreateItem {}

class MockUpdateItem extends Mock implements UpdateItem {}

class MockNavigatorObserver extends Mock implements NavigatorObserver {}

const _loadedState = ItemsState(
  items: [
    Item(
      itemId: 'item-1',
      name: 'Widget Alpha',
      barcode: 'BAR001',
      uom: 'pcs',
      isActive: true,
      currentStock: 100,
    ),
    Item(
      itemId: 'item-2',
      name: 'Widget Beta',
      barcode: 'BAR002',
      uom: 'kg',
      isActive: false,
      currentStock: 4,
    ),
  ],
);

Widget _buildApp({
  required ItemsState state,
  String role = 'Owner',
  Locale? locale,
}) {
  return ProviderScope(
    overrides: [
      itemsControllerProvider.overrideWith(() => _StubItemsController(state)),
      authControllerProvider.overrideWith(
        () => _StubAuthController(AuthControllerState(session: _session(role))),
      ),
    ],
    child: MaterialApp(
      locale: locale,
      theme: AppTheme.lightTheme,
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      home: const ItemsPage(),
    ),
  );
}

Widget _buildManageFlowApp({
  required AuthSession session,
  required MockGetItems getItems,
  required MockCreateItem createItem,
  required MockUpdateItem updateItem,
  NavigatorObserver? navigatorObserver,
}) {
  return ProviderScope(
    overrides: [
      getItemsProvider.overrideWithValue(getItems),
      createItemProvider.overrideWithValue(createItem),
      updateItemProvider.overrideWithValue(updateItem),
      authControllerProvider.overrideWith(
        () => _StubAuthController(AuthControllerState(session: session)),
      ),
    ],
    child: MaterialApp(
      theme: AppTheme.lightTheme,
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      navigatorObservers: navigatorObserver != null ? [navigatorObserver] : [],
      home: const ItemsPage(),
    ),
  );
}

Future<void> _expandInventorySpeedDial(WidgetTester tester) async {
  await tester.tap(find.byKey(ItemsPage.speedDialMainKey));
  await tester.pumpAndSettle();
}

Future<void> _tapAddProductAction(WidgetTester tester) async {
  await _expandInventorySpeedDial(tester);
  await tester.tap(find.byKey(ItemsPage.addProductActionKey));
  await tester.pumpAndSettle();
}

void main() {
  setUpAll(() {
    registerFallbackValue(
      MaterialPageRoute<BarcodeScanResult?>(builder: (_) => const SizedBox()),
    );
  });

  group('ItemsPage', () {
    testWidgets('shows loading indicator when loading', (tester) async {
      await tester.pumpWidget(
        _buildApp(state: const ItemsState(isLoading: true)),
      );

      expect(find.byType(CircularProgressIndicator), findsOneWidget);
    });

    testWidgets('shows empty state when list empty', (tester) async {
      await tester.pumpWidget(_buildApp(state: const ItemsState()));
      await tester.pumpAndSettle();

      expect(find.text('No products found'), findsOneWidget);
    });

    testWidgets('shows error state with retry action', (tester) async {
      await tester.pumpWidget(
        _buildApp(state: const ItemsState(failure: NetworkFailure())),
      );
      await tester.pumpAndSettle();

      expect(find.text('Unable to load products'), findsOneWidget);
      expect(
        find.text('Unable to connect. Please check your network.'),
        findsOneWidget,
      );
      expect(find.widgetWithText(FilledButton, 'Retry'), findsOneWidget);
    });

    testWidgets('shows item cards with active and inactive chips', (
      tester,
    ) async {
      await tester.pumpWidget(_buildApp(state: _loadedState));
      await tester.pumpAndSettle();

      expect(find.text('Widget Alpha'), findsOneWidget);
      expect(find.text('Widget Beta'), findsOneWidget);
      expect(find.text('Active'), findsOneWidget);
      expect(find.text('Inactive'), findsOneWidget);
      expect(find.text('BAR001'), findsOneWidget);
      expect(find.text('pcs'), findsOneWidget);
      expect(find.text('100'), findsOneWidget);
    });

    testWidgets('shows inventory actions when speed dial expands', (
      tester,
    ) async {
      await tester.pumpWidget(_buildApp(state: _loadedState));
      await tester.pumpAndSettle();

      await _expandInventorySpeedDial(tester);

      expect(find.text('Add Product'), findsOneWidget);
      expect(find.text('Add Inventory'), findsOneWidget);
      expect(find.text('Batch Overview'), findsOneWidget);
      expect(find.text('Adjustment History'), findsOneWidget);
    });

    testWidgets('shows add product action for owner', (tester) async {
      await tester.pumpWidget(_buildApp(state: _loadedState));
      await tester.pumpAndSettle();
      expect(find.byKey(ItemsPage.addProductFabKey), findsOneWidget);
    });

    testWidgets('shows add product action for manager', (tester) async {
      await tester.pumpWidget(_buildApp(state: _loadedState, role: 'Manager'));
      await tester.pumpAndSettle();
      expect(find.byKey(ItemsPage.addProductFabKey), findsOneWidget);
    });

    testWidgets('hides add product action for staff', (tester) async {
      await tester.pumpWidget(_buildApp(state: _loadedState, role: 'Staff'));
      await tester.pumpAndSettle();
      expect(find.byKey(ItemsPage.addProductFabKey), findsNothing);
    });

    testWidgets('staff cannot open edit sheet by tapping item', (tester) async {
      await tester.pumpWidget(_buildApp(state: _loadedState, role: 'Staff'));
      await tester.pumpAndSettle();

      await tester.tap(find.text('Widget Alpha'));
      await tester.pumpAndSettle();

      expect(find.byKey(EditItemSheet.nameFieldKey), findsNothing);
    });

    testWidgets('owner can create item and gets success snackbar', (
      tester,
    ) async {
      final getItems = MockGetItems();
      final createItem = MockCreateItem();
      final updateItem = MockUpdateItem();

      when(getItems.call).thenAnswer((_) async => _loadedState.items);
      when(
        () => createItem(
          name: any(named: 'name'),
          barcode: any(named: 'barcode'),
          uom: any(named: 'uom'),
          description: any(named: 'description'),
        ),
      ).thenAnswer(
        (_) async => const Item(
          itemId: 'item-3',
          name: 'New Product',
          barcode: 'BAR003',
          uom: 'pcs',
          isActive: true,
          currentStock: 0,
        ),
      );
      when(
        () => updateItem(
          itemId: any(named: 'itemId'),
          name: any(named: 'name'),
          barcode: any(named: 'barcode'),
          uom: any(named: 'uom'),
          description: any(named: 'description'),
          isActive: any(named: 'isActive'),
        ),
      ).thenAnswer((_) async => _loadedState.items.first);

      await tester.pumpWidget(
        _buildManageFlowApp(
          session: _session('Owner'),
          getItems: getItems,
          createItem: createItem,
          updateItem: updateItem,
        ),
      );
      await tester.pumpAndSettle();

      await _tapAddProductAction(tester);

      await tester.enterText(
        find.byKey(CreateItemSheet.nameFieldKey),
        'New Product',
      );
      await tester.enterText(
        find.byKey(CreateItemSheet.barcodeFieldKey),
        'BAR003',
      );
      await tester.enterText(find.byKey(CreateItemSheet.uomFieldKey), 'pcs');
      await tester.tap(find.byKey(CreateItemSheet.submitButtonKey));
      await tester.pumpAndSettle();

      expect(find.text('Product created successfully.'), findsOneWidget);
      expect(find.byKey(CreateItemSheet.nameFieldKey), findsNothing);
      verify(getItems.call).called(greaterThanOrEqualTo(2));
    });

    testWidgets('owner can open edit sheet and update item', (tester) async {
      final getItems = MockGetItems();
      final createItem = MockCreateItem();
      final updateItem = MockUpdateItem();

      when(getItems.call).thenAnswer((_) async => _loadedState.items);
      when(
        () => createItem(
          name: any(named: 'name'),
          barcode: any(named: 'barcode'),
          uom: any(named: 'uom'),
          description: any(named: 'description'),
        ),
      ).thenAnswer((_) async => _loadedState.items.first);
      when(
        () => updateItem(
          itemId: any(named: 'itemId'),
          name: any(named: 'name'),
          barcode: any(named: 'barcode'),
          uom: any(named: 'uom'),
          description: any(named: 'description'),
          isActive: any(named: 'isActive'),
        ),
      ).thenAnswer(
        (_) async => const Item(
          itemId: 'item-1',
          name: 'Widget Alpha Updated',
          barcode: 'BAR001',
          uom: 'pcs',
          isActive: true,
          currentStock: 100,
        ),
      );

      await tester.pumpWidget(
        _buildManageFlowApp(
          session: _session('Owner'),
          getItems: getItems,
          createItem: createItem,
          updateItem: updateItem,
        ),
      );
      await tester.pumpAndSettle();

      await tester.tap(find.text('Widget Alpha'));
      await tester.pumpAndSettle();
      expect(find.byKey(EditItemSheet.nameFieldKey), findsOneWidget);

      await tester.enterText(
        find.byKey(EditItemSheet.nameFieldKey),
        'Widget Alpha Updated',
      );
      await tester.tap(find.byKey(EditItemSheet.submitButtonKey));
      await tester.pumpAndSettle();

      expect(find.text('Product updated successfully.'), findsOneWidget);
      verify(getItems.call).called(greaterThanOrEqualTo(2));
    });

    testWidgets('create product scan button fills barcode field', (
      tester,
    ) async {
      final getItems = MockGetItems();
      final createItem = MockCreateItem();
      final updateItem = MockUpdateItem();

      when(getItems.call).thenAnswer((_) async => _loadedState.items);
      when(
        () => createItem(
          name: any(named: 'name'),
          barcode: any(named: 'barcode'),
          uom: any(named: 'uom'),
          description: any(named: 'description'),
        ),
      ).thenAnswer(
        (_) async => const Item(
          itemId: 'item-3',
          name: 'New Product',
          barcode: 'SCANNED123',
          uom: 'pcs',
          isActive: true,
          currentStock: 0,
        ),
      );

      final observer = MockNavigatorObserver();
      await tester.pumpWidget(
        _buildManageFlowApp(
          session: _session('Owner'),
          getItems: getItems,
          createItem: createItem,
          updateItem: updateItem,
          navigatorObserver: observer,
        ),
      );
      await tester.pumpAndSettle();

      await _tapAddProductAction(tester);

      // Tap scan button and verify scanner was opened
      await tester.tap(find.byKey(CreateItemSheet.scanBarcodeButtonKey));
      await tester.pump();

      final capturedRoute =
          verify(() => observer.didPush(captureAny(), any())).captured.last
              as Route<dynamic>;
      expect(capturedRoute, isA<MaterialPageRoute<BarcodeScanResult?>>());

      // Simulate scan result
      Navigator.of(
        tester.element(find.byType(CreateItemSheet)),
      ).pop(const BarcodeScanResult(value: 'SCANNED123'));
      await tester.pumpAndSettle();

      final barcodeField = tester.widget<TextFormField>(
        find.byKey(CreateItemSheet.barcodeFieldKey),
      );
      expect(barcodeField.controller?.text, 'SCANNED123');
    });

    testWidgets('edit product scan button replaces barcode field', (
      tester,
    ) async {
      final getItems = MockGetItems();
      final createItem = MockCreateItem();
      final updateItem = MockUpdateItem();

      when(getItems.call).thenAnswer((_) async => _loadedState.items);
      when(
        () => createItem(
          name: any(named: 'name'),
          barcode: any(named: 'barcode'),
          uom: any(named: 'uom'),
          description: any(named: 'description'),
        ),
      ).thenAnswer((_) async => _loadedState.items.first);
      when(
        () => updateItem(
          itemId: any(named: 'itemId'),
          name: any(named: 'name'),
          barcode: any(named: 'barcode'),
          uom: any(named: 'uom'),
          description: any(named: 'description'),
          isActive: any(named: 'isActive'),
        ),
      ).thenAnswer((_) async => _loadedState.items.first);

      final observer = MockNavigatorObserver();
      await tester.pumpWidget(
        _buildManageFlowApp(
          session: _session('Owner'),
          getItems: getItems,
          createItem: createItem,
          updateItem: updateItem,
          navigatorObserver: observer,
        ),
      );
      await tester.pumpAndSettle();

      await tester.tap(find.text('Widget Alpha'));
      await tester.pumpAndSettle();

      // Tap scan button and verify scanner was opened
      await tester.tap(find.byKey(EditItemSheet.scanBarcodeButtonKey));
      await tester.pump();

      final capturedRoute =
          verify(() => observer.didPush(captureAny(), any())).captured.last
              as Route<dynamic>;
      expect(capturedRoute, isA<MaterialPageRoute<BarcodeScanResult?>>());

      // Simulate scan result
      Navigator.of(
        tester.element(find.byType(EditItemSheet)),
      ).pop(const BarcodeScanResult(value: 'SCANNED456'));
      await tester.pumpAndSettle();

      final barcodeField = tester.widget<TextFormField>(
        find.byKey(EditItemSheet.barcodeFieldKey),
      );
      expect(barcodeField.controller?.text, 'SCANNED456');
    });

    testWidgets(
      'create product with long scanned barcode shows validation error',
      (tester) async {
        final getItems = MockGetItems();
        final createItem = MockCreateItem();
        final updateItem = MockUpdateItem();

        when(getItems.call).thenAnswer((_) async => _loadedState.items);
        when(
          () => createItem(
            name: any(named: 'name'),
            barcode: any(named: 'barcode'),
            uom: any(named: 'uom'),
            description: any(named: 'description'),
          ),
        ).thenAnswer((_) async => _loadedState.items.first);

        final observer = MockNavigatorObserver();
        await tester.pumpWidget(
          _buildManageFlowApp(
            session: _session('Owner'),
            getItems: getItems,
            createItem: createItem,
            updateItem: updateItem,
            navigatorObserver: observer,
          ),
        );
        await tester.pumpAndSettle();

        await _tapAddProductAction(tester);

        await tester.enterText(
          find.byKey(CreateItemSheet.nameFieldKey),
          'Test Product',
        );
        await tester.enterText(find.byKey(CreateItemSheet.uomFieldKey), 'pcs');

        // Tap scan button and simulate a long barcode result
        await tester.tap(find.byKey(CreateItemSheet.scanBarcodeButtonKey));
        await tester.pump();

        final longBarcode = 'x' * 121;
        Navigator.of(
          tester.element(find.byType(CreateItemSheet)),
        ).pop(BarcodeScanResult(value: longBarcode));
        await tester.pumpAndSettle();

        expect(
          find.text('Barcode must be 120 characters or fewer.'),
          findsOneWidget,
        );
      },
    );

    testWidgets(
      'edit product with long scanned barcode shows validation error',
      (tester) async {
        final getItems = MockGetItems();
        final createItem = MockCreateItem();
        final updateItem = MockUpdateItem();

        when(getItems.call).thenAnswer((_) async => _loadedState.items);
        when(
          () => createItem(
            name: any(named: 'name'),
            barcode: any(named: 'barcode'),
            uom: any(named: 'uom'),
            description: any(named: 'description'),
          ),
        ).thenAnswer((_) async => _loadedState.items.first);
        when(
          () => updateItem(
            itemId: any(named: 'itemId'),
            name: any(named: 'name'),
            barcode: any(named: 'barcode'),
            uom: any(named: 'uom'),
            description: any(named: 'description'),
            isActive: any(named: 'isActive'),
          ),
        ).thenAnswer((_) async => _loadedState.items.first);

        final observer = MockNavigatorObserver();
        await tester.pumpWidget(
          _buildManageFlowApp(
            session: _session('Owner'),
            getItems: getItems,
            createItem: createItem,
            updateItem: updateItem,
            navigatorObserver: observer,
          ),
        );
        await tester.pumpAndSettle();

        await tester.tap(find.text('Widget Alpha'));
        await tester.pumpAndSettle();

        // Tap scan button and simulate a long barcode result
        await tester.tap(find.byKey(EditItemSheet.scanBarcodeButtonKey));
        await tester.pump();

        final longBarcode = 'x' * 121;
        Navigator.of(
          tester.element(find.byType(EditItemSheet)),
        ).pop(BarcodeScanResult(value: longBarcode));
        await tester.pumpAndSettle();

        expect(
          find.text('Barcode must be 120 characters or fewer.'),
          findsOneWidget,
        );
      },
    );
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
