import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/use_cases/add_inventory_inbound.dart';
import 'package:intelibill_mobile/src/features/inventory/presentation/controllers/add_inventory_controller.dart';
import 'package:mocktail/mocktail.dart';

class MockAddInventoryInbound extends Mock implements AddInventoryInbound {}

void main() {
  late MockAddInventoryInbound addInventoryInbound;

  setUp(() {
    addInventoryInbound = MockAddInventoryInbound();
  });

  ProviderContainer makeContainer() {
    return ProviderContainer(
      overrides: [
        addInventoryInboundProvider.overrideWithValue(addInventoryInbound),
      ],
    );
  }

  Future<void> submit(AddInventoryController controller) {
    return controller.submitInbound(
      itemName: 'Item A',
      barcode: 'BAR-001',
      uom: 'pcs',
      quantity: 10,
      costPrice: 50,
      mrp: 75,
      salesPrice: 70,
    );
  }

  group('AddInventoryController', () {
    test('starts with initial state', () {
      final container = makeContainer();
      addTearDown(container.dispose);

      final state = container.read(addInventoryControllerProvider);
      expect(state.isSubmitting, false);
      expect(state.submitFailure, isNull);
      expect(state.lastInboundSucceeded, false);
    });

    test('sets success state on inbound submit', () async {
      when(
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
      ).thenAnswer((_) async {});

      final container = makeContainer();
      addTearDown(container.dispose);

      await submit(container.read(addInventoryControllerProvider.notifier));

      final state = container.read(addInventoryControllerProvider);
      expect(state.isSubmitting, false);
      expect(state.submitFailure, isNull);
      expect(state.lastInboundSucceeded, true);
    });

    test('stores failure when inbound submit throws', () async {
      when(
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
      ).thenThrow(AppException(failure: const Failure.network()));

      final container = makeContainer();
      addTearDown(container.dispose);

      await submit(container.read(addInventoryControllerProvider.notifier));

      final state = container.read(addInventoryControllerProvider);
      expect(state.isSubmitting, false);
      expect(state.submitFailure, isA<NetworkFailure>());
      expect(state.lastInboundSucceeded, false);
    });

    test('retry clears previous failure', () async {
      final completer = Completer<void>();
      when(
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
      ).thenAnswer((_) => completer.future);

      final container = makeContainer();
      addTearDown(container.dispose);

      final controller =
          container.read(addInventoryControllerProvider.notifier)
            ..state = const AddInventoryState(
              submitFailure: Failure.network(),
            );

      final submitFuture = submit(controller);
      final state = container.read(addInventoryControllerProvider);

      expect(state.isSubmitting, true);
      expect(state.submitFailure, isNull);

      completer.complete();
      await submitFuture;

      final finishedState = container.read(addInventoryControllerProvider);
      expect(finishedState.lastInboundSucceeded, true);
      expect(finishedState.submitFailure, isNull);
    });
  });
}
