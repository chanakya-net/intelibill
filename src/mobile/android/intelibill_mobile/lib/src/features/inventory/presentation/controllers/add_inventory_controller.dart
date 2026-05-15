import 'package:flutter/foundation.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/use_cases/add_inventory_inbound.dart';
import 'package:intelibill_mobile/src/features/inventory/presentation/controllers/items_controller.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'add_inventory_controller.g.dart';

@riverpod
AddInventoryInbound addInventoryInbound(Ref ref) {
  final repository = ref.watch(inventoryRepositoryProvider);
  return AddInventoryInbound(repository);
}

@immutable
class AddInventoryState {
  const AddInventoryState({
    this.isSubmitting = false,
    this.submitFailure,
    this.lastInboundSucceeded = false,
  });

  final bool isSubmitting;
  final Failure? submitFailure;
  final bool lastInboundSucceeded;

  AddInventoryState copyWith({
    bool? isSubmitting,
    Failure? submitFailure,
    bool? lastInboundSucceeded,
    bool clearSubmitFailure = false,
  }) {
    return AddInventoryState(
      isSubmitting: isSubmitting ?? this.isSubmitting,
      submitFailure: clearSubmitFailure
          ? null
          : (submitFailure ?? this.submitFailure),
      lastInboundSucceeded: lastInboundSucceeded ?? this.lastInboundSucceeded,
    );
  }
}

@riverpod
class AddInventoryController extends _$AddInventoryController {
  @override
  AddInventoryState build() => const AddInventoryState();

  Future<void> submitInbound({
    required String itemName,
    required String barcode,
    required String uom,
    String? batchNumber,
    required double quantity,
    required double costPrice,
    required double mrp,
    required double salesPrice,
    double taxRate = 0,
    bool taxIncluded = false,
    DateTime? expiryDate,
    DateTime? manufacturingDate,
    String? referenceNumber,
    String? notes,
  }) async {
    if (state.isSubmitting) return;
    state = state.copyWith(
      isSubmitting: true,
      clearSubmitFailure: true,
      lastInboundSucceeded: false,
    );

    final useCase = ref.read(addInventoryInboundProvider);
    try {
      await useCase(
        itemName: itemName,
        barcode: barcode,
        uom: uom,
        batchNumber: batchNumber,
        quantity: quantity,
        costPrice: costPrice,
        mrp: mrp,
        salesPrice: salesPrice,
        taxRate: taxRate,
        taxIncluded: taxIncluded,
        expiryDate: expiryDate,
        manufacturingDate: manufacturingDate,
        referenceNumber: referenceNumber,
        notes: notes,
      );
      if (!ref.mounted) return;
      state = state.copyWith(
        isSubmitting: false,
        lastInboundSucceeded: true,
        clearSubmitFailure: true,
      );
    } on AppException catch (error) {
      if (!ref.mounted) return;
      state = state.copyWith(
        isSubmitting: false,
        submitFailure: error.failure,
      );
    } on Object {
      if (!ref.mounted) return;
      state = state.copyWith(
        isSubmitting: false,
        submitFailure: const Failure.unknown(),
      );
    }
  }
}
