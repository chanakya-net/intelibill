import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_filters.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_list_item.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_status.dart';
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
    this.isLoadingMore = false,
    this.hasMore = false,
    this.loadMoreFailure,
    this.pageNumber = 1,
    this.isRefreshing = false,
    this.refreshFailure,
  });

  final List<PurchaseOrderListItem> items;
  final int totalCount;
  final bool isLoading;
  final Failure? failure;
  final bool isLoadingMore;
  final bool hasMore;
  final Failure? loadMoreFailure;
  final int pageNumber;
  final bool isRefreshing;
  final Failure? refreshFailure;

  bool get isInitialLoading => isLoading && items.isEmpty;
  bool get isEmpty => !isLoading && failure == null && items.isEmpty;

  PurchaseOrdersState copyWith({
    List<PurchaseOrderListItem>? items,
    int? totalCount,
    bool? isLoading,
    Failure? failure,
    bool clearFailure = false,
    bool? isLoadingMore,
    bool? hasMore,
    Failure? loadMoreFailure,
    bool clearLoadMoreFailure = false,
    int? pageNumber,
    bool? isRefreshing,
    Failure? refreshFailure,
    bool clearRefreshFailure = false,
  }) {
    return PurchaseOrdersState(
      items: items ?? this.items,
      totalCount: totalCount ?? this.totalCount,
      isLoading: isLoading ?? this.isLoading,
      failure: clearFailure ? null : (failure ?? this.failure),
      isLoadingMore: isLoadingMore ?? this.isLoadingMore,
      hasMore: hasMore ?? this.hasMore,
      loadMoreFailure: clearLoadMoreFailure
          ? null
          : (loadMoreFailure ?? this.loadMoreFailure),
      pageNumber: pageNumber ?? this.pageNumber,
      isRefreshing: isRefreshing ?? this.isRefreshing,
      refreshFailure: clearRefreshFailure
          ? null
          : (refreshFailure ?? this.refreshFailure),
    );
  }
}

@riverpod
class PurchaseOrdersController extends _$PurchaseOrdersController {
  late Timer _searchDebounce;
  int _requestGeneration = 0;
  String? _activeSearch;
  PurchaseOrderStatus? _activeStatus;
  DateTime? _activeDateFrom;
  DateTime? _activeDateTo;
  int _nextPageToLoad = 2;

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
    _nextPageToLoad = 2;
    final generation = _nextGeneration();
    return _loadFirstPageForRefresh(generation, resetInitialLoad: true);
  }

  Future<void> retry() {
    _searchDebounce.cancel();
    _nextPageToLoad = 2;
    return _loadFirstPage(_nextGeneration());
  }

  void updateSearch(String query) {
    _activeSearch = _normalizedSearch(query);
    final generation = _nextGeneration();
    _searchDebounce.cancel();
    _searchDebounce = Timer(const Duration(milliseconds: 300), () {
      if (_requestGeneration != generation) return;
      unawaited(_loadFirstPage(generation));
    });
  }

  void updateStatus(PurchaseOrderStatus? status) {
    _activeStatus = status;
    unawaited(_loadFirstPage(_nextGeneration()));
  }

  void updateOrderDateFrom(DateTime? date) {
    if (!_isValidRange(date, _activeDateTo)) return;
    _activeDateFrom = date;
    unawaited(_loadFirstPage(_nextGeneration()));
  }

  void updateOrderDateTo(DateTime? date) {
    if (!_isValidRange(_activeDateFrom, date)) return;
    _activeDateTo = date;
    unawaited(_loadFirstPage(_nextGeneration()));
  }

  void clearFilters() {
    _activeSearch = null;
    _activeStatus = null;
    _activeDateFrom = null;
    _activeDateTo = null;
    _searchDebounce.cancel();
    unawaited(_loadFirstPage(_nextGeneration()));
  }

  Future<void> _loadFirstPageForRefresh(int generation, {bool resetInitialLoad = false}) async {
    _nextPageToLoad = 2;
    final filters = PurchaseOrderFilters(
      search: _activeSearch,
      status: _activeStatus,
      orderDateFrom: _activeDateFrom,
      orderDateTo: _activeDateTo,
    );
    state = state.copyWith(
      isRefreshing: true,
      clearRefreshFailure: true,
      isLoading: resetInitialLoad ? true : state.isLoading,
      clearFailure: resetInitialLoad,
      isLoadingMore: resetInitialLoad ? false : state.isLoadingMore,
      clearLoadMoreFailure: resetInitialLoad,
    );
    try {
      final page = await ref.read(getPurchaseOrdersProvider)(filters);
      if (!ref.mounted || _requestGeneration != generation) return;
      state = state.copyWith(
        items: page.items,
        totalCount: page.totalCount,
        isRefreshing: false,
        isLoading: false,
        clearRefreshFailure: true,
        clearFailure: true,
        pageNumber: 1,
        hasMore: page.items.length < page.totalCount,
      );
    } on AppException catch (error) {
      if (!ref.mounted || _requestGeneration != generation) return;
      state = state.copyWith(
        isRefreshing: false,
        isLoading: false,
        refreshFailure: error.failure,
        failure: resetInitialLoad ? error.failure : null,
      );
    } on Object {
      if (!ref.mounted || _requestGeneration != generation) return;
      state = state.copyWith(
        isRefreshing: false,
        isLoading: false,
        refreshFailure: const Failure.unknown(),
        failure: resetInitialLoad ? const Failure.unknown() : null,
      );
    }
  }

  Future<void> _loadFirstPage(int generation) async {
    _nextPageToLoad = 2;
    final filters = PurchaseOrderFilters(
      search: _activeSearch,
      status: _activeStatus,
      orderDateFrom: _activeDateFrom,
      orderDateTo: _activeDateTo,
    );
    state = state.copyWith(
      isLoading: true,
      isLoadingMore: false,
      hasMore: false,
      clearFailure: true,
      clearLoadMoreFailure: true,
    );
    try {
      final page = await ref.read(getPurchaseOrdersProvider)(filters);
      if (!ref.mounted || _requestGeneration != generation) return;
      state = state.copyWith(
        items: page.items,
        totalCount: page.totalCount,
        isLoading: false,
        clearFailure: true,
        hasMore: page.items.length < page.totalCount,
        pageNumber: 1,
      );
    } on AppException catch (error) {
      if (!ref.mounted || _requestGeneration != generation) return;
      state = state.copyWith(isLoading: false, failure: error.failure);
    } on Object {
      if (!ref.mounted || _requestGeneration != generation) return;
      state = state.copyWith(
        isLoading: false,
        failure: const Failure.unknown(),
      );
    }
  }

  Future<void> loadMore() async {
    if (!state.hasMore ||
        state.isLoadingMore ||
        state.isLoading ||
        state.isRefreshing ||
        state.loadMoreFailure != null) {
      return;
    }
    state = state.copyWith(isLoadingMore: true);
    await _loadNextPage(
      generation: _requestGeneration,
      pageNumber: _nextPageToLoad,
    );
  }

  Future<void> retryLoadMore() async {
    if (!state.hasMore ||
        state.isLoadingMore ||
        state.isLoading ||
        state.loadMoreFailure == null) {
      return;
    }
    state = state.copyWith(isLoadingMore: true, clearLoadMoreFailure: true);
    await _loadNextPage(
      generation: _requestGeneration,
      pageNumber: _nextPageToLoad,
    );
  }

  Future<void> _loadNextPage({
    required int generation,
    required int pageNumber,
  }) async {
    final filters = PurchaseOrderFilters(
      search: _activeSearch,
      status: _activeStatus,
      orderDateFrom: _activeDateFrom,
      orderDateTo: _activeDateTo,
      page: pageNumber,
      pageSize: 20,
    );
    try {
      final page = await ref.read(getPurchaseOrdersProvider)(filters);
      if (!ref.mounted || _requestGeneration != generation) return;
      state = state.copyWith(
        items: [...state.items, ...page.items],
        totalCount: page.totalCount,
        isLoadingMore: false,
        clearLoadMoreFailure: true,
        hasMore: state.items.length + page.items.length < page.totalCount,
        pageNumber: pageNumber,
      );
      _nextPageToLoad += 1;
    } on AppException catch (error) {
      if (!ref.mounted || _requestGeneration != generation) return;
      state = state.copyWith(
        isLoadingMore: false,
        loadMoreFailure: error.failure,
      );
    } on Object {
      if (!ref.mounted || _requestGeneration != generation) return;
      state = state.copyWith(
        isLoadingMore: false,
        loadMoreFailure: const Failure.unknown(),
      );
    }
  }

  int _nextGeneration() => ++_requestGeneration;

  bool _isValidRange(DateTime? from, DateTime? to) {
    return from == null || to == null || !from.isAfter(to);
  }

  String? _normalizedSearch(String query) {
    final normalized = query.trim();
    return normalized.isEmpty ? null : normalized;
  }
}
