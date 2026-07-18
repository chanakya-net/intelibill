import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_line.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_status.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_detail_controller.dart';

void main() {
  group('PurchaseOrderDetailState.cancelState', () {
    test('initial cancel state is not loading with no failure', () {
      const state = PurchaseOrderDetailState();
      expect(state.cancelState.isLoading, isFalse);
      expect(state.cancelState.failure, isNull);
    });

    test('copyWith updates cancelState with isLoading', () {
      const initialState = PurchaseOrderDetailState();
      final updated = initialState.copyWith(
        cancelState: initialState.cancelState.copyWith(isLoading: true),
      );

      expect(updated.cancelState.isLoading, isTrue);
      expect(updated.cancelState.failure, isNull);
    });

    test('copyWith clears cancel failure on subsequent attempt', () {
      var state = const PurchaseOrderDetailState();
      state = state.copyWith(
        cancelState: state.cancelState.copyWith(
          isLoading: false,
          failure: const Failure.server(message: 'error'),
        ),
      );
      expect(state.cancelState.failure, isNotNull);

      state = state.copyWith(
        cancelState: state.cancelState.copyWith(
          isLoading: true,
          clearFailure: true,
        ),
      );

      expect(state.cancelState.failure, isNull);
      expect(state.cancelState.isLoading, isTrue);
    });

    test('preserves detail when setting cancel failure', () {
      final po = _placedPO();
      var state = PurchaseOrderDetailState(detail: po);

      state = state.copyWith(
        cancelState: state.cancelState.copyWith(
          isLoading: false,
          failure: const Failure.server(message: 'conflict'),
        ),
      );

      expect(state.detail, po);
      expect(state.cancelState.failure, isNotNull);
    });

    test('updates detail when cancel succeeds', () {
      final placed = _placedPO();
      final cancelled = _cancelledPO();

      var state = PurchaseOrderDetailState(detail: placed);
      state = state.copyWith(
        detail: cancelled,
        cancelState: state.cancelState.copyWith(isLoading: false),
      );

      expect(state.detail?.status, PurchaseOrderStatus.cancelled);
      expect(state.detail?.cancellationReason, 'No longer needed');
      expect(state.cancelState.isLoading, isFalse);
    });
  });
}

PurchaseOrder _placedPO() {
  return PurchaseOrder(
    purchaseOrderId: 'po-1',
    purchaseOrderNumber: 'PO-001',
    status: PurchaseOrderStatus.placed,
    lines: const [
      PurchaseOrderLine(
        lineId: 'line-1',
        itemId: 'item-1',
        description: 'Widget',
        expectedQuantity: 10,
        receivedQuantity: 0,
        remainingQuantity: 10,
        unitCost: 10.0,
        lineTotal: 100.0,
      ),
    ],
    expectedTotal: 100.0,
    createdAt: DateTime.utc(2026, 7, 1),
  );
}

PurchaseOrder _cancelledPO() {
  return PurchaseOrder(
    purchaseOrderId: 'po-1',
    purchaseOrderNumber: 'PO-001',
    status: PurchaseOrderStatus.cancelled,
    lines: const [
      PurchaseOrderLine(
        lineId: 'line-1',
        itemId: 'item-1',
        description: 'Widget',
        expectedQuantity: 10,
        receivedQuantity: 0,
        remainingQuantity: 10,
        unitCost: 10.0,
        lineTotal: 100.0,
      ),
    ],
    expectedTotal: 100.0,
    createdAt: DateTime.utc(2026, 7, 1),
    cancellationReason: 'No longer needed',
  );
}
