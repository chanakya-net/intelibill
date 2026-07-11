import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/network/api_client_provider.dart';
import 'package:intelibill_mobile/src/features/expenses/data/data_sources/expense_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/expenses/data/repositories/expense_repository_impl.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expense_detail.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expense_list_item.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expenses_page.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/repositories/expense_repository.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/use_cases/get_expense_detail.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/use_cases/get_expenses.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'expenses_controller.g.dart';

enum ExpenseStatusFilter { all, active, voided }

@riverpod
ExpenseRemoteDataSource expenseRemoteDataSource(Ref ref) {
  return ExpenseRemoteDataSourceImpl(ref.watch(apiClientProvider));
}

@riverpod
ExpenseRepository expenseRepository(Ref ref) {
  return ExpenseRepositoryImpl(ref.watch(expenseRemoteDataSourceProvider));
}

@riverpod
GetExpenses getExpensesUseCase(Ref ref) {
  return GetExpenses(ref.watch(expenseRepositoryProvider));
}

@riverpod
GetExpenseDetail getExpenseDetailUseCase(Ref ref) {
  return GetExpenseDetail(ref.watch(expenseRepositoryProvider));
}

@immutable
class ExpensesState {
  const ExpensesState({
    this.page,
    this.statusFilter = ExpenseStatusFilter.all,
    this.isLoading = false,
    this.isLoadingMore = false,
    this.failure,
    this.loadMoreFailure,
    this.selectedExpense,
    this.selectedExpenseId,
    this.isDetailLoading = false,
    this.detailFailure,
  });

  final ExpensePage? page;
  final ExpenseStatusFilter statusFilter;
  final bool isLoading;
  final bool isLoadingMore;
  final Failure? failure;
  final Failure? loadMoreFailure;
  final ExpenseDetail? selectedExpense;
  final String? selectedExpenseId;
  final bool isDetailLoading;
  final Failure? detailFailure;

  List<ExpenseListItem> get filteredExpenses {
    final expenses = page?.items ?? const <ExpenseListItem>[];
    return switch (statusFilter) {
      ExpenseStatusFilter.all => expenses,
      ExpenseStatusFilter.active =>
        expenses.where((expense) => !expense.isVoided).toList(),
      ExpenseStatusFilter.voided =>
        expenses.where((expense) => expense.isVoided).toList(),
    };
  }

  ExpensesState copyWith({
    ExpensePage? page,
    ExpenseStatusFilter? statusFilter,
    bool? isLoading,
    bool? isLoadingMore,
    Failure? failure,
    bool clearFailure = false,
    Failure? loadMoreFailure,
    bool clearLoadMoreFailure = false,
    ExpenseDetail? selectedExpense,
    String? selectedExpenseId,
    bool? isDetailLoading,
    Failure? detailFailure,
    bool clearSelectedExpense = false,
    bool clearDetailFailure = false,
  }) {
    return ExpensesState(
      page: page ?? this.page,
      statusFilter: statusFilter ?? this.statusFilter,
      isLoading: isLoading ?? this.isLoading,
      isLoadingMore: isLoadingMore ?? this.isLoadingMore,
      failure: clearFailure ? null : (failure ?? this.failure),
      loadMoreFailure: clearLoadMoreFailure
          ? null
          : (loadMoreFailure ?? this.loadMoreFailure),
      selectedExpense: clearSelectedExpense
          ? null
          : (selectedExpense ?? this.selectedExpense),
      selectedExpenseId: clearSelectedExpense
          ? selectedExpenseId
          : (selectedExpenseId ?? this.selectedExpenseId),
      isDetailLoading: isDetailLoading ?? this.isDetailLoading,
      detailFailure: clearSelectedExpense || clearDetailFailure
          ? null
          : (detailFailure ?? this.detailFailure),
    );
  }
}

@riverpod
class ExpensesController extends _$ExpensesController {
  int _detailRequestGeneration = 0;

  @override
  ExpensesState build() {
    unawaited(Future.microtask(_load));
    return const ExpensesState(isLoading: true);
  }

  Future<void> _load() async {
    try {
      final page = await ref.read(getExpensesUseCaseProvider)();
      if (!ref.mounted) return;
      state = state.copyWith(page: page, isLoading: false, clearFailure: true);
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
    state = state.copyWith(isLoading: true, clearFailure: true);
    try {
      final page = await ref.read(getExpensesUseCaseProvider)();
      if (!ref.mounted) return;
      state = state.copyWith(page: page, isLoading: false, clearFailure: true);
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

  Future<void> loadMore() async {
    final page = state.page;
    if (state.isLoading ||
        state.isLoadingMore ||
        page == null ||
        page.items.isEmpty) {
      return;
    }
    final nextPage = page.pageNumber + 1;
    final totalPages = (page.totalCount + page.pageSize - 1) ~/ page.pageSize;
    if (nextPage > totalPages) {
      return;
    }
    state = state.copyWith(isLoadingMore: true, clearLoadMoreFailure: true);
    try {
      final result = await ref.read(getExpensesUseCaseProvider)(
        page: nextPage,
        pageSize: page.pageSize,
      );
      if (!ref.mounted) return;
      state = state.copyWith(
        page: result.copyWith(items: _appendUnique(page.items, result.items)),
        isLoadingMore: false,
        clearLoadMoreFailure: true,
      );
    } on AppException catch (error) {
      if (!ref.mounted) return;
      state = state.copyWith(
        isLoadingMore: false,
        loadMoreFailure: error.failure,
      );
    } on Object {
      if (!ref.mounted) return;
      state = state.copyWith(
        isLoadingMore: false,
        loadMoreFailure: const Failure.unknown(),
      );
    }
  }

  List<ExpenseListItem> _appendUnique(
    List<ExpenseListItem> existing,
    List<ExpenseListItem> incoming,
  ) {
    final ids = existing.map((expense) => expense.id).toSet();
    return [...existing, ...incoming.where((expense) => ids.add(expense.id))];
  }

  void updateStatusFilter(ExpenseStatusFilter filter) {
    if (filter == state.statusFilter) return;
    state = state.copyWith(statusFilter: filter);
  }

  Future<void> openExpense(String id) async {
    final requestGeneration = ++_detailRequestGeneration;
    state = state.copyWith(
      selectedExpenseId: id,
      isDetailLoading: true,
      clearSelectedExpense: true,
      clearDetailFailure: true,
    );
    await _loadExpenseDetail(id, requestGeneration);
  }

  Future<void> retryExpenseDetail() async {
    final id = state.selectedExpenseId;
    if (id == null) return;
    final requestGeneration = ++_detailRequestGeneration;
    state = state.copyWith(isDetailLoading: true, clearDetailFailure: true);
    await _loadExpenseDetail(id, requestGeneration);
  }

  Future<void> _loadExpenseDetail(String id, int requestGeneration) async {
    try {
      final detail = await ref.read(getExpenseDetailUseCaseProvider)(id);
      if (!_isCurrentDetailRequest(id, requestGeneration)) return;
      state = state.copyWith(
        selectedExpense: detail,
        isDetailLoading: false,
        clearDetailFailure: true,
      );
    } on AppException catch (error) {
      if (!_isCurrentDetailRequest(id, requestGeneration)) return;
      state = state.copyWith(
        isDetailLoading: false,
        detailFailure: error.failure,
      );
    } on Object {
      if (!_isCurrentDetailRequest(id, requestGeneration)) return;
      state = state.copyWith(
        isDetailLoading: false,
        detailFailure: const Failure.unknown(),
      );
    }
  }

  bool _isCurrentDetailRequest(String id, int requestGeneration) {
    return ref.mounted &&
        _detailRequestGeneration == requestGeneration &&
        state.selectedExpenseId == id;
  }

  void clearSelectedExpense() {
    _detailRequestGeneration += 1;
    state = state.copyWith(isDetailLoading: false, clearSelectedExpense: true);
  }
}
