import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/app/theme/app_theme.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/customers/domain/entities/customer.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/credit_note.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/payment_method.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_detail.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_preview.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sellable.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/controllers/new_sale_controller.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/pages/new_sale_page.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/widgets/payment_section.dart';

class _StubNewSaleController extends NewSaleController {
  _StubNewSaleController(
    this._state, {
    CreditNoteVerifyResult? Function(String code)? verifyResult,
  }) : _verifyCreditNoteResult = verifyResult;

  NewSaleState _state;
  final CreditNoteVerifyResult? Function(String code)? _verifyCreditNoteResult;

  String? lastSearchTerm;
  String? lastBarcode;
  int submitCallCount = 0;
  int verifyCallCount = 0;

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

  @override
  Future<void> verifyCreditNote(String code) async {
    verifyCallCount += 1;
    final providedNote = _verifyCreditNoteResult?.call(code);
    final note =
        providedNote ??
        CreditNoteVerifyResult(
          creditNoteId: 'cn-${code.toLowerCase()}',
          code: code,
          balance: 100,
        );

    _state = _state.copyWith(
      verifiedCreditNote: note,
      clearCreditNoteVerificationFailure: true,
    );
    state = _state;
  }

  @override
  void applyVerifiedCreditNote({double? amount}) {
    final verified = _state.verifiedCreditNote;
    if (verified == null) return;

    final alreadyApplied = _state.appliedCreditNotes.any(
      (note) =>
          note.creditNoteId == verified.creditNoteId ||
          note.code == verified.code,
    );
    if (alreadyApplied) {
      _state = _state.copyWith(
        creditNoteVerificationFailure: const Failure.validation(
          message: 'This credit note is already applied.',
        ),
      );
      state = _state;
      return;
    }

    final clampedAmount = amount == null
        ? verified.balance
        : amount.clamp(0.0, verified.balance).clamp(0.0, _state.payable);
    _state = _state.copyWith(
      appliedCreditNotes: [
        ..._state.appliedCreditNotes,
        AppliedCreditNote(
          creditNoteId: verified.creditNoteId,
          code: verified.code,
          balance: verified.balance,
          amount: clampedAmount,
          customerId: verified.customerId,
          customerName: verified.customerName,
        ),
      ],
      clearCreditNoteVerificationFailure: true,
    );
    _reconcilePaymentSplitAfterNoteChange();
  }

  @override
  void updateCreditNoteAmount(String creditNoteId, double amount) {
    final nextNotes = _state.appliedCreditNotes
        .map(
          (note) => note.creditNoteId == creditNoteId
              ? note.copyWith(amount: amount)
              : note,
        )
        .toList();

    _state = _state.copyWith(
      appliedCreditNotes: nextNotes,
      clearCreditNoteVerificationFailure: true,
    );
    _reconcilePaymentSplitAfterNoteChange();
  }

  @override
  void removeCreditNote(String creditNoteId) {
    final nextNotes = _state.appliedCreditNotes
        .where((note) => note.creditNoteId != creditNoteId)
        .toList();
    final nextConfirmed =
        nextNotes.isNotEmpty && _state.creditNoteCustomerMismatchConfirmed;
    _state = _state.copyWith(
      appliedCreditNotes: nextNotes,
      creditNoteCustomerMismatchConfirmed: nextConfirmed,
      clearCreditNoteVerificationFailure: true,
    );
    _reconcilePaymentSplitAfterNoteChange();
  }

  @override
  void confirmCreditNoteCustomerMismatch({required bool isConfirmed}) {
    _state = _state.copyWith(
      creditNoteCustomerMismatchConfirmed: isConfirmed,
    );
    _validatePayment();
    state = _state;
  }

  void _validatePayment() {
    if (_state.hasCreditNoteCustomerMismatch &&
        !_state.creditNoteCustomerMismatchConfirmed) {
      _state = _state.copyWith(
        submissionFailure: const Failure.validation(
          message:
              'Customer mismatch for credit note redemption requires confirmation.',
        ),
      );
      state = _state;
      return;
    }

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

  void _reconcilePaymentSplitAfterNoteChange() {
    if (_state.appliedCreditNotes.isEmpty) {
      _state = _state.copyWith(
        paidAmount: _state.hasExplicitPaymentSplit ? _state.payable : 0,
        dueAmount: _state.hasExplicitPaymentSplit ? _state.dueAmount : 0,
      );
      _validatePayment();
      state = _state;
      return;
    }

    if (_state.paymentMethod == PaymentMethod.credit) {
      _state = _state.copyWith(
        paymentMethod: _state.selectedCustomer == null
            ? PaymentMethod.cash
            : _state.paymentMethod,
      );
    }

    final cappedPaid = _state.paidAmount.clamp(0.0, _state.payable);
    final reconciledDue = (_state.payable - cappedPaid).clamp(
      0.0,
      double.infinity,
    );
    _state = _state.copyWith(
      paidAmount: cappedPaid,
      dueAmount: reconciledDue,
      hasExplicitPaymentSplit:
          _state.hasExplicitPaymentSplit &&
          _state.paymentMethod != PaymentMethod.credit,
    );
    _validatePayment();
    state = _state;
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

  @override
  Future<void> submit() async {
    submitCallCount += 1;
  }

  @override
  void clearRecordedSale() {
    _state = _state.copyWith(clearRecordedSale: true);
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

Customer _customer() {
  return const Customer(
    customerId: 'cust-1',
    name: 'Alice',
    phoneNumber: '9999999999',
    isActive: true,
  );
}

SaleDetail _recordedSale() {
  return SaleDetail(
    saleId: 'sale-1',
    invoiceNumber: 'INV-001',
    paymentMethod: 1,
    soldAt: DateTime.utc(2026, 5, 11, 10),
    paidAmount: 236,
    dueAmount: 0,
    totalBeforeDiscount: 236,
    totalDiscountAmount: 0,
    totalAmount: 236,
    totalTaxAmount: 0,
    customerName: 'Alice',
    customerPhone: '9999999999',
    status: 'paid',
  );
}

CreditNoteVerifyResult _verifiedCreditNote({
  String creditNoteId = 'cn-1',
  String code = 'CN-001',
  double balance = 100,
  String? customerId,
  String? customerName,
}) {
  return CreditNoteVerifyResult(
    creditNoteId: creditNoteId,
    code: code,
    balance: balance,
    customerId: customerId,
    customerName: customerName,
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

    testWidgets('shows submit failure text', (tester) async {
      await tester.pumpWidget(
        _buildApp(
          _StubNewSaleController(
            const NewSaleState(
              submitFailure: Failure.network(message: 'Failed to save sale'),
            ),
          ),
        ),
      );

      expect(find.byKey(const Key('new-sale-submit-failure')), findsOneWidget);
      expect(find.textContaining('Failed to save sale'), findsOneWidget);
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
        cartLines: const [NewSaleCartLine(sellable: goods, quantity: 2)],
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
        find.textContaining(
          'Sale-level discount is limited by configured rule.',
        ),
        findsOneWidget,
      );
      expect(find.byKey(const Key('checkout-button')), findsOneWidget);
      final checkoutButton = tester.widget<FilledButton>(
        find.byKey(const Key('checkout-button')),
      );
      expect(checkoutButton.onPressed, isNotNull);
    });

    testWidgets('checkout button calls submit', (tester) async {
      final controller = _StubNewSaleController(
        NewSaleState(
          cartLines: [NewSaleCartLine(sellable: _goods(), quantity: 1)],
          preview: _preview(),
          paidAmount: 236,
        ),
      );

      await tester.pumpWidget(_buildApp(controller));
      await tester.ensureVisible(find.byKey(const Key('checkout-button')));
      await tester.pump();

      final checkoutButton = tester.widget<FilledButton>(
        find.byKey(const Key('checkout-button')),
      );
      checkoutButton.onPressed?.call();
      await tester.pump();

      expect(controller.submitCallCount, 1);
    });

    testWidgets('shows recorded sale confirmation and actions', (tester) async {
      await tester.pumpWidget(
        _buildApp(
          _StubNewSaleController(NewSaleState(recordedSale: _recordedSale())),
        ),
      );

      expect(find.text('Invoice recorded'), findsOneWidget);
      expect(find.textContaining('INV-001'), findsOneWidget);
      expect(
        find.byKey(const Key('recorded-sale-detail-button')),
        findsOneWidget,
      );
      expect(
        find.byKey(const Key('recorded-sale-receipt-button')),
        findsOneWidget,
      );
      expect(
        find.byKey(const Key('recorded-sale-clear-button')),
        findsOneWidget,
      );
    });

    testWidgets('receipt button navigates to receipt with correct saleId', (
      tester,
    ) async {
      String? capturedSaleId;
      await tester.pumpWidget(
        ProviderScope(
          overrides: [
            newSaleControllerProvider.overrideWith(
              () => _StubNewSaleController(
                NewSaleState(recordedSale: _recordedSale()),
              ),
            ),
          ],
          child: MaterialApp(
            theme: AppTheme.lightTheme,
            localizationsDelegates: AppLocalizations.localizationsDelegates,
            supportedLocales: AppLocalizations.supportedLocales,
            home: NewSalePage(
              onReceiptRequested: (saleId) => capturedSaleId = saleId,
            ),
          ),
        ),
      );

      final receiptButton = tester.widget<TextButton>(
        find.byKey(const Key('recorded-sale-receipt-button')),
      );
      receiptButton.onPressed?.call();
      await tester.pump();

      expect(capturedSaleId, 'sale-1');
    });

    testWidgets('clear action resets recorded sale and cart state', (
      tester,
    ) async {
      final controller = _StubNewSaleController(
        NewSaleState(recordedSale: _recordedSale()),
      );
      await tester.pumpWidget(_buildApp(controller));

      final clearButton = tester.widget<FilledButton>(
        find.byKey(const Key('recorded-sale-clear-button')),
      );
      clearButton.onPressed?.call();
      await tester.pump();

      expect(find.text('Invoice recorded'), findsNothing);
      expect(find.text('Cart is empty.'), findsOneWidget);
    });

    testWidgets(
      'shows sale discount editor and error message when sale discount invalid',
      (tester) async {
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
          cartLines: const [NewSaleCartLine(sellable: goods, quantity: 2)],
          saleDiscountType: InstantDiscountType.percentage,
          saleDiscountValue: 25,
          saleDiscountError: 'Discount exceeds allowed maximum.',
          preview: _preview(),
        );

        await tester.pumpWidget(_buildApp(_StubNewSaleController(state)));

        expect(find.byKey(const Key('sale-discount-type')), findsOneWidget);
        expect(find.byKey(const Key('sale-discount-value')), findsOneWidget);
        expect(find.byKey(const Key('sale-discount-error')), findsOneWidget);
        expect(find.text('Discount exceeds allowed maximum.'), findsOneWidget);
        final checkoutButton = tester.widget<FilledButton>(
          find.byKey(const Key('checkout-button')),
        );
        expect(checkoutButton.onPressed, isNull);
      },
    );

    testWidgets(
      'shows line discount validation error and blocks invalid discount submit',
      (tester) async {
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
          cartLines: const [NewSaleCartLine(sellable: goods, quantity: 2)],
          preview: _preview(),
          itemDiscountErrors: const {
            'g1': 'Discount percentage exceeds allowed maximum.',
          },
        );

        await tester.pumpWidget(_buildApp(_StubNewSaleController(state)));

        expect(find.byKey(const Key('line-discount-type-g1')), findsOneWidget);
        expect(find.byKey(const Key('line-discount-value-g1')), findsOneWidget);
        expect(find.byKey(const Key('line-discount-error-g1')), findsOneWidget);
        expect(
          find.text('Discount percentage exceeds allowed maximum.'),
          findsOneWidget,
        );
        final checkoutButton = tester.widget<FilledButton>(
          find.byKey(const Key('checkout-button')),
        );
        expect(checkoutButton.onPressed, isNull);
      },
    );

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
      const state = NewSaleState(
        cartLines: [NewSaleCartLine(sellable: goods, quantity: 2)],
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
      const state = NewSaleState(
        cartLines: [NewSaleCartLine(sellable: goods, quantity: 2)],
        previewFailure: Failure.network(message: 'offline'),
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
      final state = NewSaleState(results: [_goods()]);
      await tester.pumpWidget(_buildApp(_StubNewSaleController(state)));

      expect(find.text('Flour'), findsOneWidget);
      expect(find.textContaining('Stock 10'), findsOneWidget);
      await tester.tap(find.byKey(const Key('add-button-g1')));
      await tester.pump();
      await tester.ensureVisible(find.byKey(const Key('decrease-g1')));
      await tester.pump();

      expect(find.text('Flour'), findsNWidgets(2));
      expect(find.text('Total'), findsOneWidget);
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
      await tester.ensureVisible(find.text('Total'));
      await tester.pump();

      expect(find.text('Qty: 1.25'), findsOneWidget);
      expect(find.textContaining('₹25.00'), findsWidgets);
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
      (tester) async {
        final controller = _StubNewSaleController(
          NewSaleState(
            cartLines: [NewSaleCartLine(sellable: _goods(), quantity: 1)],
            availableCustomers: [_customer()],
            selectedCustomer: _customer(),
            paidAmount: 20,
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

    testWidgets('verifies and applies credit note in checkout panel', (
      tester,
    ) async {
      final controller = _StubNewSaleController(
        NewSaleState(
          cartLines: [NewSaleCartLine(sellable: _goods(), quantity: 1)],
          preview: _preview(
            totalAmount: 100,
            totalTaxAmount: 0,
            totalDiscountAmount: 0,
          ),
          availableCustomers: [_customer()],
          selectedCustomer: _customer(),
        ),
        verifyResult: (code) {
          if (code == 'CN-100') {
            return _verifiedCreditNote(
              code: 'CN-100',
              balance: 40,
              customerId: 'cust-1',
              customerName: 'Alice',
            );
          }
          return null;
        },
      );

      await tester.pumpWidget(_buildApp(controller));
      await tester.enterText(
        find.byKey(const Key('credit-note-code-field')),
        'CN-100',
      );
      await tester.ensureVisible(
        find.byKey(const Key('verify-credit-note-button')),
      );
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('verify-credit-note-button')));
      await tester.pump();

      expect(find.text('Verified: CN-100'), findsOneWidget);
      expect(find.text('Balance: ₹40.00'), findsOneWidget);

      await tester.ensureVisible(
        find.byKey(const Key('apply-credit-note-button')),
      );
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('apply-credit-note-button')));
      await tester.pump();

      expect(find.text('Code: CN-100'), findsOneWidget);
      expect(find.byKey(const Key('credit-note-amount-cn-1')), findsOneWidget);
      expect(find.text('Total'), findsOneWidget);
      expect(find.text('₹100.00'), findsWidgets);
      expect(find.text('Applied credit notes'), findsOneWidget);
      expect(find.text('₹-40.00'), findsOneWidget);
    });

    testWidgets(
      'prevents duplicate credit note application after verification',
      (tester) async {
        final controller = _StubNewSaleController(
          NewSaleState(
            cartLines: [NewSaleCartLine(sellable: _goods(), quantity: 1)],
            preview: _preview(
              totalAmount: 100,
              totalTaxAmount: 0,
              totalDiscountAmount: 0,
            ),
            availableCustomers: [_customer()],
            selectedCustomer: _customer(),
          ),
          verifyResult: (code) {
            if (code == 'CN-100') {
              return _verifiedCreditNote(
                code: 'CN-100',
                balance: 40,
                customerId: 'cust-1',
                customerName: 'Alice',
              );
            }
            return null;
          },
        );

        await tester.pumpWidget(_buildApp(controller));
        await tester.enterText(
          find.byKey(const Key('credit-note-code-field')),
          'CN-100',
        );
        await tester.ensureVisible(
          find.byKey(const Key('verify-credit-note-button')),
        );
        await tester.pumpAndSettle();
        await tester.tap(find.byKey(const Key('verify-credit-note-button')));
        await tester.pump();
        await tester.ensureVisible(
          find.byKey(const Key('apply-credit-note-button')),
        );
        await tester.pumpAndSettle();
        await tester.tap(find.byKey(const Key('apply-credit-note-button')));
        await tester.pump();

        await tester.enterText(
          find.byKey(const Key('credit-note-code-field')),
          'CN-100',
        );
        await tester.ensureVisible(
          find.byKey(const Key('verify-credit-note-button')),
        );
        await tester.pumpAndSettle();
        await tester.tap(find.byKey(const Key('verify-credit-note-button')));
        await tester.pump();
        await tester.ensureVisible(
          find.byKey(const Key('apply-credit-note-button')),
        );
        await tester.pumpAndSettle();
        await tester.tap(find.byKey(const Key('apply-credit-note-button')));
        await tester.pump();

        expect(
          find.textContaining('This credit note is already applied.'),
          findsOneWidget,
        );
        expect(
          find.byKey(const Key('credit-note-amount-cn-1')),
          findsOneWidget,
        );
      },
    );

    testWidgets(
      'blocks checkout when credit note customer mismatches until confirmed',
      (tester) async {
        final controller = _StubNewSaleController(
          NewSaleState(
            cartLines: [NewSaleCartLine(sellable: _goods(), quantity: 1)],
            preview: _preview(
              totalAmount: 100,
              totalTaxAmount: 0,
              totalDiscountAmount: 0,
            ),
            availableCustomers: [_customer()],
            selectedCustomer: _customer(),
          ),
          verifyResult: (code) {
            if (code == 'CN-100') {
              return _verifiedCreditNote(
                code: 'CN-100',
                balance: 40,
                customerId: 'cust-2',
                customerName: 'Bob',
              );
            }
            return null;
          },
        );

        await tester.pumpWidget(_buildApp(controller));
        await tester.enterText(
          find.byKey(const Key('credit-note-code-field')),
          'CN-100',
        );
        await tester.ensureVisible(
          find.byKey(const Key('verify-credit-note-button')),
        );
        await tester.pumpAndSettle();
        await tester.tap(find.byKey(const Key('verify-credit-note-button')));
        await tester.pump();
        await tester.ensureVisible(
          find.byKey(const Key('apply-credit-note-button')),
        );
        await tester.pumpAndSettle();
        await tester.tap(find.byKey(const Key('apply-credit-note-button')));
        await tester.pump();

        final blockedButton = tester.widget<FilledButton>(
          find.byKey(const Key('checkout-button')),
        );
        expect(blockedButton.onPressed, isNull);

        await tester.tap(find.byKey(const Key('credit-note-mismatch-confirm')));
        await tester.pump();
        final enabledButton = tester.widget<FilledButton>(
          find.byKey(const Key('checkout-button')),
        );
        expect(enabledButton.onPressed, isNotNull);
      },
    );
  });
}
