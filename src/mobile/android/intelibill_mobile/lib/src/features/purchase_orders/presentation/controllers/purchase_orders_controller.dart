import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_filters.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_list_item.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_providers.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'purchase_orders_controller.g.dart';

@immutable
class PurchaseOrdersState {
  const PurchaseOrdersState({
    this.items = const [],
    this.totalCount = 0,
    this.isLoading = false,
    this.failure,
  });

  final List<PurchaseOrderListItem> items;
  final int totalCount;
  final bool isLoading;
  final Failure? failure;

  bool get isInitialLoading => isLoading && items.isEmpty;
  bool get isEmpty => !isLoading && failure == null && items.isEmpty;

  PurchaseOrdersState copyWith({
    List<PurchaseOrderListItem>? items,
    int? totalCount,
    bool? isLoading,
    Failure? failure,
    bool clearFailure = false,
  }) {
    return PurchaseOrdersState(
      items: items ?? this.items,
      totalCount: totalCount ?? this.totalCount,
      isLoading: isLoading ?? this.isLoading,
      failure: clearFailure ? null : (failure ?? this.failure),
    );
  }
}

@riverpod
class PurchaseOrdersController extends _$PurchaseOrdersController {
  late Timer _searchDebounce;
  int _searchGeneration = 0;
  String? _activeSearch;

  @override
  PurchaseOrdersState build() {
    _searchDebounce = Timer(Duration.zero, () {});
    final initialGeneration = _nextGeneration();
    unawaited(Future.microtask(() => _loadFirstPage(initialGeneration)));
    ref.onDispose(() {
      _searchDebounce.cancel();
    });
    return const PurchaseOrdersState(isLoading: true);
  }

  Future<void> refresh() {
    _searchDebounce.cancel();
    return _loadFirstPage(_nextGeneration());
  }

  Future<void> retry() {
    _searchDebounce.cancel();
    return _loadFirstPage(_nextGeneration());
  }

  void updateSearch(String query) {
    _activeSearch = _normalizedSearch(query);
    final generation = _nextGeneration();
    _searchDebounce.cancel();
    _searchDebounce = Timer(const Duration(milliseconds: 300), () {
      if (_searchGeneration != generation) return;
      unawaited(_loadFirstPage(generation));
    });
  }

  Future<void> _loadFirstPage(int generation) async {
    final filters = PurchaseOrderFilters(search: _activeSearch);
    state = state.copyWith(isLoading: true, clearFailure: true);
    try {
      final page = await ref.read(getPurchaseOrdersProvider)(filters);
      if (!ref.mounted || _searchGeneration != generation) return;
      state = state.copyWith(
        items: page.items,
        totalCount: page.totalCount,
        isLoading: false,
        clearFailure: true,
      );
    } on AppException catch (error) {
      if (!ref.mounted || _searchGeneration != generation) return;
      state = state.copyWith(isLoading: false, failure: error.failure);
    } on Object {
      if (!ref.mounted || _searchGeneration != generation) return;
      state = state.copyWith(
        isLoading: false,
        failure: const Failure.unknown(),
      );
    }
  }

  int _nextGeneration() => ++_searchGeneration;

  String? _normalizedSearch(String query) {
    final normalized = query.trim();
    return normalized.isEmpty ? null : normalized;
  }
}
