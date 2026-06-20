import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/network/api_client_provider.dart';
import 'package:intelibill_mobile/src/features/sales/data/data_sources/sales_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/sales/data/repositories/sales_repository_impl.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_list_item.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sales_history_query.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sales_history_summary.dart';
import 'package:intelibill_mobile/src/features/sales/domain/repositories/sales_repository.dart';
import 'package:intelibill_mobile/src/features/sales/domain/use_cases/get_sale_detail.dart';
import 'package:intelibill_mobile/src/features/sales/domain/use_cases/get_sales_history.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'sales_history_controller.g.dart';

@riverpod
SalesRemoteDataSource salesRemoteDataSource(Ref ref) {
  final apiClient = ref.watch(apiClientProvider);
  return SalesRemoteDataSourceImpl(apiClient);
}

@riverpod
SalesRepository salesRepository(Ref ref) {
  final remoteDataSource = ref.watch(salesRemoteDataSourceProvider);
  return SalesRepositoryImpl(remoteDataSource);
}

@riverpod
GetSalesHistory getSalesHistory(Ref ref) {
  final repository = ref.watch(salesRepositoryProvider);
  return GetSalesHistory(repository);
}

@riverpod
GetSaleDetail getSaleDetail(Ref ref) {
  final repository = ref.watch(salesRepositoryProvider);
  return GetSaleDetail(repository);
}

SalesHistoryQuery defaultSalesHistoryQuery() {
  final now = DateTime.now();
  final from = DateTime(now.year, now.month, now.day).subtract(
    const Duration(days: 30),
  );
  final to = DateTime(now.year, now.month, now.day, 23, 59, 59);
  return SalesHistoryQuery(from: from, to: to);
}

@immutable
class SalesHistoryState {
  const SalesHistoryState({
    this.sales = const [],
    this.summary = const SalesHistorySummary(
      periodSales: 0,
      invoiceCount: 0,
      refundAmount: 0,
    ),
    required this.query,
    this.searchQuery = '',
    this.statusFilter = 'all',
    this.isLoading = false,
    this.isLoadingMore = false,
    this.failure,
    this.totalCount = 0,
    this.pageNumber = 1,
    this.hasMore = false,
  });

  final List<SaleListItem> sales;
  final SalesHistorySummary summary;
  final SalesHistoryQuery query;
  final String searchQuery;
  final String statusFilter;
  final bool isLoading;
  final bool isLoadingMore;
  final Failure? failure;
  final int totalCount;
  final int pageNumber;
  final bool hasMore;

  SalesHistoryState copyWith({
    List<SaleListItem>? sales,
    SalesHistorySummary? summary,
    SalesHistoryQuery? query,
    String? searchQuery,
    String? statusFilter,
    bool? isLoading,
    bool? isLoadingMore,
    Failure? failure,
    bool clearError = false,
    int? totalCount,
    int? pageNumber,
    bool? hasMore,
  }) {
    return SalesHistoryState(
      sales: sales ?? this.sales,
      summary: summary ?? this.summary,
      query: query ?? this.query,
      searchQuery: searchQuery ?? this.searchQuery,
      statusFilter: statusFilter ?? this.statusFilter,
      isLoading: isLoading ?? this.isLoading,
      isLoadingMore: isLoadingMore ?? this.isLoadingMore,
      failure: clearError ? null : (failure ?? this.failure),
      totalCount: totalCount ?? this.totalCount,
      pageNumber: pageNumber ?? this.pageNumber,
      hasMore: hasMore ?? this.hasMore,
    );
  }
}

@riverpod
class SalesHistoryController extends _$SalesHistoryController {
  static const _pageSize = 20;
  Timer? _searchDebounce;

  @override
  SalesHistoryState build() {
    final initialQuery = defaultSalesHistoryQuery();
    ref.onDispose(() => _searchDebounce?.cancel());
    unawaited(Future.microtask(() => _load(query: initialQuery)));
    return SalesHistoryState(
      isLoading: true,
      query: initialQuery,
    );
  }

  Future<void> _load({
    required SalesHistoryQuery query,
    bool append = false,
  }) async {
    final useCase = ref.read(getSalesHistoryProvider);
    final apiQuery = query.copyWith(
      search: state.searchQuery.trim().isEmpty
          ? null
          : state.searchQuery.trim(),
      status: state.statusFilter == 'all' ? null : state.statusFilter,
      page: query.page,
      pageSize: _pageSize,
    );

    try {
      final result = await useCase(apiQuery);
      if (!ref.mounted) return;

      state = state.copyWith(
        sales: append ? [...state.sales, ...result.items] : result.items,
        summary: result.summary,
        query: query,
        isLoading: false,
        isLoadingMore: false,
        totalCount: result.totalCount,
        pageNumber: result.pageNumber,
        hasMore: result.hasMore,
        clearError: true,
      );
    } on AppException catch (error) {
      if (!ref.mounted) return;
      state = state.copyWith(
        isLoading: false,
        isLoadingMore: false,
        failure: error.failure,
      );
    } on Object {
      if (!ref.mounted) return;
      state = state.copyWith(
        isLoading: false,
        isLoadingMore: false,
        failure: const Failure.unknown(),
      );
    }
  }

  Future<void> refresh() async {
    state = state.copyWith(isLoading: true, clearError: true);
    await _load(query: state.query.copyWith(page: 1));
  }

  Future<void> loadMore() async {
    if (!state.hasMore || state.isLoadingMore || state.isLoading) return;
    state = state.copyWith(isLoadingMore: true);
    await _load(
      query: state.query.copyWith(page: state.pageNumber + 1),
      append: true,
    );
  }

  void updateSearch(String query) {
    state = state.copyWith(searchQuery: query);
    _searchDebounce?.cancel();
    _searchDebounce = Timer(const Duration(milliseconds: 300), () {
      if (!ref.mounted) return;
      unawaited(refresh());
    });
  }

  Future<void> updateStatusFilter(String status) async {
    if (state.statusFilter == status) return;
    state = state.copyWith(
      statusFilter: status,
      isLoading: true,
      clearError: true,
    );
    await _load(query: state.query.copyWith(page: 1));
  }

  Future<void> updateDateRange({
    required DateTime from,
    required DateTime to,
  }) async {
    final normalizedFrom = DateTime(from.year, from.month, from.day);
    final normalizedTo = DateTime(to.year, to.month, to.day, 23, 59, 59);
    state = state.copyWith(
      query: state.query.copyWith(
        from: normalizedFrom,
        to: normalizedTo,
        page: 1,
      ),
      isLoading: true,
      clearError: true,
    );
    await _load(query: state.query);
  }

  Future<void> clearFilters() async {
    _searchDebounce?.cancel();
    final query = defaultSalesHistoryQuery();
    state = state.copyWith(
      searchQuery: '',
      statusFilter: 'all',
      query: query,
      isLoading: true,
      clearError: true,
    );
    await _load(query: query);
  }
}
