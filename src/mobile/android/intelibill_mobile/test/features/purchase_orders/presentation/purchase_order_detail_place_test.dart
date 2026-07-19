import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/app/theme/app_theme.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_line.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_status.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/get_purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/place_purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_providers.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/pages/purchase_order_detail_page.dart';
import 'package:intelibill_mobile/src/features/suppliers/domain/entities/supplier.dart';
import 'package:intelibill_mobile/src/features/suppliers/presentation/controllers/suppliers_controller.dart';
import 'package:mocktail/mocktail.dart';

class _MockSuppliersController extends Mock implements SuppliersState {}

class _MockGetPurchaseOrder extends Mock implements GetPurchaseOrder {}

class _MockPlacePurchaseOrder extends Mock implements PlacePurchaseOrder {}

_SimpleHarness _buildHarness({
  required GetPurchaseOrder getPurchaseOrder,
  required PlacePurchaseOrder placePurchaseOrder,
  required List<Supplier> suppliers,
  required String purchaseOrderId,
  Locale? locale,
}) {
  final suppliersState = SuppliersState(suppliers: suppliers);
  final container = ProviderContainer(
    overrides: [
      getPurchaseOrderProvider.overrideWithValue(getPurchaseOrder),
      placePurchaseOrderProvider.overrideWithValue(placePurchaseOrder),
      suppliersControllerProvider.overrideWithValue(suppliersState),
    ],
  );

  return _SimpleHarness(
    container: container,
    app: UncontrolledProviderScope(
      container: container,
      child: MaterialApp(
        theme: AppTheme.lightTheme,
        locale: locale,
        supportedLocales: AppLocalizations.supportedLocales,
        localizationsDelegates: AppLocalizations.localizationsDelegates,
        home: PurchaseOrderDetailPage(purchaseOrderId: purchaseOrderId),
      ),
    ),
  );
}

class _SimpleHarness {
  _SimpleHarness({required this.container, required this.app});
  final ProviderContainer container;
  final Widget app;
}

PurchaseOrder _draftPo({
  String purchaseOrderId = 'po-1',
  String? supplierId = 'supplier-1',
  List<PurchaseOrderLine>? lines,
  PurchaseOrderStatus status = PurchaseOrderStatus.draft,
}) {
  return PurchaseOrder(
    purchaseOrderId: purchaseOrderId,
    purchaseOrderNumber: 'PO-001',
    status: status,
    supplierId: supplierId,
    lines:
        lines ??
        [
          const PurchaseOrderLine(
            lineId: 'line-1',
            itemId: 'item-1',
            description: 'Test Item',
            expectedQuantity: 10,
            receivedQuantity: 0,
            remainingQuantity: 10,
            unitCost: 50.0,
            lineTotal: 500.0,
          ),
        ],
    expectedTotal: 500.0,
    createdAt: DateTime.now(),
  );
}

Supplier _activeSupplier({
  String supplierId = 'supplier-1',
  bool isActive = true,
}) {
  return Supplier(
    supplierId: supplierId,
    name: 'Supplier 1',
    address: '123 Street',
    isSystem: false,
    isActive: isActive,
    isPreferred: false,
    balanceDue: 0.0,
  );
}

void main() {
  group('PurchaseOrderDetailPage - Place Lifecycle', () {
    testWidgets('shows Place button for Draft status with active supplier', (
      tester,
    ) async {
      final getPurchaseOrder = _MockGetPurchaseOrder();
      final placePurchaseOrder = _MockPlacePurchaseOrder();
      when(() => getPurchaseOrder('po-1')).thenAnswer(
        (_) async => _draftPo(),
      );

      final harness = _buildHarness(
        getPurchaseOrder: getPurchaseOrder,
        placePurchaseOrder: placePurchaseOrder,
        suppliers: [_activeSupplier()],
        purchaseOrderId: 'po-1',
      );

      addTearDown(harness.container.dispose);
      await tester.pumpWidget(harness.app);
      await tester.pumpAndSettle();
      expect(
        find.byKey(const Key('purchase-order-detail-place-button')),
        findsOneWidget,
      );
    });

    testWidgets('disables Place button when supplier is inactive', (
      tester,
    ) async {
      final getPurchaseOrder = _MockGetPurchaseOrder();
      final placePurchaseOrder = _MockPlacePurchaseOrder();
      when(() => getPurchaseOrder('po-1')).thenAnswer(
        (_) async => _draftPo(),
      );

      final harness = _buildHarness(
        getPurchaseOrder: getPurchaseOrder,
        placePurchaseOrder: placePurchaseOrder,
        suppliers: [_activeSupplier(isActive: false)],
        purchaseOrderId: 'po-1',
      );

      addTearDown(harness.container.dispose);
      await tester.pumpWidget(harness.app);
      await tester.pumpAndSettle();
      final button = find.byKey(
        const Key('purchase-order-detail-place-button'),
      );
      expect(button, findsOneWidget);
      expect(
        (tester.widget<IconButton>(button)).onPressed,
        isNull,
      );
    });

    testWidgets('does not offer Place when the supplier is missing', (
      tester,
    ) async {
      final getPurchaseOrder = _MockGetPurchaseOrder();
      final placePurchaseOrder = _MockPlacePurchaseOrder();
      when(() => getPurchaseOrder('po-1')).thenAnswer((_) async => _draftPo());

      final harness = _buildHarness(
        getPurchaseOrder: getPurchaseOrder,
        placePurchaseOrder: placePurchaseOrder,
        suppliers: const [],
        purchaseOrderId: 'po-1',
      );
      addTearDown(harness.container.dispose);
      await tester.pumpWidget(harness.app);
      await tester.pumpAndSettle();

      expect(
        tester
            .widget<IconButton>(
              find.byKey(const Key('purchase-order-detail-place-button')),
            )
            .onPressed,
        isNull,
      );
    });

    testWidgets('disables Place button when no valid lines', (tester) async {
      final getPurchaseOrder = _MockGetPurchaseOrder();
      final placePurchaseOrder = _MockPlacePurchaseOrder();
      when(() => getPurchaseOrder('po-1')).thenAnswer(
        (_) async => _draftPo(
          lines: [
            const PurchaseOrderLine(
              lineId: 'line-1',
              itemId: 'item-1',
              description: 'Test Item',
              expectedQuantity: 0,
              receivedQuantity: 0,
              remainingQuantity: 0,
              unitCost: 50.0,
              lineTotal: 0.0,
            ),
          ],
        ),
      );

      final harness = _buildHarness(
        getPurchaseOrder: getPurchaseOrder,
        placePurchaseOrder: placePurchaseOrder,
        suppliers: [_activeSupplier()],
        purchaseOrderId: 'po-1',
      );

      addTearDown(harness.container.dispose);
      await tester.pumpWidget(harness.app);
      await tester.pumpAndSettle();
      final button = find.byKey(
        const Key('purchase-order-detail-place-button'),
      );
      expect(button, findsOneWidget);
      expect(
        (tester.widget<IconButton>(button)).onPressed,
        isNull,
      );
    });

    testWidgets('shows confirmation sheet on Place button tap', (tester) async {
      final getPurchaseOrder = _MockGetPurchaseOrder();
      final placePurchaseOrder = _MockPlacePurchaseOrder();
      when(() => getPurchaseOrder('po-1')).thenAnswer(
        (_) async => _draftPo(),
      );
      when(() => placePurchaseOrder('po-1')).thenAnswer(
        (_) async => _draftPo(supplierId: 'supplier-1'),
      );

      final harness = _buildHarness(
        getPurchaseOrder: getPurchaseOrder,
        placePurchaseOrder: placePurchaseOrder,
        suppliers: [_activeSupplier()],
        purchaseOrderId: 'po-1',
      );

      addTearDown(harness.container.dispose);
      await tester.pumpWidget(harness.app);
      await tester.pump();
      await tester.tap(
        find.byKey(const Key('purchase-order-detail-place-button')),
      );
      await tester.pumpAndSettle();
      expect(find.text('Place purchase order'), findsOneWidget);
      expect(
        find.text('Are you sure you want to place this purchase order?'),
        findsOneWidget,
      );
    });

    testWidgets('calls place use case on confirmation', (tester) async {
      final getPurchaseOrder = _MockGetPurchaseOrder();
      final placePurchaseOrder = _MockPlacePurchaseOrder();
      final placed = _draftPo();
      when(() => getPurchaseOrder('po-1')).thenAnswer(
        (_) async => _draftPo(),
      );
      when(() => placePurchaseOrder('po-1')).thenAnswer(
        (_) async => placed,
      );

      final harness = _buildHarness(
        getPurchaseOrder: getPurchaseOrder,
        placePurchaseOrder: placePurchaseOrder,
        suppliers: [_activeSupplier()],
        purchaseOrderId: 'po-1',
      );

      addTearDown(harness.container.dispose);
      await tester.pumpWidget(harness.app);
      await tester.pump();
      await tester.tap(
        find.byKey(const Key('purchase-order-detail-place-button')),
      );
      await tester.pumpAndSettle();
      await tester.tap(find.text('Place order'));
      await tester.pumpAndSettle();
      verify(() => placePurchaseOrder('po-1')).called(1);
    });

    testWidgets('guards duplicate confirmation while place is pending', (
      tester,
    ) async {
      final getPurchaseOrder = _MockGetPurchaseOrder();
      final placePurchaseOrder = _MockPlacePurchaseOrder();
      when(() => getPurchaseOrder('po-1')).thenAnswer(
        (_) async => _draftPo(),
      );
      final pending = Completer<PurchaseOrder>();
      when(() => placePurchaseOrder('po-1')).thenAnswer((_) => pending.future);

      final harness = _buildHarness(
        getPurchaseOrder: getPurchaseOrder,
        placePurchaseOrder: placePurchaseOrder,
        suppliers: [_activeSupplier()],
        purchaseOrderId: 'po-1',
      );

      addTearDown(harness.container.dispose);
      await tester.pumpWidget(harness.app);
      await tester.pump();
      await tester.tap(
        find.byKey(const Key('purchase-order-detail-place-button')),
      );
      await tester.pumpAndSettle();
      await tester.tap(find.text('Place order'));
      await tester.pump();
      await tester.tap(find.byType(ElevatedButton));
      await tester.pump();
      verify(() => placePurchaseOrder('po-1')).called(1);
      pending.complete(_draftPo(status: PurchaseOrderStatus.placed));
      await tester.pumpAndSettle();
      expect(find.text('Purchase order placed successfully.'), findsOneWidget);
    });

    testWidgets('shows failure message on place error', (tester) async {
      final getPurchaseOrder = _MockGetPurchaseOrder();
      final placePurchaseOrder = _MockPlacePurchaseOrder();
      when(() => getPurchaseOrder('po-1')).thenAnswer(
        (_) async => _draftPo(),
      );
      when(() => placePurchaseOrder('po-1')).thenThrow(
        AppException(failure: const Failure.unknown()),
      );

      final harness = _buildHarness(
        getPurchaseOrder: getPurchaseOrder,
        placePurchaseOrder: placePurchaseOrder,
        suppliers: [_activeSupplier()],
        purchaseOrderId: 'po-1',
      );

      addTearDown(harness.container.dispose);
      await tester.pumpWidget(harness.app);
      await tester.pump();
      await tester.tap(
        find.byKey(const Key('purchase-order-detail-place-button')),
      );
      await tester.pumpAndSettle();
      await tester.tap(find.text('Place order'));
      await tester.pump();
      expect(
        find.text('Could not place purchase order. Please try again.'),
        findsOneWidget,
      );
    });

    testWidgets('keeps confirmation open after cancellation', (tester) async {
      final getPurchaseOrder = _MockGetPurchaseOrder();
      final placePurchaseOrder = _MockPlacePurchaseOrder();
      when(() => getPurchaseOrder('po-1')).thenAnswer((_) async => _draftPo());
      final harness = _buildHarness(
        getPurchaseOrder: getPurchaseOrder,
        placePurchaseOrder: placePurchaseOrder,
        suppliers: [_activeSupplier()],
        purchaseOrderId: 'po-1',
      );
      addTearDown(harness.container.dispose);
      await tester.pumpWidget(harness.app);
      await tester.pump();
      await tester.tap(
        find.byKey(const Key('purchase-order-detail-place-button')),
      );
      await tester.pumpAndSettle();
      await tester.tap(find.text('Cancel'));
      await tester.pumpAndSettle();

      verifyNever(() => placePurchaseOrder(any()));
    });

    testWidgets('localizes confirmation, success, and failure messages', (
      tester,
    ) async {
      final getPurchaseOrder = _MockGetPurchaseOrder();
      final placePurchaseOrder = _MockPlacePurchaseOrder();
      when(() => getPurchaseOrder('po-1')).thenAnswer((_) async => _draftPo());
      when(() => placePurchaseOrder('po-1')).thenThrow(
        AppException(failure: const Failure.network()),
      );
      final harness = _buildHarness(
        getPurchaseOrder: getPurchaseOrder,
        placePurchaseOrder: placePurchaseOrder,
        suppliers: [_activeSupplier()],
        purchaseOrderId: 'po-1',
        locale: const Locale('hi', 'IN'),
      );
      addTearDown(harness.container.dispose);
      await tester.pumpWidget(harness.app);
      await tester.pump();
      await tester.tap(
        find.byKey(const Key('purchase-order-detail-place-button')),
      );
      await tester.pumpAndSettle();
      expect(find.text('खरीद आदेश भेजें'), findsOneWidget);
      expect(
        find.text('क्या आप यह खरीद आदेश भेजना चाहते हैं?'),
        findsOneWidget,
      );
      await tester.tap(find.text('आदेश भेजें'));
      await tester.pumpAndSettle();
      expect(
        find.text('खरीद आदेश भेजा नहीं जा सका। कृपया पुनः प्रयास करें।'),
        findsOneWidget,
      );
      when(() => placePurchaseOrder('po-1')).thenAnswer(
        (_) async => _draftPo(status: PurchaseOrderStatus.placed),
      );
      await tester.tap(find.text('आदेश भेजें'));
      await tester.pumpAndSettle();
      expect(find.text('खरीद आदेश भेज दिया गया।'), findsOneWidget);
    });
  });
}
