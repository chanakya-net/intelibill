import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/app/theme/app_theme.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_draft.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_builder_controller.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/pages/purchase_order_builder_page.dart';
import 'package:intelibill_mobile/src/features/suppliers/domain/entities/supplier.dart';

class _StubBuilderController extends PurchaseOrderBuilderController {
  _StubBuilderController(this.initialState);

  final PurchaseOrderBuilderState initialState;
  PurchaseOrderDraft? savedDraft;

  @override
  PurchaseOrderBuilderState build(String target) => initialState;

  @override
  void selectSupplier(Supplier? supplier) {
    state = state.copyWith(
      selectedSupplier: supplier,
      clearSupplier: supplier == null,
    );
  }

  @override
  void setOrderDate(DateTime? date) {
    state = state.copyWith(orderDate: date, clearOrderDate: date == null);
  }

  @override
  void setExpectedDeliveryDate(DateTime? date) {
    state = state.copyWith(
      expectedDeliveryDate: date,
      clearExpectedDeliveryDate: date == null,
    );
  }

  @override
  void setSupplierReferenceNumber(String value) {
    state = state.copyWith(supplierReferenceNumber: value);
  }

  @override
  void setNotes(String value) {
    state = state.copyWith(notes: value);
  }
}

void main() {
  Widget buildApp(PurchaseOrderBuilderState state) {
    return ProviderScope(
      overrides: [
        purchaseOrderBuilderControllerProvider('new').overrideWith(
          () => _StubBuilderController(state),
        ),
      ],
      child: MaterialApp(
        theme: AppTheme.lightTheme,
        localizationsDelegates: AppLocalizations.localizationsDelegates,
        supportedLocales: AppLocalizations.supportedLocales,
        home: const PurchaseOrderBuilderPage(target: 'new'),
      ),
    );
  }

  testWidgets('renders keyboard-safe header fields and sticky save action', (
    tester,
  ) async {
    await tester.pumpWidget(
      buildApp(
        PurchaseOrderBuilderState(
          suppliers: [_supplier()],
        ),
      ),
    );

    expect(find.byKey(PurchaseOrderBuilderPage.pageKey), findsOneWidget);
    expect(
      find.byKey(PurchaseOrderBuilderPage.supplierFieldKey),
      findsOneWidget,
    );
    expect(
      find.byKey(PurchaseOrderBuilderPage.orderDateFieldKey),
      findsOneWidget,
    );
    expect(
      find.byKey(PurchaseOrderBuilderPage.expectedDeliveryDateFieldKey),
      findsOneWidget,
    );
    expect(
      find.byKey(PurchaseOrderBuilderPage.referenceFieldKey),
      findsOneWidget,
    );
    expect(find.byKey(PurchaseOrderBuilderPage.notesFieldKey), findsOneWidget);
    expect(find.byKey(PurchaseOrderBuilderPage.saveButtonKey), findsOneWidget);
    expect(find.byType(SingleChildScrollView), findsOneWidget);
  });

  testWidgets('shows only the supplied active supplier option', (tester) async {
    await tester.pumpWidget(
      buildApp(PurchaseOrderBuilderState(suppliers: [_supplier()])),
    );

    await tester.tap(find.byKey(PurchaseOrderBuilderPage.supplierFieldKey));
    await tester.pumpAndSettle();

    expect(find.text('Fresh Supplier'), findsOneWidget);
  });
}

Supplier _supplier() => const Supplier(
  supplierId: 'supplier-1',
  name: 'Fresh Supplier',
  isSystem: false,
  isActive: true,
  isPreferred: true,
  balanceDue: 0,
);
