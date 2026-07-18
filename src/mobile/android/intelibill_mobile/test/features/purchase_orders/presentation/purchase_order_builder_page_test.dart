import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/app/theme/app_theme.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_status.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_builder_controller.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/pages/purchase_order_builder_page.dart';
import 'package:intelibill_mobile/src/features/suppliers/domain/entities/supplier.dart';

class _StubBuilderController extends PurchaseOrderBuilderController {
  _StubBuilderController(this.initialState, {this.draftToSave});

  final PurchaseOrderBuilderState initialState;
  final PurchaseOrder? draftToSave;

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

  @override
  Future<PurchaseOrder?> save() async {
    final draft = draftToSave;
    if (draft != null) state = state.copyWith(savedDraft: draft);
    return draft;
  }
}

void main() {
  Widget buildApp(
    PurchaseOrderBuilderState state, {
    PurchaseOrder? draftToSave,
    GoRouter? router,
  }) {
    return ProviderScope(
      overrides: [
        purchaseOrderBuilderControllerProvider('new').overrideWith(
          () => _StubBuilderController(state, draftToSave: draftToSave),
        ),
      ],
      child: router == null
          ? MaterialApp(
              theme: AppTheme.lightTheme,
              localizationsDelegates: AppLocalizations.localizationsDelegates,
              supportedLocales: AppLocalizations.supportedLocales,
              home: const PurchaseOrderBuilderPage(target: 'new'),
            )
          : MaterialApp.router(
              theme: AppTheme.lightTheme,
              localizationsDelegates: AppLocalizations.localizationsDelegates,
              supportedLocales: AppLocalizations.supportedLocales,
              routerConfig: router,
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

  testWidgets('preserves form and shows error message on save failure', (
    tester,
  ) async {
    const failure = Failure.server(statusCode: 500);
    await tester.pumpWidget(
      buildApp(
        PurchaseOrderBuilderState(
          suppliers: [_supplier()],
          failure: failure,
          isSupplierLoadFailure: false,
        ),
      ),
    );

    expect(find.byKey(PurchaseOrderBuilderPage.pageKey), findsOneWidget);
    expect(
      find.byKey(PurchaseOrderBuilderPage.supplierFieldKey),
      findsOneWidget,
    );
    expect(find.text('Could not save purchase order draft.'), findsOneWidget);
  });

  testWidgets('renders formatted date selected through date picker', (
    tester,
  ) async {
    await tester.pumpWidget(
      buildApp(
        PurchaseOrderBuilderState(
          suppliers: [_supplier()],
        ),
      ),
    );

    final field = find.byKey(PurchaseOrderBuilderPage.orderDateFieldKey);
    final selectedDate = DateTime(
      DateTime.now().year,
      DateTime.now().month,
      15,
    );
    final formattedDate = MaterialLocalizations.of(
      tester.element(field),
    ).formatMediumDate(selectedDate);

    await tester.tap(field);
    await tester.pumpAndSettle();
    await tester.tap(find.text('15').last);
    await tester.tap(find.text('OK'));
    await tester.pumpAndSettle();

    expect(
      tester
          .widget<TextFormField>(
            find.descendant(of: field, matching: find.byType(TextFormField)),
          )
          .controller!
          .text,
      formattedDate,
    );
  });

  testWidgets(
    'navigates to saved purchase order detail after successful save',
    (
      tester,
    ) async {
      final router = GoRouter(
        initialLocation: AppRoutes.purchaseOrderNew,
        routes: [
          GoRoute(
            path: AppRoutes.purchaseOrderNew,
            builder: (_, _) => const PurchaseOrderBuilderPage(target: 'new'),
          ),
          GoRoute(
            path: AppRoutes.purchaseOrderDetail,
            builder: (_, state) =>
                Text(state.pathParameters['purchaseOrderId']!),
          ),
        ],
      );
      await tester.pumpWidget(
        buildApp(
          PurchaseOrderBuilderState(suppliers: [_supplier()]),
          draftToSave: _savedPurchaseOrder(),
          router: router,
        ),
      );

      await tester.tap(find.byKey(PurchaseOrderBuilderPage.saveButtonKey));
      await tester.pumpAndSettle();

      expect(
        router.routerDelegate.currentConfiguration.uri.path,
        AppRoutes.purchaseOrderDetailFor('po-1'),
      );
      expect(find.text('po-1'), findsOneWidget);
    },
  );
}

PurchaseOrder _savedPurchaseOrder() => PurchaseOrder(
  purchaseOrderId: 'po-1',
  purchaseOrderNumber: 'PO-2026-000001',
  status: PurchaseOrderStatus.draft,
  lines: const [],
  expectedTotal: 0,
  createdAt: DateTime(2026, 7, 19),
);

Supplier _supplier() => const Supplier(
  supplierId: 'supplier-1',
  name: 'Fresh Supplier',
  isSystem: false,
  isActive: true,
  isPreferred: true,
  balanceDue: 0,
);
