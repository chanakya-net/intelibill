import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/app/theme/app_theme.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/customers/domain/entities/customer.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sellable.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/payment_method.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/controllers/new_sale_controller.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/pages/new_sale_page.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/widgets/payment_section.dart';

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
  void selectCustomer(String? customerId) {
    Customer? selected;
    if (customerId != null && customerId.isNotEmpty) {
      for (final customer in _state.availableCustomers) {
        if (customer.customerId == customerId) {
          selected = customer;
          break;
        }
      }
    }

    final nextMethod =
        selected == null && _state.paymentMethod == PaymentMethod.credit
        ? PaymentMethod.cash
        : _state.paymentMethod;
    _state = _state.copyWith(
      selectedCustomer: selected,
      clearSelectedCustomer: selected == null,
      paymentMethod: nextMethod,
    );
    _validatePayment();
    state = _state;
  }

  @override
  void setPaymentMethod(PaymentMethod method) {
    if (method == _state.paymentMethod) return;

    if (method == PaymentMethod.credit && _state.selectedCustomer == null) {
      _state = _state.copyWith(
        submissionFailure: const Failure.validation(
          message: 'Select a customer for credit or due payment.',
        ),
      );
      state = _state;
      return;
    }

    _state = _state.copyWith(paymentMethod: method);
    _validatePayment();
    state = _state;
  }

  @override
  void setPaidAmount(double paidAmount) {
    final clampedPaid = paidAmount.clamp(0.0, _state.payable);
    final nextDue = (_state.payable - clampedPaid).clamp(0.0, double.infinity);

    _state = _state.copyWith(paidAmount: clampedPaid, dueAmount: nextDue);
    _validatePayment();
    state = _state;
  }

  @override
  void setDueAmount(double dueAmount) {
    final clampedDue = dueAmount.clamp(0.0, _state.payable);
    final nextPaid = (_state.payable - clampedDue).clamp(0.0, double.infinity);

    _state = _state.copyWith(paidAmount: nextPaid, dueAmount: clampedDue);
    _validatePayment();
    state = _state;
  }

  void _validatePayment() {
    if (_state.selectedCustomer == null &&
        (_state.dueAmount > 0 ||
            _state.paymentMethod == PaymentMethod.credit)) {
      _state = _state.copyWith(
        submissionFailure: const Failure.validation(
          message: 'Select a customer for credit or due payment.',
        ),
      );
      return;
    }

    final amountsMatch =
        (_state.paidAmount + _state.dueAmount - _state.payable).abs() <= 0.01;
    if (!amountsMatch) {
      _state = _state.copyWith(
        submissionFailure: Failure.validation(
          message:
              'Paid and due must equal ₹${_state.payable.toStringAsFixed(2)} in total.',
        ),
      );
      return;
    }

    _state = _state.copyWith(clearSubmissionFailure: true);
  }

  @override
  Future<void> search({String? searchTerm, String? barcode}) async {
    final term = (searchTerm ?? _state.searchTerm).trim();
    final code = (barcode ?? _state.barcodeTerm).trim();
    final nextSearchTerm = code.isNotEmpty ? '' : term;

    lastSearchTerm = nextSearchTerm.isEmpty ? null : nextSearchTerm;
    lastBarcode = code.isEmpty ? null : code;
    _state = _state.copyWith(searchTerm: nextSearchTerm, barcodeTerm: code);
    state = _state;
  }
}

Widget _buildApp(_StubNewSaleController controller) {
  return ProviderScope(
    overrides: [newSaleControllerProvider.overrideWith(() => controller)],
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

Customer _customer() {
  return const Customer(
    customerId: 'cust-1',
    name: 'Alice',
    phoneNumber: '9999999999',
    isActive: true,
  );
}

String _fieldText(WidgetTester tester, Key key) {
  return tester.widget<TextField>(find.byKey(key)).controller!.text;
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
        _buildApp(_StubNewSaleController(const NewSaleState())),
      );

      expect(find.text('Cart is empty.'), findsOneWidget);
    });

    testWidgets('renders results and adds item to cart', (tester) async {
      final state = NewSaleState(results: [_goods()]);
      await tester.pumpWidget(_buildApp(_StubNewSaleController(state)));

      expect(find.text('Flour'), findsOneWidget);
      expect(find.textContaining('Stock 10'), findsOneWidget);
      await tester.tap(find.byKey(const Key('add-button-g1')));
      await tester.pump();
      await tester.ensureVisible(find.byKey(const Key('decrease-g1')));
      await tester.pump();

      expect(find.text('Flour'), findsNWidgets(2));
      await tester.ensureVisible(find.textContaining('Total:'));
      await tester.pump();
      expect(find.textContaining('Total:'), findsOneWidget);
      expect(find.byKey(const Key('decrease-g1')), findsOneWidget);
      expect(find.byKey(const Key('increase-g1')), findsOneWidget);
      expect(find.text('Qty: 1'), findsOneWidget);
    });

    testWidgets(
      'renders service results distinctly and adds service cart line',
      (tester) async {
        final state = NewSaleState(results: [_service()]);
        await tester.pumpWidget(_buildApp(_StubNewSaleController(state)));

        expect(find.text('Installation'), findsOneWidget);
        expect(find.text('Service'), findsOneWidget);
        expect(find.textContaining('₹150'), findsOneWidget);
        await tester.tap(find.byKey(const Key('add-button-s1')));
        await tester.pump();
        await tester.ensureVisible(
          find.byKey(const Key('service-unit-price-s1')),
        );
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
        cartLines: [NewSaleCartLine(sellable: goods, quantity: 1.25)],
      );

      await tester.pumpWidget(_buildApp(_StubNewSaleController(state)));
      await tester.ensureVisible(find.textContaining('Total:'));
      await tester.pump();

      expect(find.text('Qty: 1.25'), findsOneWidget);
      expect(find.textContaining('Total: ₹25.00'), findsOneWidget);
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
      await tester.ensureVisible(
        find.byKey(const Key('service-unit-price-s1')),
      );
      await tester.pump();

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

    testWidgets('shows customer picker and payment section', (tester) async {
      await tester.pumpWidget(
        _buildApp(
          _StubNewSaleController(
            NewSaleState(
              cartLines: [NewSaleCartLine(sellable: _goods(), quantity: 1)],
              availableCustomers: [_customer()],
            ),
          ),
        ),
      );

      expect(find.byKey(NewSalePage.customerDropdownKey), findsOneWidget);
      expect(find.byKey(PaymentSection.paidAmountFieldKey), findsOneWidget);
      expect(find.byKey(PaymentSection.dueAmountFieldKey), findsOneWidget);
      expect(find.text('Payment'), findsOneWidget);
      expect(
        tester
            .widget<ChoiceChip>(
              find.byKey(PaymentSection.paymentMethodCreditKey),
            )
            .onSelected,
        isNull,
      );
    });

    testWidgets('enables credit after selecting customer', (tester) async {
      final controller = _StubNewSaleController(
        NewSaleState(
          cartLines: [NewSaleCartLine(sellable: _goods(), quantity: 1)],
          availableCustomers: [_customer()],
        ),
      );
      await tester.pumpWidget(_buildApp(controller));

      await tester.tap(find.byKey(NewSalePage.customerDropdownKey));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Alice').last);
      await tester.pumpAndSettle();

      final creditChip = tester.widget<ChoiceChip>(
        find.byKey(PaymentSection.paymentMethodCreditKey),
      );
      expect(creditChip.onSelected, isNotNull);
    });

    testWidgets('due amount requires customer before submit', (tester) async {
      final controller = _StubNewSaleController(
        NewSaleState(
          cartLines: [NewSaleCartLine(sellable: _goods(), quantity: 1)],
          availableCustomers: [_customer()],
        ),
      );
      await tester.pumpWidget(_buildApp(controller));

      await tester.enterText(
        find.byKey(PaymentSection.dueAmountFieldKey),
        '10',
      );
      await tester.pump();

      final blockedButton = tester.widget<FilledButton>(
        find.byKey(NewSalePage.submitButtonKey),
      );
      expect(blockedButton.onPressed, isNull);
      expect(find.byKey(NewSalePage.paymentFailureKey), findsOneWidget);

      await tester.ensureVisible(find.byKey(NewSalePage.customerDropdownKey));
      await tester.pump();
      await tester.tap(find.byKey(NewSalePage.customerDropdownKey));
      await tester.pumpAndSettle();
      await tester.ensureVisible(find.text('Alice'));
      await tester.tap(find.text('Alice').last);
      await tester.pumpAndSettle();

      final enabledButton = tester.widget<FilledButton>(
        find.byKey(NewSalePage.submitButtonKey),
      );
      expect(enabledButton.onPressed, isNotNull);
      expect(find.byKey(NewSalePage.paymentFailureKey), findsNothing);
    });

    testWidgets(
      'payment split fields stay editable during incremental typing',
      (
        tester,
      ) async {
        final controller = _StubNewSaleController(
          NewSaleState(
            cartLines: [NewSaleCartLine(sellable: _goods(), quantity: 1)],
            availableCustomers: [_customer()],
            selectedCustomer: _customer(),
            paidAmount: 20,
            dueAmount: 0,
          ),
        );
        await tester.pumpWidget(_buildApp(controller));

        await tester.tap(find.byKey(PaymentSection.dueAmountFieldKey));
        await tester.pump();

        tester.testTextInput.updateEditingValue(
          const TextEditingValue(
            text: '5',
            selection: TextSelection.collapsed(offset: 1),
          ),
        );
        await tester.pump();
        expect(_fieldText(tester, PaymentSection.dueAmountFieldKey), '5');

        tester.testTextInput.updateEditingValue(
          const TextEditingValue(
            text: '5.',
            selection: TextSelection.collapsed(offset: 2),
          ),
        );
        await tester.pump();
        expect(_fieldText(tester, PaymentSection.dueAmountFieldKey), '5.');

        FocusManager.instance.primaryFocus?.unfocus();
        await tester.pump();
        expect(_fieldText(tester, PaymentSection.dueAmountFieldKey), '5.00');

        await tester.ensureVisible(
          find.byKey(PaymentSection.paidAmountFieldKey),
        );
        await tester.pump();
        await tester.tap(find.byKey(PaymentSection.paidAmountFieldKey));
        await tester.pump();

        tester.testTextInput.updateEditingValue(
          const TextEditingValue(
            text: '15',
            selection: TextSelection.collapsed(offset: 2),
          ),
        );
        await tester.pump();
        expect(_fieldText(tester, PaymentSection.paidAmountFieldKey), '15');

        tester.testTextInput.updateEditingValue(
          const TextEditingValue(
            text: '15.5',
            selection: TextSelection.collapsed(offset: 4),
          ),
        );
        await tester.pump();
        expect(_fieldText(tester, PaymentSection.paidAmountFieldKey), '15.5');

        tester.testTextInput.updateEditingValue(
          const TextEditingValue(
            text: '15.50',
            selection: TextSelection.collapsed(offset: 5),
          ),
        );
        await tester.pump();
        expect(_fieldText(tester, PaymentSection.paidAmountFieldKey), '15.50');
      },
    );
  });
}
