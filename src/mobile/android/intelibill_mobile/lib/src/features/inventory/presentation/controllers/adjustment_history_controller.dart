import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/entities/inventory_adjustment.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/use_cases/get_adjustment_history.dart';
import 'package:intelibill_mobile/src/features/inventory/presentation/controllers/items_controller.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'adjustment_history_controller.g.dart';

@riverpod
GetAdjustmentHistory getAdjustmentHistory(Ref ref) {
  final repository = ref.watch(inventoryRepositoryProvider);
  return GetAdjustmentHistory(repository);
}

@immutable
class AdjustmentHistoryState {
  const AdjustmentHistoryState({
    this.adjustments = const [],
    this.isLoading = false,
    this.isLoadingMore = false,
    this.failure,
    this.pageNumber = 1,
    this.hasMore = true,
  });

  final List<InventoryAdjustment> adjustments;
  final bool isLoading;
  final bool isLoadingMore;
  final AppException? failure;
  final int pageNumber;
  final bool hasMore;

  AdjustmentHistoryState copyWith({
    List<InventoryAdjustment>? adjustments,
    bool? isLoading,
    bool? isLoadingMore,
    AppException? failure,
    bool clearFailure = false,
    int? pageNumber,
    bool? hasMore,
  }) {
    return AdjustmentHistoryState(
      adjustments: adjustments ?? this.adjustments,
      isLoading: isLoading ?? this.isLoading,
      isLoadingMore: isLoadingMore ?? this.isLoadingMore,
      failure: clearFailure ? null : (failure ?? this.failure),
      pageNumber: pageNumber ?? this.pageNumber,
      hasMore: hasMore ?? this.hasMore,
    );
  }
}

@riverpod
class AdjustmentHistoryController extends _$AdjustmentHistoryController {
  static const _pageSize = 50;

  @override
  AdjustmentHistoryState build() {
    unawaited(Future.microtask(_load));
    return const AdjustmentHistoryState(isLoading: true);
  }

  Future<void> _load({bool refresh = false}) async {
    final useCase = ref.read(getAdjustmentHistoryProvider);
    try {
      final result = await useCase(pageNumber: 1, pageSize: _pageSize);
      if (!ref.mounted) return;
      state = state.copyWith(
        adjustments: result.items,
        isLoading: false,
        isLoadingMore: false,
        pageNumber: 1,
        hasMore: result.hasMore,
        clearFailure: true,
      );
    } on AppException catch (error) {
      if (!ref.mounted) return;
      state = state.copyWith(
        isLoading: false,
        isLoadingMore: false,
        failure: error,
      );
    } on Object {
      if (!ref.mounted) return;
      state = state.copyWith(isLoading: false, isLoadingMore: false);
    }
  }

  Future<void> refresh() async {
    state = state.copyWith(isLoading: true, pageNumber: 1, clearFailure: true);
    await _load(refresh: true);
  }

  Future<void> loadMore() async {
    if (!state.hasMore || state.isLoadingMore) return;
    final nextPage = state.pageNumber + 1;
    state = state.copyWith(isLoadingMore: true);
    final useCase = ref.read(getAdjustmentHistoryProvider);
    try {
      final result = await useCase(pageNumber: nextPage, pageSize: _pageSize);
      if (!ref.mounted) return;
      state = state.copyWith(
        adjustments: [...state.adjustments, ...result.items],
        isLoadingMore: false,
        pageNumber: nextPage,
        hasMore: result.hasMore,
      );
    } on AppException catch (error) {
      if (!ref.mounted) return;
      state = state.copyWith(isLoadingMore: false, failure: error);
    } on Object {
      if (!ref.mounted) return;
      state = state.copyWith(isLoadingMore: false);
    }
  }
}
