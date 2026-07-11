import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/network/api_client_provider.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:intelibill_mobile/src/features/expenses/data/data_sources/expense_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/expenses/data/repositories/expense_repository_impl.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expense_category.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expense_detail.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expense_list_item.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expenses_page.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/repositories/expense_repository.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/use_cases/correct_expense.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/use_cases/get_expense_categories.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/use_cases/get_expense_detail.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/use_cases/get_expenses.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/use_cases/record_expense.dart';
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

@riverpod
GetExpenseCategories getExpenseCategoriesUseCase(Ref ref) {
  return GetExpenseCategories(ref.watch(expenseRepositoryProvider));
}

@riverpod
RecordExpense recordExpenseUseCase(Ref ref) {
  return RecordExpense(ref.watch(expenseRepositoryProvider));
}

@riverpod
CorrectExpense correctExpenseUseCase(Ref ref) {
  return CorrectExpense(ref.watch(expenseRepositoryProvider));
}

@immutable
class ExpensesState {
  const ExpensesState({
    this.page,
    this.searchQuery = '',
    this.pageNumber = 1,
    this.pageSize = 20,
    this.totalCount = 0,
    this.statusFilter = ExpenseStatusFilter.all,
    this.isLoading = false,
    this.isLoadingMore = false,
    this.failure,
    this.loadMoreFailure,
    this.selectedExpense,
    this.selectedExpenseId,
    this.isDetailLoading = false,
    this.detailFailure,
    this.categories = const [],
    this.isLoadingCategories = false,
    this.categoryFailure,
    this.isSubmitting = false,
    this.submitFailure,
  });

  final ExpensePage? page;
  final String searchQuery;
  final int pageNumber;
  final int pageSize;
  final int totalCount;
  final ExpenseStatusFilter statusFilter;
  final bool isLoading;
  final bool isLoadingMore;
  final Failure? failure;
  final Failure? loadMoreFailure;
  final ExpenseDetail? selectedExpense;
  final String? selectedExpenseId;
  final bool isDetailLoading;
  final Failure? detailFailure;
  final List<ExpenseCategory> categories;
  final bool isLoadingCategories;
  final Failure? categoryFailure;
  final bool isSubmitting;
  final Failure? submitFailure;

  bool get hasMore => pageNumber * pageSize < totalCount;

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

  List<ExpenseListItem> get _loadedExpenses =>
      page?.items ?? const <ExpenseListItem>[];

  double get loadedAmount => _loadedExpenses
      .where((expense) => !expense.isVoided)
      .fold<double>(0, (sum, expense) => sum + expense.amount);

  int get loadedActiveCount =>
      _loadedExpenses.where((expense) => !expense.isVoided).length;

  int get loadedVoidedCount =>
      _loadedExpenses.where((expense) => expense.isVoided).length;

  ExpensesState copyWith({
    ExpensePage? page,
    String? searchQuery,
    int? pageNumber,
    int? pageSize,
    int? totalCount,
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
    List<ExpenseCategory>? categories,
    bool? isLoadingCategories,
    Failure? categoryFailure,
    bool clearCategoryFailure = false,
    bool? isSubmitting,
    Failure? submitFailure,
    bool clearSubmitFailure = false,
  }) {
    return ExpensesState(
      page: page ?? this.page,
      searchQuery: searchQuery ?? this.searchQuery,
      pageNumber: pageNumber ?? this.pageNumber,
      pageSize: pageSize ?? this.pageSize,
      totalCount: totalCount ?? this.totalCount,
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
      categories: categories ?? this.categories,
      isLoadingCategories: isLoadingCategories ?? this.isLoadingCategories,
      categoryFailure: clearCategoryFailure
          ? null
          : (categoryFailure ?? this.categoryFailure),
      isSubmitting: isSubmitting ?? this.isSubmitting,
      submitFailure: clearSubmitFailure
          ? null
          : (submitFailure ?? this.submitFailure),
    );
  }
}

@riverpod
class ExpensesController extends _$ExpensesController {
  Timer? _searchDebounce;
  String? _activeShopId;
  int _detailRequestGeneration = 0;
  int _paginationGeneration = 0;
  int _categoryGeneration = 0;
  int _mutationGeneration = 0;
  Future<void>? _loadMoreOperation;
  int _loadMoreOperationGeneration = -1;

  @override
  ExpensesState build() {
    if (!ref.mounted) {
      return const ExpensesState(isLoading: true);
    }
    final activeShopId = ref
        .watch(authControllerProvider)
        .value
        ?.session
        ?.activeShopId;
    final shopChanged = activeShopId != null && _activeShopId != activeShopId;
    if (shopChanged) {
      _handleActiveShopChange(activeShopId);
    }
    ref.onDispose(() => _searchDebounce?.cancel());
    unawaited(Future.microtask(_startInitialLoad));
    if (activeShopId == null || shopChanged) {
      return const ExpensesState(isLoading: true);
    }
    return state;
  }

  void _handleActiveShopChange(String? activeShopId) {
    _activeShopId = activeShopId;
    _paginationGeneration += 1;
    _detailRequestGeneration += 1;
    _categoryGeneration += 1;
    _mutationGeneration += 1;
    _loadMoreOperationGeneration = -1;
    _loadMoreOperation = null;
    _searchDebounce?.cancel();

    state = const ExpensesState();

    unawaited(refresh());
  }

  void _startInitialLoad() {
    if (!ref.mounted) {
      return;
    }
    if (_paginationGeneration == 0) unawaited(refresh());
  }

  Future<void> refresh() async {
    if (!ref.mounted) return;
    final requestGeneration = ++_paginationGeneration;
    final requestShopId = _activeShopId;
    state = state.copyWith(
      isLoading: true,
      isLoadingMore: false,
      clearFailure: true,
      clearLoadMoreFailure: true,
    );
    try {
      final search = state.searchQuery.trim();
      final page = await _fetchExpenses(
        page: search.isEmpty ? null : 1,
        pageSize: search.isEmpty ? null : state.pageSize,
      );
      if (!_isCurrentPaginationRequest(requestGeneration, requestShopId)) {
        return;
      }
      state = state.copyWith(
        page: page,
        pageNumber: page.pageNumber,
        pageSize: page.pageSize,
        totalCount: page.totalCount,
        isLoading: false,
        clearFailure: true,
      );
    } on AppException catch (error) {
      if (!_isCurrentPaginationRequest(requestGeneration, requestShopId)) {
        return;
      }
      state = state.copyWith(isLoading: false, failure: error.failure);
    } on Object {
      if (!_isCurrentPaginationRequest(requestGeneration, requestShopId)) {
        return;
      }
      state = state.copyWith(
        isLoading: false,
        failure: const Failure.unknown(),
      );
    }
  }

  Future<void> loadMore() {
    if (!ref.mounted) return Future.value();
    final pendingOperation = _loadMoreOperation;
    if (pendingOperation != null &&
        _loadMoreOperationGeneration == _paginationGeneration) {
      return pendingOperation;
    }

    final requestGeneration = _paginationGeneration;
    final operation = _loadMoreInternal();
    _loadMoreOperation = operation;
    _loadMoreOperationGeneration = requestGeneration;
    unawaited(
      operation.whenComplete(() {
        if (identical(_loadMoreOperation, operation)) {
          _loadMoreOperation = null;
        }
      }),
    );
    return operation;
  }

  Future<void> _loadMoreInternal() async {
    if (!ref.mounted) {
      return Future.value();
    }
    final page = state.page;
    if (state.isLoading ||
        page == null ||
        page.items.isEmpty ||
        !state.hasMore) {
      return;
    }
    final requestGeneration = _paginationGeneration;
    final requestShopId = _activeShopId;
    final nextPage = state.pageNumber + 1;
    state = state.copyWith(isLoadingMore: true, clearLoadMoreFailure: true);
    try {
      final result = await _fetchExpenses(
        page: nextPage,
        pageSize: state.pageSize,
      );
      if (!_isCurrentPaginationRequest(requestGeneration, requestShopId)) {
        return;
      }
      final currentPage = state.page;
      if (currentPage == null) {
        return;
      }
      state = state.copyWith(
        page: result.copyWith(
          items: _appendUnique(currentPage.items, result.items),
        ),
        pageNumber: result.pageNumber,
        pageSize: result.pageSize,
        totalCount: result.totalCount,
        isLoadingMore: false,
        clearLoadMoreFailure: true,
      );
    } on AppException catch (error) {
      if (!_isCurrentPaginationRequest(requestGeneration, requestShopId)) {
        return;
      }
      state = state.copyWith(
        isLoadingMore: false,
        loadMoreFailure: error.failure,
      );
    } on Object {
      if (!_isCurrentPaginationRequest(requestGeneration, requestShopId)) {
        return;
      }
      state = state.copyWith(
        isLoadingMore: false,
        loadMoreFailure: const Failure.unknown(),
      );
    }
  }

  bool _isCurrentPaginationRequest(
    int requestGeneration,
    String? requestShopId,
  ) {
    return ref.mounted &&
        _paginationGeneration == requestGeneration &&
        _activeShopId == requestShopId;
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

  void updateSearch(String query) {
    if (!ref.mounted) return;
    state = state.copyWith(searchQuery: query);
    _searchDebounce?.cancel();
    ++_paginationGeneration;
    final requestGeneration = _paginationGeneration;
    final requestShopId = _activeShopId;
    _searchDebounce = Timer(const Duration(milliseconds: 300), () {
      if (!_isCurrentPaginationRequest(requestGeneration, requestShopId) ||
          !ref.mounted) {
        return;
      }
      state = state.copyWith(pageNumber: 1);
      unawaited(refresh());
    });
  }

  Future<ExpensePage> _fetchExpenses({int? page, int? pageSize}) {
    final search = state.searchQuery.trim();
    final getExpenses = ref.read(getExpensesUseCaseProvider);
    if (search.isEmpty && page == null && pageSize == null) {
      return getExpenses();
    }
    if (search.isEmpty) {
      return getExpenses(page: page, pageSize: pageSize);
    }
    return getExpenses(page: page, pageSize: pageSize, search: search);
  }

  Future<void> loadCategories({bool force = false}) async {
    if (!ref.mounted) return;
    if (state.isLoadingCategories || (!force && state.categories.isNotEmpty)) {
      return;
    }
    final requestGeneration = ++_categoryGeneration;
    final requestShopId = _activeShopId;
    state = state.copyWith(
      isLoadingCategories: true,
      clearCategoryFailure: true,
    );
    try {
      final categories = await ref.read(getExpenseCategoriesUseCaseProvider)();
      if (!_isCurrentCategoryRequest(requestGeneration, requestShopId)) {
        return;
      }
      state = state.copyWith(
        categories: categories,
        isLoadingCategories: false,
        clearCategoryFailure: true,
      );
    } on AppException catch (error) {
      if (!_isCurrentCategoryRequest(requestGeneration, requestShopId)) {
        return;
      }
      state = state.copyWith(
        isLoadingCategories: false,
        categoryFailure: error.failure,
      );
    } on Object {
      if (!_isCurrentCategoryRequest(requestGeneration, requestShopId)) {
        return;
      }
      state = state.copyWith(
        isLoadingCategories: false,
        categoryFailure: const Failure.unknown(),
      );
    }
  }

  bool _isCurrentCategoryRequest(int requestGeneration, String? requestShopId) {
    return ref.mounted &&
        _categoryGeneration == requestGeneration &&
        _activeShopId == requestShopId;
  }

  Future<bool> recordExpense({
    required String categoryName,
    required double amount,
    required String paidTo,
    String? description,
    required DateTime expenseDate,
  }) async {
    if (!ref.mounted || state.isSubmitting) return false;
    final requestGeneration = ++_mutationGeneration;
    final requestShopId = _activeShopId;
    state = state.copyWith(isSubmitting: true, clearSubmitFailure: true);
    try {
      await ref.read(recordExpenseUseCaseProvider)(
        categoryName: categoryName,
        amount: amount,
        paidTo: paidTo,
        description: description,
        expenseDate: expenseDate,
      );
      // The mutation is committed once the record use case completes. A list
      // refresh failure must not turn that committed mutation into a retryable
      // form submission.
      if (!_isCurrentMutationRequest(requestGeneration, requestShopId)) {
        return true;
      }
      await refresh();
      if (!_isCurrentMutationRequest(requestGeneration, requestShopId)) {
        return true;
      }
      state = state.copyWith(isSubmitting: false, clearSubmitFailure: true);
      return true;
    } on AppException catch (error) {
      if (!_isCurrentMutationRequest(requestGeneration, requestShopId)) {
        return false;
      }
      state = state.copyWith(isSubmitting: false, submitFailure: error.failure);
      return false;
    } on Object {
      if (!_isCurrentMutationRequest(requestGeneration, requestShopId)) {
        return false;
      }
      state = state.copyWith(
        isSubmitting: false,
        submitFailure: const Failure.unknown(),
      );
      return false;
    }
  }

  Future<bool> correctExpense(
    String id, {
    required String categoryName,
    required double amount,
    required String paidTo,
    String? description,
    required DateTime expenseDate,
  }) async {
    if (!ref.mounted || state.isSubmitting) return false;
    final requestGeneration = ++_mutationGeneration;
    final requestShopId = _activeShopId;
    state = state.copyWith(isSubmitting: true, clearSubmitFailure: true);
    try {
      final replacement = await ref.read(correctExpenseUseCaseProvider)(
        id,
        categoryName: categoryName,
        amount: amount,
        paidTo: paidTo,
        description: description,
        expenseDate: expenseDate,
      );
      if (!_isCurrentMutationRequest(requestGeneration, requestShopId)) {
        return true;
      }
      await refresh();
      if (!_isCurrentMutationRequest(requestGeneration, requestShopId)) {
        return true;
      }
      state = state.copyWith(
        selectedExpenseId: replacement.id,
        selectedExpense: replacement,
        isSubmitting: false,
        clearSubmitFailure: true,
      );
      return true;
    } on AppException catch (error) {
      if (!_isCurrentMutationRequest(requestGeneration, requestShopId)) {
        return false;
      }
      state = state.copyWith(isSubmitting: false, submitFailure: error.failure);
      return false;
    } on Object {
      if (!_isCurrentMutationRequest(requestGeneration, requestShopId)) {
        return false;
      }
      state = state.copyWith(
        isSubmitting: false,
        submitFailure: const Failure.unknown(),
      );
      return false;
    }
  }

  Future<void> openExpense(String id) async {
    if (!ref.mounted) return;
    final requestGeneration = ++_detailRequestGeneration;
    final requestShopId = _activeShopId;
    state = state.copyWith(
      selectedExpenseId: id,
      isDetailLoading: true,
      clearSelectedExpense: true,
      clearDetailFailure: true,
    );
    await _loadExpenseDetail(id, requestGeneration, requestShopId);
  }

  Future<void> retryExpenseDetail() async {
    if (!ref.mounted) return;
    final id = state.selectedExpenseId;
    if (id == null) return;
    final requestGeneration = ++_detailRequestGeneration;
    final requestShopId = _activeShopId;
    state = state.copyWith(isDetailLoading: true, clearDetailFailure: true);
    await _loadExpenseDetail(id, requestGeneration, requestShopId);
  }

  Future<void> _loadExpenseDetail(
    String id,
    int requestGeneration,
    String? requestShopId,
  ) async {
    if (!ref.mounted) return;
    try {
      final detail = await ref.read(getExpenseDetailUseCaseProvider)(id);
      if (!_isCurrentDetailRequest(id, requestGeneration, requestShopId)) {
        return;
      }
      state = state.copyWith(
        selectedExpense: detail,
        isDetailLoading: false,
        clearDetailFailure: true,
      );
    } on AppException catch (error) {
      if (!_isCurrentDetailRequest(id, requestGeneration, requestShopId)) {
        return;
      }
      state = state.copyWith(
        isDetailLoading: false,
        detailFailure: error.failure,
      );
    } on Object {
      if (!_isCurrentDetailRequest(id, requestGeneration, requestShopId)) {
        return;
      }
      state = state.copyWith(
        isDetailLoading: false,
        detailFailure: const Failure.unknown(),
      );
    }
  }

  bool _isCurrentDetailRequest(
    String id,
    int requestGeneration,
    String? requestShopId,
  ) {
    return ref.mounted &&
        _detailRequestGeneration == requestGeneration &&
        _activeShopId == requestShopId &&
        state.selectedExpenseId == id;
  }

  bool _isCurrentMutationRequest(int requestGeneration, String? requestShopId) {
    return ref.mounted &&
        _mutationGeneration == requestGeneration &&
        _activeShopId == requestShopId;
  }

  void clearSelectedExpense() {
    _detailRequestGeneration += 1;
    state = state.copyWith(isDetailLoading: false, clearSelectedExpense: true);
  }
}
