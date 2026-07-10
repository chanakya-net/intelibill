import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/network/api_client_provider.dart';
import 'package:intelibill_mobile/src/features/expenses/data/data_sources/expense_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/expenses/data/repositories/expense_repository_impl.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expense_detail.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expense_list_item.dart';
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
    this.expenses = const [],
    this.totalCount = 0,
    this.statusFilter = ExpenseStatusFilter.all,
    this.isLoading = false,
    this.isRefreshing = false,
    this.listFailure,
    this.selectedExpense,
    this.selectedExpenseId,
    this.isDetailLoading = false,
    this.detailFailure,
  });

  final List<ExpenseListItem> expenses;
  final int totalCount;
  final ExpenseStatusFilter statusFilter;
  final bool isLoading;
  final bool isRefreshing;
  final Failure? listFailure;
  final ExpenseDetail? selectedExpense;
  final String? selectedExpenseId;
  final bool isDetailLoading;
  final Failure? detailFailure;

  List<ExpenseListItem> get filteredExpenses => switch (statusFilter) {
    ExpenseStatusFilter.all => expenses,
    ExpenseStatusFilter.active =>
      expenses.where((expense) => !expense.isVoided).toList(growable: false),
    ExpenseStatusFilter.voided =>
      expenses.where((expense) => expense.isVoided).toList(growable: false),
  };

  ExpensesState copyWith({
    List<ExpenseListItem>? expenses,
    int? totalCount,
    ExpenseStatusFilter? statusFilter,
    bool? isLoading,
    bool? isRefreshing,
    Failure? listFailure,
    bool clearListFailure = false,
    ExpenseDetail? selectedExpense,
    String? selectedExpenseId,
    bool? isDetailLoading,
    Failure? detailFailure,
    bool clearSelectedExpense = false,
    bool clearDetailFailure = false,
  }) {
    return ExpensesState(
      expenses: expenses ?? this.expenses,
      totalCount: totalCount ?? this.totalCount,
      statusFilter: statusFilter ?? this.statusFilter,
      isLoading: isLoading ?? this.isLoading,
      isRefreshing: isRefreshing ?? this.isRefreshing,
      listFailure: clearListFailure ? null : (listFailure ?? this.listFailure),
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
    unawaited(Future.microtask(_loadExpenses));
    return const ExpensesState(isLoading: true);
  }

  void updateStatusFilter(ExpenseStatusFilter filter) {
    state = state.copyWith(statusFilter: filter);
  }

  Future<void> _loadExpenses() async {
    try {
      final page = await ref.read(getExpensesUseCaseProvider)();
      if (!ref.mounted) return;
      state = state.copyWith(
        expenses: page.items,
        totalCount: page.totalCount,
        isLoading: false,
        isRefreshing: false,
        clearListFailure: true,
      );
    } on AppException catch (error) {
      if (!ref.mounted) return;
      state = state.copyWith(
        isLoading: false,
        isRefreshing: false,
        listFailure: error.failure,
      );
    } on Object {
      if (!ref.mounted) return;
      state = state.copyWith(
        isLoading: false,
        isRefreshing: false,
        listFailure: const Failure.unknown(),
      );
    }
  }

  Future<void> refresh() async {
    final hasVisibleRows = state.expenses.isNotEmpty;
    state = state.copyWith(
      isLoading: !hasVisibleRows,
      isRefreshing: hasVisibleRows,
      clearListFailure: true,
    );
    await _loadExpenses();
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
    state = state.copyWith(
      isDetailLoading: true,
      clearDetailFailure: true,
    );
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
    state = state.copyWith(
      isDetailLoading: false,
      clearSelectedExpense: true,
    );
  }
}
