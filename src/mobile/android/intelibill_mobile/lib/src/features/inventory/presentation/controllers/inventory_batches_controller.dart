import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/entities/inventory_batch.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/use_cases/adjust_inventory_batch.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/use_cases/get_inventory_batches.dart';
import 'package:intelibill_mobile/src/features/inventory/presentation/controllers/items_controller.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'inventory_batches_controller.g.dart';

@riverpod
GetInventoryBatches getInventoryBatches(Ref ref) {
  final repository = ref.watch(inventoryRepositoryProvider);
  return GetInventoryBatches(repository);
}

@riverpod
AdjustInventoryBatch adjustInventoryBatch(Ref ref) {
  final repository = ref.watch(inventoryRepositoryProvider);
  return AdjustInventoryBatch(repository);
}

@immutable
class InventoryBatchesState {
  const InventoryBatchesState({
    this.batches = const [],
    this.searchQuery = '',
    this.isLoading = false,
    this.isSubmitting = false,
    this.failure,
    this.submitFailure,
    this.lastAdjustedBatchId,
  });

  final List<InventoryBatch> batches;
  final String searchQuery;
  final bool isLoading;
  final bool isSubmitting;
  final Failure? failure;
  final Failure? submitFailure;
  final String? lastAdjustedBatchId;

  List<InventoryBatch> get filteredBatches {
    if (searchQuery.isEmpty) return batches;
    final query = searchQuery.toLowerCase();
    return batches.where((batch) {
      return batch.itemName.toLowerCase().contains(query) ||
          batch.batchNumber.toLowerCase().contains(query);
    }).toList();
  }

  InventoryBatchesState copyWith({
    List<InventoryBatch>? batches,
    String? searchQuery,
    bool? isLoading,
    bool? isSubmitting,
    Failure? failure,
    Failure? submitFailure,
    String? lastAdjustedBatchId,
    bool clearError = false,
    bool clearSubmitError = false,
    bool clearLastAdjustedBatchId = false,
  }) {
    return InventoryBatchesState(
      batches: batches ?? this.batches,
      searchQuery: searchQuery ?? this.searchQuery,
      isLoading: isLoading ?? this.isLoading,
      isSubmitting: isSubmitting ?? this.isSubmitting,
      failure: clearError ? null : (failure ?? this.failure),
      submitFailure: clearSubmitError
          ? null
          : (submitFailure ?? this.submitFailure),
      lastAdjustedBatchId: clearLastAdjustedBatchId
          ? null
          : (lastAdjustedBatchId ?? this.lastAdjustedBatchId),
    );
  }
}

@riverpod
class InventoryBatchesController extends _$InventoryBatchesController {
  @override
  InventoryBatchesState build() {
    unawaited(Future.microtask(_load));
    return const InventoryBatchesState(isLoading: true);
  }

  Future<void> _load() async {
    final useCase = ref.read(getInventoryBatchesProvider);
    try {
      final batches = await useCase();
      if (!ref.mounted) return;
      state = state.copyWith(batches: batches, isLoading: false);
    } on AppException catch (error) {
      if (!ref.mounted) return;
      state = state.copyWith(isLoading: false, failure: error.failure);
    } on Object {
      if (!ref.mounted) return;
      state = state.copyWith(
        isLoading: false,
        failure: const Failure.unknown(),
      );
    }
  }

  Future<void> refresh() async {
    state = state.copyWith(isLoading: true, clearError: true);
    await _load();
  }

  void updateSearch(String query) {
    state = state.copyWith(searchQuery: query);
  }

  Future<void> adjustBatch({
    required String batchId,
    required String direction,
    required String reason,
    required double quantity,
    DateTime? performedAt,
    String? notes,
  }) async {
    if (state.isSubmitting) return;
    state = state.copyWith(
      isSubmitting: true,
      clearSubmitError: true,
      clearLastAdjustedBatchId: true,
    );
    final useCase = ref.read(adjustInventoryBatchProvider);
    try {
      await useCase(
        batchId: batchId,
        direction: direction,
        reason: reason,
        quantity: quantity,
        performedAt: performedAt,
        notes: notes,
      );
      if (!ref.mounted) return;
      state = state.copyWith(isSubmitting: false, lastAdjustedBatchId: batchId);
      await refresh();
    } on AppException catch (error) {
      if (!ref.mounted) return;
      state = state.copyWith(isSubmitting: false, submitFailure: error.failure);
    } on Object {
      if (!ref.mounted) return;
      state = state.copyWith(
        isSubmitting: false,
        submitFailure: const Failure.unknown(),
      );
    }
  }
}
