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

  @override
  PurchaseOrdersState build() {
    _searchDebounce = Timer(Duration.zero, () {});
    unawaited(Future.microtask(_loadFirstPage));
    ref.onDispose(() {
      _searchDebounce.cancel();
    });
    return const PurchaseOrdersState(isLoading: true);
  }

  Future<void> refresh() => _loadFirstPage();

  Future<void> retry() => _loadFirstPage();

  void updateSearch(String query) {
    _searchGeneration += 1;
    final gen = _searchGeneration;
    _searchDebounce.cancel();
    _searchDebounce = Timer(const Duration(milliseconds: 300), () {
      if (_searchGeneration != gen) return;
      unawaited(_search(query));
    });
  }

  Future<void> _search(String query) async {
    final gen = _searchGeneration;
    state = state.copyWith(isLoading: true, clearFailure: true);
    try {
      final page = await ref.read(getPurchaseOrdersProvider)(
        PurchaseOrderFilters(search: query.isEmpty ? null : query),
      );
      if (!ref.mounted || _searchGeneration != gen) return;
      state = state.copyWith(
        items: page.items,
        totalCount: page.totalCount,
        isLoading: false,
        clearFailure: true,
      );
    } on AppException catch (error) {
      if (!ref.mounted || _searchGeneration != gen) return;
      state = state.copyWith(isLoading: false, failure: error.failure);
    } on Object {
      if (!ref.mounted || _searchGeneration != gen) return;
      state = state.copyWith(
        isLoading: false,
        failure: const Failure.unknown(),
      );
    }
  }

  Future<void> _loadFirstPage() async {
    state = state.copyWith(isLoading: true, clearFailure: true);
    try {
      final page = await ref.read(getPurchaseOrdersProvider)(
        const PurchaseOrderFilters(),
      );
      if (!ref.mounted) return;
      state = state.copyWith(
        items: page.items,
        totalCount: page.totalCount,
        isLoading: false,
        clearFailure: true,
      );
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
}
