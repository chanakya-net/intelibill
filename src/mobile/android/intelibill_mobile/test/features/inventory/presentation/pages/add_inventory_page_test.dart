import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/app/theme/app_theme.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/entities/item.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/entities/product_details.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/use_cases/add_inventory_inbound.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/use_cases/get_product_details.dart';
import 'package:intelibill_mobile/src/features/inventory/presentation/controllers/add_inventory_controller.dart';
import 'package:intelibill_mobile/src/features/inventory/presentation/controllers/items_controller.dart';
import 'package:intelibill_mobile/src/features/inventory/presentation/pages/add_inventory_page.dart';
import 'package:intelibill_mobile/src/shared/barcode_scanner/barcode_scan_result.dart';
import 'package:mocktail/mocktail.dart';

class _StubAddInventoryController extends AddInventoryController {
  _StubAddInventoryController(this._state);

  final AddInventoryState _state;

  @override
  AddInventoryState build() => _state;
}

class MockAddInventoryInbound extends Mock implements AddInventoryInbound {}

class MockGetProductDetails extends Mock implements GetProductDetails {}

class MockNavigatorObserver extends Mock implements NavigatorObserver {}

const _catalogItems = [
  Item(
    itemId: 'item-1',
    name: 'Milk',
    barcode: 'BAR001',
    uom: 'ltr',
    isActive: true,
    currentStock: 5,
  ),
  Item(
    itemId: 'item-2',
    name: 'Rice',
    barcode: 'BAR002',
    uom: 'kg',
    isActive: true,
    currentStock: 10,
  ),
];

Widget _buildAppWithState(
  AddInventoryState state, {
  List<Item> catalogItems = const [],
  GetProductDetails? getProductDetails,
}) {
  final detailsUseCase = getProductDetails ?? MockGetProductDetails();
  if (detailsUseCase is MockGetProductDetails && getProductDetails == null) {
    when(
      () => detailsUseCase(
        name: any(named: 'name'),
        barcode: any(named: 'barcode'),
      ),
    ).thenAnswer(
      (_) async => const ProductDetails(
        name: '',
        description: '',
        uom: '',
        costPrice: 0,
        mrp: 0,
        salesPrice: 0,
      ),
    );
  }

  return ProviderScope(
    overrides: [
      addInventoryControllerProvider.overrideWith(
        () => _StubAddInventoryController(state),
      ),
      itemsControllerProvider.overrideWithValue(
        ItemsState(items: catalogItems),
      ),
      getProductDetailsProvider.overrideWithValue(detailsUseCase),
    ],
    child: MaterialApp(
      theme: AppTheme.lightTheme,
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      home: const AddInventoryPage(),
    ),
  );
}

Widget _buildAppWithUseCase(MockAddInventoryInbound useCase) {
  return ProviderScope(
    overrides: [
      addInventoryInboundProvider.overrideWithValue(useCase),
      itemsControllerProvider.overrideWithValue(const ItemsState()),
      getProductDetailsProvider.overrideWithValue(MockGetProductDetails()),
    ],
    child: MaterialApp(
      theme: AppTheme.lightTheme,
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      home: const AddInventoryPage(),
    ),
  );
}

void main() {
  setUpAll(() {
    registerFallbackValue(
      MaterialPageRoute<BarcodeScanResult?>(builder: (_) => const SizedBox()),
    );
  });

  group('AddInventoryPage', () {
    testWidgets('renders all expected fields', (tester) async {
      await tester.pumpWidget(_buildAppWithState(const AddInventoryState()));
      await tester.pumpAndSettle();

      expect(find.byKey(AddInventoryPage.itemNameFieldKey), findsOneWidget);
      expect(find.byKey(AddInventoryPage.barcodeFieldKey), findsOneWidget);
      expect(find.byKey(AddInventoryPage.scanBarcodeButtonKey), findsOneWidget);
      expect(find.byKey(AddInventoryPage.uomFieldKey), findsOneWidget);
      expect(find.byKey(AddInventoryPage.batchNumberFieldKey), findsOneWidget);
      expect(find.byKey(AddInventoryPage.quantityFieldKey), findsOneWidget);
      expect(find.byKey(AddInventoryPage.costPriceFieldKey), findsOneWidget);
      expect(find.byKey(AddInventoryPage.mrpFieldKey), findsOneWidget);
      expect(find.byKey(AddInventoryPage.salesPriceFieldKey), findsOneWidget);
      expect(find.byKey(AddInventoryPage.taxRateFieldKey), findsOneWidget);
      expect(find.byKey(AddInventoryPage.taxIncludedSwitchKey), findsOneWidget);
      expect(find.byKey(AddInventoryPage.expiryDateFieldKey), findsOneWidget);
      expect(
        find.byKey(AddInventoryPage.manufacturingDateFieldKey),
        findsOneWidget,
      );
      expect(find.byKey(AddInventoryPage.referenceFieldKey), findsOneWidget);
      expect(find.byKey(AddInventoryPage.notesFieldKey), findsOneWidget);
      expect(find.byKey(AddInventoryPage.submitButtonKey), findsOneWidget);
    });

    testWidgets('generates a batch number when the form opens', (tester) async {
      await tester.pumpWidget(_buildAppWithState(const AddInventoryState()));
      await tester.pumpAndSettle();

      final batchNumberField = tester.widget<TextFormField>(
        find.byKey(AddInventoryPage.batchNumberFieldKey),
      );

      expect(
        batchNumberField.controller?.text,
        matches(RegExp(r'^BN-\d{8}-[A-Z2-9]{5}$')),
      );
    });

    testWidgets('item name autocomplete fills barcode and UOM', (tester) async {
      await tester.pumpWidget(
        _buildAppWithState(
          const AddInventoryState(),
          catalogItems: _catalogItems,
        ),
      );
      await tester.pumpAndSettle();

      await tester.enterText(
        find.byKey(AddInventoryPage.itemNameFieldKey),
        'Mil',
      );
      await tester.pumpAndSettle();

      expect(find.text('Milk'), findsOneWidget);

      await tester.tap(find.text('Milk'));
      await tester.pumpAndSettle();

      final itemNameField = tester.widget<TextFormField>(
        find.byKey(AddInventoryPage.itemNameFieldKey),
      );
      final barcodeField = tester.widget<TextFormField>(
        find.byKey(AddInventoryPage.barcodeFieldKey),
      );
      final uomField = tester.widget<TextFormField>(
        find.byKey(AddInventoryPage.uomFieldKey),
      );

      expect(itemNameField.controller?.text, 'Milk');
      expect(barcodeField.controller?.text, 'BAR001');
      expect(uomField.controller?.text, 'ltr');
    });

    testWidgets('barcode autocomplete fills item name and UOM', (tester) async {
      await tester.pumpWidget(
        _buildAppWithState(
          const AddInventoryState(),
          catalogItems: _catalogItems,
        ),
      );
      await tester.pumpAndSettle();

      await tester.enterText(
        find.byKey(AddInventoryPage.barcodeFieldKey),
        'BAR002',
      );
      await tester.pumpAndSettle();

      expect(find.text('BAR002'), findsAtLeastNWidgets(1));

      await tester.tap(find.text('BAR002').last);
      await tester.pumpAndSettle();

      final itemNameField = tester.widget<TextFormField>(
        find.byKey(AddInventoryPage.itemNameFieldKey),
      );
      final barcodeField = tester.widget<TextFormField>(
        find.byKey(AddInventoryPage.barcodeFieldKey),
      );
      final uomField = tester.widget<TextFormField>(
        find.byKey(AddInventoryPage.uomFieldKey),
      );

      expect(itemNameField.controller?.text, 'Rice');
      expect(barcodeField.controller?.text, 'BAR002');
      expect(uomField.controller?.text, 'kg');
    });

    testWidgets('barcode focus loss fills product pricing details', (
      tester,
    ) async {
      final getProductDetails = MockGetProductDetails();
      when(
        () => getProductDetails(
          name: any(named: 'name'),
          barcode: any(named: 'barcode'),
        ),
      ).thenAnswer(
        (_) async => const ProductDetails(
          name: 'Rice',
          description: 'Premium rice',
          uom: 'kg',
          costPrice: 42,
          mrp: 50,
          salesPrice: 48,
          taxIncluded: true,
          taxRatePercent: 18,
        ),
      );

      await tester.pumpWidget(
        _buildAppWithState(
          const AddInventoryState(),
          getProductDetails: getProductDetails,
        ),
      );
      await tester.pumpAndSettle();

      await tester.enterText(
        find.byKey(AddInventoryPage.barcodeFieldKey),
        'BAR002',
      );
      await tester.tap(find.byKey(AddInventoryPage.quantityFieldKey));
      await tester.pumpAndSettle();

      final itemNameField = tester.widget<TextFormField>(
        find.byKey(AddInventoryPage.itemNameFieldKey),
      );
      final uomField = tester.widget<TextFormField>(
        find.byKey(AddInventoryPage.uomFieldKey),
      );
      final costPriceField = tester.widget<TextFormField>(
        find.byKey(AddInventoryPage.costPriceFieldKey),
      );
      final mrpField = tester.widget<TextFormField>(
        find.byKey(AddInventoryPage.mrpFieldKey),
      );
      final salesPriceField = tester.widget<TextFormField>(
        find.byKey(AddInventoryPage.salesPriceFieldKey),
      );
      final taxRateField = tester.widget<TextFormField>(
        find.byKey(AddInventoryPage.taxRateFieldKey),
      );
      final taxSwitch = tester.widget<SwitchListTile>(
        find.byKey(AddInventoryPage.taxIncludedSwitchKey),
      );

      expect(itemNameField.controller?.text, 'Rice');
      expect(uomField.controller?.text, 'kg');
      expect(costPriceField.controller?.text, '42');
      expect(mrpField.controller?.text, '50');
      expect(salesPriceField.controller?.text, '48');
      expect(taxRateField.controller?.text, '18');
      expect(taxSwitch.value, isTrue);
      verify(() => getProductDetails(barcode: 'BAR002')).called(1);
    });

    testWidgets('empty submit shows validation errors and no API call', (
      tester,
    ) async {
      final addInventoryInbound = MockAddInventoryInbound();

      await tester.pumpWidget(_buildAppWithUseCase(addInventoryInbound));
      await tester.pumpAndSettle();

      await tester.ensureVisible(find.byKey(AddInventoryPage.submitButtonKey));
      await tester.tap(find.byKey(AddInventoryPage.submitButtonKey));
      await tester.pumpAndSettle();

      expect(find.text('Item name is required.'), findsOneWidget);
      expect(find.text('Barcode is required.'), findsOneWidget);
      expect(find.text('Unit of measure is required.'), findsOneWidget);
      expect(find.text('Quantity is required.'), findsOneWidget);
      expect(find.text('Cost price is required.'), findsOneWidget);
      expect(find.text('MRP is required.'), findsOneWidget);
      expect(find.text('Sales price is required.'), findsOneWidget);

      verifyNever(
        () => addInventoryInbound(
          itemName: any(named: 'itemName'),
          barcode: any(named: 'barcode'),
          uom: any(named: 'uom'),
          batchNumber: any(named: 'batchNumber'),
          quantity: any(named: 'quantity'),
          costPrice: any(named: 'costPrice'),
          mrp: any(named: 'mrp'),
          salesPrice: any(named: 'salesPrice'),
          taxRate: any(named: 'taxRate'),
          taxIncluded: any(named: 'taxIncluded'),
          expiryDate: any(named: 'expiryDate'),
          manufacturingDate: any(named: 'manufacturingDate'),
          referenceNumber: any(named: 'referenceNumber'),
          notes: any(named: 'notes'),
        ),
      );
    });

    testWidgets('submit disabled while submitting', (tester) async {
      await tester.pumpWidget(
        _buildAppWithState(const AddInventoryState(isSubmitting: true)),
      );
      await tester.pump();

      final button = tester.widget<FilledButton>(
        find.byKey(AddInventoryPage.submitButtonKey),
      );
      expect(button.onPressed, isNull);
    });

    testWidgets('inline error displayed when submit failure present', (
      tester,
    ) async {
      await tester.pumpWidget(
        _buildAppWithState(
          const AddInventoryState(submitFailure: Failure.network()),
        ),
      );
      await tester.pumpAndSettle();

      expect(
        find.text('Unable to connect. Please check your network.'),
        findsOneWidget,
      );
    });

    testWidgets('scanning a barcode fills the field and fetches details', (
      tester,
    ) async {
      final getProductDetails = MockGetProductDetails();
      when(
        () => getProductDetails(
          name: any(named: 'name'),
          barcode: any(named: 'barcode'),
        ),
      ).thenAnswer(
        (_) async => const ProductDetails(
          name: 'Scanned Item',
          description: '',
          uom: 'pcs',
          costPrice: 10,
          mrp: 15,
          salesPrice: 12,
        ),
      );

      final observer = MockNavigatorObserver();
      await tester.pumpWidget(
        ProviderScope(
          overrides: [
            addInventoryControllerProvider.overrideWith(
              () => _StubAddInventoryController(const AddInventoryState()),
            ),
            itemsControllerProvider.overrideWithValue(const ItemsState()),
            getProductDetailsProvider.overrideWithValue(getProductDetails),
          ],
          child: MaterialApp(
            theme: AppTheme.lightTheme,
            localizationsDelegates: AppLocalizations.localizationsDelegates,
            supportedLocales: AppLocalizations.supportedLocales,
            navigatorObservers: [observer],
            home: const AddInventoryPage(),
          ),
        ),
      );
      await tester.pumpAndSettle();

      await tester.tap(find.byKey(AddInventoryPage.scanBarcodeButtonKey));
      await tester.pump();

      // Verify scanner was opened
      final capturedRoute =
          verify(() => observer.didPush(captureAny(), any())).captured.last
              as Route<dynamic>;
      expect(capturedRoute, isA<MaterialPageRoute<BarcodeScanResult?>>());

      // Simulate scan result
      Navigator.of(
        tester.element(find.byType(AddInventoryPage)),
      ).pop(const BarcodeScanResult(value: 'SCANNED123'));
      await tester.pumpAndSettle();

      final barcodeField = tester.widget<TextFormField>(
        find.byKey(AddInventoryPage.barcodeFieldKey),
      );
      expect(barcodeField.controller?.text, 'SCANNED123');

      // Verify details were fetched
      expect(find.text('Scanned Item'), findsOneWidget);
      verify(() => getProductDetails(barcode: 'SCANNED123')).called(1);
    });

    testWidgets('scanning a catalog barcode autofills name and UOM', (
      tester,
    ) async {
      final getProductDetails = MockGetProductDetails();
      when(
        () => getProductDetails(
          name: any(named: 'name'),
          barcode: any(named: 'barcode'),
        ),
      ).thenAnswer(
        (_) async => const ProductDetails(
          name: 'Milk',
          description: '',
          uom: 'ltr',
          costPrice: 20,
          mrp: 25,
          salesPrice: 22,
        ),
      );

      final observer = MockNavigatorObserver();
      await tester.pumpWidget(
        ProviderScope(
          overrides: [
            addInventoryControllerProvider.overrideWith(
              () => _StubAddInventoryController(const AddInventoryState()),
            ),
            itemsControllerProvider.overrideWithValue(
              const ItemsState(items: _catalogItems),
            ),
            getProductDetailsProvider.overrideWithValue(getProductDetails),
          ],
          child: MaterialApp(
            theme: AppTheme.lightTheme,
            localizationsDelegates: AppLocalizations.localizationsDelegates,
            supportedLocales: AppLocalizations.supportedLocales,
            navigatorObservers: [observer],
            home: const AddInventoryPage(),
          ),
        ),
      );
      await tester.pumpAndSettle();

      await tester.tap(find.byKey(AddInventoryPage.scanBarcodeButtonKey));
      await tester.pump();

      Navigator.of(
        tester.element(find.byType(AddInventoryPage)),
      ).pop(const BarcodeScanResult(value: 'BAR001'));
      await tester.pumpAndSettle();

      final itemNameField = tester.widget<TextFormField>(
        find.byKey(AddInventoryPage.itemNameFieldKey),
      );
      final barcodeField = tester.widget<TextFormField>(
        find.byKey(AddInventoryPage.barcodeFieldKey),
      );
      final uomField = tester.widget<TextFormField>(
        find.byKey(AddInventoryPage.uomFieldKey),
      );

      expect(itemNameField.controller?.text, 'Milk');
      expect(barcodeField.controller?.text, 'BAR001');
      expect(uomField.controller?.text, 'ltr');

      verify(
        () => getProductDetails(name: 'Milk', barcode: 'BAR001'),
      ).called(1);
    });

    testWidgets('long scanned barcode shows validation error', (tester) async {
      final getProductDetails = MockGetProductDetails();
      when(
        () => getProductDetails(
          name: any(named: 'name'),
          barcode: any(named: 'barcode'),
        ),
      ).thenAnswer(
        (_) async => const ProductDetails(
          name: '',
          description: '',
          uom: '',
          costPrice: 0,
          mrp: 0,
          salesPrice: 0,
        ),
      );

      final observer = MockNavigatorObserver();
      await tester.pumpWidget(
        ProviderScope(
          overrides: [
            addInventoryControllerProvider.overrideWith(
              () => _StubAddInventoryController(const AddInventoryState()),
            ),
            itemsControllerProvider.overrideWithValue(const ItemsState()),
            getProductDetailsProvider.overrideWithValue(getProductDetails),
          ],
          child: MaterialApp(
            theme: AppTheme.lightTheme,
            localizationsDelegates: AppLocalizations.localizationsDelegates,
            supportedLocales: AppLocalizations.supportedLocales,
            navigatorObservers: [observer],
            home: const AddInventoryPage(),
          ),
        ),
      );
      await tester.pumpAndSettle();

      await tester.tap(find.byKey(AddInventoryPage.scanBarcodeButtonKey));
      await tester.pump();

      final longBarcode = 'B' * 121;
      Navigator.of(
        tester.element(find.byType(AddInventoryPage)),
      ).pop(BarcodeScanResult(value: longBarcode));
      await tester.pumpAndSettle();

      expect(find.text(longBarcode), findsOneWidget);

      // Trigger validation by tapping submit
      await tester.ensureVisible(find.byKey(AddInventoryPage.submitButtonKey));
      await tester.tap(find.byKey(AddInventoryPage.submitButtonKey));
      await tester.pumpAndSettle();

      expect(
        find.text('Barcode must be 120 characters or fewer.'),
        findsOneWidget,
      );
    });
  });
}
