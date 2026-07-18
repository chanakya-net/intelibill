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
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/close_purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/use_cases/get_purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_providers.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_detail_controller.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/pages/purchase_order_detail_page.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/widgets/purchase_order_close_sheet.dart';
import 'package:mocktail/mocktail.dart';

class _MockGetPurchaseOrder extends Mock implements GetPurchaseOrder {}

class _MockClosePurchaseOrder extends Mock implements ClosePurchaseOrder {}

void main() {
  testWidgets('shows close only for PartiallyReceived', (tester) async {
    final get = _MockGetPurchaseOrder();
    when(() => get(any())).thenAnswer(
      (_) async => _detail(status: PurchaseOrderStatus.partiallyReceived),
    );
    final container = ProviderContainer(
      overrides: [getPurchaseOrderProvider.overrideWithValue(get)],
    );
    addTearDown(container.dispose);

    await tester.pumpWidget(_app(container, 'po-1'));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 50));

    expect(
      find.byKey(const Key('purchase-order-detail-close-button')),
      findsOneWidget,
    );
  });

  testWidgets('submits close reason and renders server metadata', (
    tester,
  ) async {
    final get = _MockGetPurchaseOrder();
    final close = _MockClosePurchaseOrder();
    when(() => get(any())).thenAnswer(
      (_) async => _detail(status: PurchaseOrderStatus.partiallyReceived),
    );
    when(() => close(any(), any())).thenAnswer(
      (_) async => _detail(
        status: PurchaseOrderStatus.closed,
        closedBy: 'server-user',
        closeReason: 'Server reason',
        closedAt: DateTime.utc(2026, 7, 15, 10, 30),
      ),
    );
    final container = ProviderContainer(
      overrides: [
        getPurchaseOrderProvider.overrideWithValue(get),
        closePurchaseOrderProvider.overrideWithValue(close),
      ],
    );
    addTearDown(container.dispose);

    await tester.pumpWidget(_app(container, 'po-1'));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 50));
    await tester.tap(
      find.byKey(const Key('purchase-order-detail-close-button')),
    );
    await tester.pumpAndSettle();
    await tester.enterText(find.byType(TextField), '  User reason  ');
    await tester.pump();
    await tester.tap(find.widgetWithText(ElevatedButton, 'Close Order'));
    await tester.pumpAndSettle();

    verify(() => close('po-1', 'User reason')).called(1);
    expect(find.byType(PurchaseOrderCloseSheet), findsNothing);
  });

  test(
    'conflict refreshes authoritative detail and retains mutation failure',
    () async {
      final get = _MockGetPurchaseOrder();
      final close = _MockClosePurchaseOrder();
      var reads = 0;
      when(() => get('po-1')).thenAnswer((_) async {
        reads++;
        return _detail(
          status: reads == 1
              ? PurchaseOrderStatus.partiallyReceived
              : PurchaseOrderStatus.closed,
          closeReason: reads == 1 ? null : 'Authoritative reason',
        );
      });
      const conflict = Failure.server(statusCode: 409, message: 'Conflict');
      when(
        () => close('po-1', any()),
      ).thenThrow(AppException(failure: conflict));
      final container = ProviderContainer(
        overrides: [
          getPurchaseOrderProvider.overrideWithValue(get),
          closePurchaseOrderProvider.overrideWithValue(close),
        ],
      );
      addTearDown(container.dispose);
      container.listen(
        purchaseOrderDetailControllerProvider('po-1'),
        (_, __) {},
      );
      await Future<void>.delayed(const Duration(milliseconds: 20));

      await expectLater(
        container
            .read(purchaseOrderDetailControllerProvider('po-1').notifier)
            .close('Reason'),
        throwsA(isA<AppException>()),
      );
      final state = container.read(
        purchaseOrderDetailControllerProvider('po-1'),
      );
      expect(reads, 2);
      expect(state.detail?.status, PurchaseOrderStatus.closed);
      expect(state.detail?.closeReason, 'Authoritative reason');
      expect(state.closeState.failure, conflict);
    },
  );

  test('retains loaded detail when close failure refresh also fails', () async {
    final get = _MockGetPurchaseOrder();
    final close = _MockClosePurchaseOrder();
    when(() => get(any())).thenAnswer((_) async => _detail());
    when(() => close(any(), any())).thenThrow(
      AppException(failure: const Failure.network(message: 'offline')),
    );
    final container = ProviderContainer(
      overrides: [
        getPurchaseOrderProvider.overrideWithValue(get),
        closePurchaseOrderProvider.overrideWithValue(close),
      ],
    );
    addTearDown(container.dispose);
    container.listen(purchaseOrderDetailControllerProvider('po-1'), (_, __) {});
    await Future<void>.delayed(const Duration(milliseconds: 20));
    final before = container
        .read(purchaseOrderDetailControllerProvider('po-1'))
        .detail;

    await expectLater(
      container
          .read(purchaseOrderDetailControllerProvider('po-1').notifier)
          .close('Reason'),
      throwsA(isA<AppException>()),
    );
    final state = container.read(purchaseOrderDetailControllerProvider('po-1'));
    expect(state.detail, before);
    expect(state.closeState.failure, isA<NetworkFailure>());
  });
}

Widget _app(ProviderContainer container, String id) {
  return UncontrolledProviderScope(
    container: container,
    child: MaterialApp(
      theme: AppTheme.lightTheme,
      supportedLocales: const [Locale('en', 'IN')],
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      home: PurchaseOrderDetailPage(purchaseOrderId: id),
    ),
  );
}

PurchaseOrder _detail({
  PurchaseOrderStatus status = PurchaseOrderStatus.partiallyReceived,
  DateTime? closedAt,
  String? closedBy,
  String? closeReason,
}) {
  return PurchaseOrder(
    purchaseOrderId: 'po-1',
    purchaseOrderNumber: 'PO-001',
    status: status,
    lines: const [
      PurchaseOrderLine(
        lineId: 'line-1',
        itemId: 'item-1',
        description: 'Widget',
        expectedQuantity: 10,
        receivedQuantity: 5,
        remainingQuantity: 5,
        unitCost: 10,
        lineTotal: 100,
      ),
    ],
    expectedTotal: 100,
    createdAt: DateTime.utc(2026, 7, 1),
    receivedQuantity: 5,
    closedAt: closedAt,
    closedBy: closedBy,
    closeReason: closeReason,
  );
}
