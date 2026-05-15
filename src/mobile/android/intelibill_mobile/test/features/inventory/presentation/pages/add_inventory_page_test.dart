import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/app/theme/app_theme.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/use_cases/add_inventory_inbound.dart';
import 'package:intelibill_mobile/src/features/inventory/presentation/controllers/add_inventory_controller.dart';
import 'package:intelibill_mobile/src/features/inventory/presentation/pages/add_inventory_page.dart';
import 'package:mocktail/mocktail.dart';

class _StubAddInventoryController extends AddInventoryController {
  _StubAddInventoryController(this._state);

  final AddInventoryState _state;

  @override
  AddInventoryState build() => _state;
}

class MockAddInventoryInbound extends Mock implements AddInventoryInbound {}

Widget _buildAppWithState(AddInventoryState state) {
  return ProviderScope(
    overrides: [
      addInventoryControllerProvider.overrideWith(
        () => _StubAddInventoryController(state),
      ),
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
  group('AddInventoryPage', () {
    testWidgets('renders all expected fields', (tester) async {
      await tester.pumpWidget(_buildAppWithState(const AddInventoryState()));
      await tester.pumpAndSettle();

      expect(find.byKey(AddInventoryPage.itemNameFieldKey), findsOneWidget);
      expect(find.byKey(AddInventoryPage.barcodeFieldKey), findsOneWidget);
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
  });
}
