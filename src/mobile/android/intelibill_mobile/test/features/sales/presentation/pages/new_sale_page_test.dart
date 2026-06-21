import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/app/theme/app_theme.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_preview.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sellable.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/controllers/new_sale_controller.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/pages/new_sale_page.dart';

class _StubNewSaleController extends NewSaleController {
  _StubNewSaleController(this._state);

  NewSaleState _state;
  String? lastSearchTerm;
  String? lastBarcode;

  @override
  NewSaleState build() => _state;

  @override
  void updateSearchTerm(String value) {
    _state = _state.copyWith(searchTerm: value);
    state = _state;
  }

  @override
  void updateBarcodeTerm(String value) {
    _state = _state.copyWith(barcodeTerm: value, searchTerm: '');
    state = _state;
  }

  @override
  void updateCartQuantity(String sellableId, double nextQuantity) {
    final lines = _state.cartLines
        .where((line) => line.sellable.id != sellableId)
        .toList();
    if (nextQuantity > 0) {
      final line = _state.cartLines.firstWhere(
        (line) => line.sellable.id == sellableId,
      );
      lines.add(line.copyWith(quantity: nextQuantity));
    }
    _state = _state.copyWith(cartLines: lines);
    state = _state;
  }

  @override
  void removeFromCart(String sellableId) {
    _state = _state.copyWith(
      cartLines: _state.cartLines
          .where((line) => line.sellable.id != sellableId)
          .toList(),
    );
    state = _state;
  }

  @override
  Future<void> addToCart(Sellable sellable, {double quantity = 1}) async {
    final line = NewSaleCartLine(
      sellable: sellable,
      quantity: quantity,
      unitPrice: sellable.price,
    );
    _state = _state.copyWith(cartLines: [..._state.cartLines, line]);
    state = _state;
  }

  @override
  void updateCartUnitPrice(String sellableId, double nextUnitPrice) {
    final lines = _state.cartLines
        .where((line) => line.sellable.id != sellableId)
        .toList();
    final line = _state.cartLines.firstWhere(
      (line) => line.sellable.id == sellableId,
    );
    lines.add(line.copyWith(unitPrice: nextUnitPrice));
    _state = _state.copyWith(cartLines: lines);
    state = _state;
  }

  @override
  Future<void> search({String? searchTerm, String? barcode}) async {
    final term = (searchTerm ?? _state.searchTerm).trim();
    final code = (barcode ?? _state.barcodeTerm).trim();
    final nextSearchTerm = code.isNotEmpty ? '' : term;

    lastSearchTerm = nextSearchTerm.isEmpty ? null : nextSearchTerm;
    lastBarcode = code.isEmpty ? null : code;
    _state = _state.copyWith(
      searchTerm: nextSearchTerm,
      barcodeTerm: code,
    );
    state = _state;
  }
}

Widget _buildApp(_StubNewSaleController controller) {
  return ProviderScope(
    overrides: [
      newSaleControllerProvider.overrideWith(
        () => controller,
      ),
    ],
    child: MaterialApp(
      theme: AppTheme.lightTheme,
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      home: const NewSalePage(),
    ),
  );
}

Sellable _goods() {
  return const Sellable(
    id: 'g1',
    kind: 'Goods',
    name: 'Flour',
    stock: 10,
    price: 20,
    barcode: 'BAR001',
    batchNumber: 'BN-1',
  );
}

Sellable _service() {
  return const Sellable(
    id: 's1',
    kind: 'Service',
    name: 'Installation',
    stock: 0,
    price: 150,
    barcode: 'SRV001',
  );
}

SalePreview _preview({
  double totalAmount = 236,
  double totalTaxableAmount = 200,
  double totalTaxAmount = 36,
  double totalDiscountAmount = 14,
  double saleLevelEligibleSubtotal = 120,
  List<SalePreviewWarning>? warnings,
}) {
  return SalePreview(
    totalAmount: totalAmount,
    totalTaxableAmount: totalTaxableAmount,
    totalTaxAmount: totalTaxAmount,
    totalDiscountAmount: totalDiscountAmount,
    saleLevelEligibleSubtotal: saleLevelEligibleSubtotal,
    configuredSaleRule: const SalePreviewConfiguredSaleRule(
      ruleId: 'rule-1',
      ruleType: 'SalePercentage',
      percentage: 10,
      thresholdAmount: 100,
    ),
    lines: const [],
    infos: const [
      SalePreviewInfo(
        code: 'sale_preview.info.configured_rule_applied',
        message: 'Configured sale rule applied.',
      ),
    ],
    warnings:
        warnings ??
        const [
          SalePreviewWarning(
            code: 'sale_preview.warning.validation',
            message: 'Sale-level discount is limited by configured rule.',
            severity: 'info',
          ),
        ],
  );
}

void main() {
  group('NewSalePage', () {
    testWidgets('shows loading state', (tester) async {
      await tester.pumpWidget(
        _buildApp(
          _StubNewSaleController(const NewSaleState(isSearching: true)),
        ),
      );

      expect(find.byType(CircularProgressIndicator), findsOneWidget);
    });

    testWidgets('shows error state', (tester) async {
      await tester.pumpWidget(
        _buildApp(
          _StubNewSaleController(
            const NewSaleState(
              searchFailure: Failure.validation(message: 'Scan failed'),
            ),
          ),
        ),
      );

      expect(find.byKey(const Key('new-sale-failure')), findsOneWidget);
      expect(find.textContaining('Scan failed'), findsOneWidget);
      expect(find.text('No sellables found.'), findsOneWidget);
    });

    testWidgets('shows empty cart message', (tester) async {
      await tester.pumpWidget(
        _buildApp(
          _StubNewSaleController(const NewSaleState()),
        ),
      );

      expect(find.text('Cart is empty.'), findsOneWidget);
    });

    testWidgets('shows checkout totals and server preview warnings', (
      tester,
    ) async {
      const goods = Sellable(
        id: 'g1',
        kind: 'Goods',
        name: 'Flour',
        stock: 10,
        price: 20,
        barcode: 'BAR001',
        batchNumber: 'BN-1',
      );
      final state = NewSaleState(
        cartLines: [const NewSaleCartLine(sellable: goods, quantity: 2)],
        preview: _preview(),
      );

      await tester.pumpWidget(_buildApp(_StubNewSaleController(state)));

      expect(find.text('Checkout summary'), findsOneWidget);
      expect(find.text('Subtotal'), findsOneWidget);
      expect(find.textContaining('₹200.00'), findsWidgets);
      expect(find.textContaining('₹36.00'), findsWidgets);
      expect(find.textContaining('₹14.00'), findsWidgets);
      expect(find.textContaining('₹236.00'), findsWidgets);
      expect(find.text('Warnings'), findsOneWidget);
      expect(
        find.textContaining('Sale-level discount is limited by configured rule.'),
        findsOneWidget,
      );
      expect(find.byKey(const Key('checkout-button')), findsOneWidget);
      final checkoutButton = tester.widget<FilledButton>(
        find.byKey(const Key('checkout-button')),
      );
      expect(checkoutButton.onPressed, isNotNull);
    });

    testWidgets('shows preview loading state and disables checkout', (
      tester,
    ) async {
      const goods = Sellable(
        id: 'g1',
        kind: 'Goods',
        name: 'Flour',
        stock: 10,
        price: 20,
        barcode: 'BAR001',
        batchNumber: 'BN-1',
      );
      final state = NewSaleState(
        cartLines: [const NewSaleCartLine(sellable: goods, quantity: 2)],
        isPreviewLoading: true,
      );

      await tester.pumpWidget(_buildApp(_StubNewSaleController(state)));

      expect(find.byKey(const Key('checkout-preview-loading')), findsOneWidget);
      final checkoutButton = tester.widget<FilledButton>(
        find.byKey(const Key('checkout-button')),
      );
      expect(checkoutButton.onPressed, isNull);
    });

    testWidgets('shows preview error and retry preview action', (tester) async {
      const goods = Sellable(
        id: 'g1',
        kind: 'Goods',
        name: 'Flour',
        stock: 10,
        price: 20,
        barcode: 'BAR001',
        batchNumber: 'BN-1',
      );
      final state = NewSaleState(
        cartLines: [const NewSaleCartLine(sellable: goods, quantity: 2)],
        previewFailure: const Failure.network(message: 'offline'),
      );

      await tester.pumpWidget(_buildApp(_StubNewSaleController(state)));

      expect(find.byKey(const Key('checkout-preview-error')), findsOneWidget);
      expect(find.textContaining('offline'), findsOneWidget);
      final retryButton = tester.widget<OutlinedButton>(
        find.byKey(const Key('refresh-preview-button')),
      );
      expect(retryButton.onPressed, isNotNull);
    });

    testWidgets('renders results and adds item to cart', (tester) async {
      final state = NewSaleState(
        results: [_goods()],
      );
      await tester.pumpWidget(_buildApp(_StubNewSaleController(state)));

      expect(find.text('Flour'), findsOneWidget);
      expect(find.textContaining('Stock 10'), findsOneWidget);
      await tester.tap(find.byKey(const Key('add-button-g1')));
      await tester.pump();

      expect(find.text('Flour'), findsNWidgets(2));
      expect(find.text('Total'), findsOneWidget);
      expect(find.byKey(const Key('decrease-g1')), findsOneWidget);
      expect(find.byKey(const Key('increase-g1')), findsOneWidget);
      expect(find.text('Qty: 1'), findsOneWidget);
    });

    testWidgets(
      'renders service results distinctly and adds service cart line',
      (
        tester,
      ) async {
        final state = NewSaleState(results: [_service()]);
        await tester.pumpWidget(_buildApp(_StubNewSaleController(state)));

        expect(find.text('Installation'), findsOneWidget);
        expect(find.text('Service'), findsOneWidget);
        expect(find.textContaining('₹150'), findsOneWidget);
        await tester.tap(find.byKey(const Key('add-button-s1')));
        await tester.pump();

        expect(find.byKey(const Key('service-unit-price-s1')), findsOneWidget);
        expect(find.text('Qty: 1'), findsOneWidget);
      },
    );

    testWidgets('clears stale search results when latest state has failure', (
      tester,
    ) async {
      final controller = _StubNewSaleController(
        const NewSaleState(
          searchFailure: Failure.validation(message: 'Enter search'),
        ),
      );

      await tester.pumpWidget(_buildApp(controller));

      expect(find.byKey(const Key('new-sale-failure')), findsOneWidget);
      expect(find.text('Flour'), findsNothing);
      expect(find.text('No sellables found.'), findsOneWidget);
    });

    testWidgets('renders fractional cart quantities', (tester) async {
      const goods = Sellable(
        id: 'g1',
        kind: 'Goods',
        name: 'Flour',
        stock: 1.25,
        price: 20,
        barcode: 'BAR001',
        batchNumber: 'BN-1',
      );
      const state = NewSaleState(
        cartLines: [
          NewSaleCartLine(sellable: goods, quantity: 1.25),
        ],
      );

      await tester.pumpWidget(_buildApp(_StubNewSaleController(state)));

      expect(find.text('Qty: 1.25'), findsOneWidget);
      expect(find.textContaining('₹25.00'), findsNWidgets(3));
    });

    testWidgets('renders mixed goods and service cart lines', (tester) async {
      const goods = Sellable(
        id: 'g1',
        kind: 'Goods',
        name: 'Flour',
        stock: 1,
        price: 20,
        barcode: 'BAR001',
        batchNumber: 'BN-1',
      );
      const service = Sellable(
        id: 's1',
        kind: 'Service',
        name: 'Installation',
        stock: 0,
        price: 150,
        barcode: 'SRV001',
      );
      const state = NewSaleState(
        cartLines: [
          NewSaleCartLine(sellable: goods, quantity: 1),
          NewSaleCartLine(sellable: service, quantity: 2, unitPrice: 175),
        ],
      );

      await tester.pumpWidget(_buildApp(_StubNewSaleController(state)));

      expect(find.byKey(const Key('decrease-g1')), findsOneWidget);
      expect(find.byKey(const Key('service-unit-price-s1')), findsOneWidget);
    });

    testWidgets('barcode lookup clears stale search field and stays in sync', (
      tester,
    ) async {
      final controller = _StubNewSaleController(const NewSaleState());
      await tester.pumpWidget(_buildApp(controller));

      await tester.enterText(
        find.byKey(const Key('sales-search-field')),
        'Flour',
      );
      await tester.enterText(find.byKey(const Key('barcode-field')), 'BAR001');
      await tester.tap(find.byKey(const Key('barcode-search-button')));
      await tester.pump();

      final searchField = tester.widget<TextField>(
        find.byKey(const Key('sales-search-field')),
      );
      final barcodeField = tester.widget<TextField>(
        find.byKey(const Key('barcode-field')),
      );

      expect(searchField.controller!.text, isEmpty);
      expect(barcodeField.controller!.text, 'BAR001');
      expect(controller.lastSearchTerm, isNull);
      expect(controller.lastBarcode, 'BAR001');
    });
  });
}
