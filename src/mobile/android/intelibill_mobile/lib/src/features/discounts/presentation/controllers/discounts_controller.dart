import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/network/api_client_provider.dart';
import 'package:intelibill_mobile/src/features/discounts/data/data_sources/discounts_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/discounts/data/repositories/discounts_repository_impl.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount_rule.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount_rule_query.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/repositories/discounts_repository.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/use_cases/get_discount_rule_detail.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/use_cases/get_discount_rules.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'discounts_controller.g.dart';

@riverpod
DiscountsRemoteDataSource discountsRemoteDataSource(Ref ref) {
  final apiClient = ref.watch(apiClientProvider);
  return DiscountsRemoteDataSourceImpl(apiClient);
}

@riverpod
DiscountsRepository discountsRepository(Ref ref) {
  final remoteDataSource = ref.watch(discountsRemoteDataSourceProvider);
  return DiscountsRepositoryImpl(remoteDataSource);
}

@riverpod
GetDiscountRules getDiscountRules(Ref ref) {
  final repository = ref.watch(discountsRepositoryProvider);
  return GetDiscountRules(repository);
}

@riverpod
GetDiscountRuleDetail getDiscountRuleDetail(Ref ref) {
  final repository = ref.watch(discountsRepositoryProvider);
  return GetDiscountRuleDetail(repository);
}

@immutable
class DiscountsState {
  const DiscountsState({
    this.rules = const [],
    required this.query,
    this.searchQuery = '',
    this.statusFilter = DiscountRuleStatusFilter.all,
    this.ruleTypeFilter = DiscountRuleTypeFilter.all,
    this.sort = DiscountRuleSort.createdDesc,
    this.isLoading = false,
    this.isLoadingMore = false,
    this.isDetailLoading = false,
    this.failure,
    this.detailFailure,
    this.totalCount = 0,
    this.pageNumber = 1,
    this.pageSize = 20,
    this.hasMore = false,
    this.selectedRule,
    this.selectedRuleId,
  });

  final List<DiscountRule> rules;
  final DiscountRulesQuery query;
  final String searchQuery;
  final String statusFilter;
  final String ruleTypeFilter;
  final String sort;
  final bool isLoading;
  final bool isLoadingMore;
  final bool isDetailLoading;
  final Failure? failure;
  final Failure? detailFailure;
  final int totalCount;
  final int pageNumber;
  final int pageSize;
  final bool hasMore;
  final DiscountRule? selectedRule;
  final String? selectedRuleId;

  DiscountsState copyWith({
    List<DiscountRule>? rules,
    DiscountRulesQuery? query,
    String? searchQuery,
    String? statusFilter,
    String? ruleTypeFilter,
    String? sort,
    bool? isLoading,
    bool? isLoadingMore,
    bool? isDetailLoading,
    Failure? failure,
    Failure? detailFailure,
    bool clearFailure = false,
    bool clearDetailFailure = false,
    int? totalCount,
    int? pageNumber,
    int? pageSize,
    bool? hasMore,
    DiscountRule? selectedRule,
    String? selectedRuleId,
    bool clearSelectedRule = false,
  }) {
    return DiscountsState(
      rules: rules ?? this.rules,
      query: query ?? this.query,
      searchQuery: searchQuery ?? this.searchQuery,
      statusFilter: statusFilter ?? this.statusFilter,
      ruleTypeFilter: ruleTypeFilter ?? this.ruleTypeFilter,
      sort: sort ?? this.sort,
      isLoading: isLoading ?? this.isLoading,
      isLoadingMore: isLoadingMore ?? this.isLoadingMore,
      isDetailLoading: isDetailLoading ?? this.isDetailLoading,
      failure: clearFailure ? null : (failure ?? this.failure),
      detailFailure: clearDetailFailure
          ? null
          : (detailFailure ?? this.detailFailure),
      totalCount: totalCount ?? this.totalCount,
      pageNumber: pageNumber ?? this.pageNumber,
      pageSize: pageSize ?? this.pageSize,
      hasMore: hasMore ?? this.hasMore,
      selectedRule: clearSelectedRule
          ? null
          : (selectedRule ?? this.selectedRule),
      selectedRuleId: clearSelectedRule
          ? null
          : (selectedRuleId ?? this.selectedRuleId),
    );
  }
}

@riverpod
class DiscountsController extends _$DiscountsController {
  static const int _pageSize = 20;
  Timer? _searchDebounce;

  @override
  DiscountsState build() {
    const initialQuery = DiscountRulesQuery();
    ref.onDispose(() => _searchDebounce?.cancel());
    unawaited(Future.microtask(() => _load(query: initialQuery)));
    return const DiscountsState(
      isLoading: true,
      query: initialQuery,
    );
  }

  Future<void> _load({
    required DiscountRulesQuery query,
    bool append = false,
  }) async {
    final useCase = ref.read(getDiscountRulesProvider);
    final requestQuery = query.copyWith(
      search: state.searchQuery.trim().isEmpty
          ? null
          : state.searchQuery.trim(),
      pageSize: _pageSize,
      statusFilter: state.statusFilter,
      ruleTypeFilter: state.ruleTypeFilter,
      sort: state.sort,
      page: query.page,
    );

    try {
      final result = await useCase(requestQuery);
      if (!ref.mounted) return;

      state = state.copyWith(
        rules: append ? [...state.rules, ...result.rules] : result.rules,
        query: requestQuery,
        isLoading: false,
        isLoadingMore: false,
        totalCount: result.totalCount,
        pageNumber: result.pageNumber,
        pageSize: result.pageSize,
        hasMore: result.hasMore,
        clearFailure: true,
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
    state = state.copyWith(
      isLoading: true,
      clearFailure: true,
    );
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

  Future<void> updateSearch(String queryText) async {
    state = state.copyWith(searchQuery: queryText);
    _searchDebounce?.cancel();
    _searchDebounce = Timer(const Duration(milliseconds: 300), () {
      if (!ref.mounted) return;
      unawaited(refresh());
    });
  }

  Future<void> clearSearch() async {
    _searchDebounce?.cancel();
    if (state.searchQuery.isEmpty) return;
    state = state.copyWith(searchQuery: '');
    await refresh();
  }

  Future<void> updateStatusFilter(String status) async {
    if (state.statusFilter == status) return;
    state = state.copyWith(
      statusFilter: status,
      isLoading: true,
      clearFailure: true,
    );
    await _load(query: state.query.copyWith(page: 1));
  }

  Future<void> updateRuleTypeFilter(String ruleType) async {
    if (state.ruleTypeFilter == ruleType) return;
    state = state.copyWith(
      ruleTypeFilter: ruleType,
      isLoading: true,
      clearFailure: true,
    );
    await _load(query: state.query.copyWith(page: 1));
  }

  Future<void> updateSort(String sort) async {
    if (state.sort == sort) return;
    state = state.copyWith(
      sort: sort,
      isLoading: true,
      clearFailure: true,
    );
    await _load(query: state.query.copyWith(page: 1));
  }

  void clearSelectedRule() {
    state = state.copyWith(clearSelectedRule: true);
  }

  Future<void> selectRule(String ruleId) async {
    final detailUseCase = ref.read(getDiscountRuleDetailProvider);
    state = state.copyWith(
      selectedRuleId: ruleId,
      isDetailLoading: true,
      clearDetailFailure: true,
    );

    try {
      final detail = await detailUseCase(ruleId: ruleId);
      if (!ref.mounted) return;
      state = state.copyWith(
        selectedRule: detail,
        isDetailLoading: false,
      );
    } on AppException catch (error) {
      if (!ref.mounted) return;
      state = state.copyWith(
        isDetailLoading: false,
        detailFailure: error.failure,
      );
    } on Object {
      if (!ref.mounted) return;
      state = state.copyWith(
        isDetailLoading: false,
        detailFailure: const Failure.unknown(),
      );
    }
  }
}
