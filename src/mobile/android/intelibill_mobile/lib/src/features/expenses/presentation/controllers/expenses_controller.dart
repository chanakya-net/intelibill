import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/network/api_client_provider.dart';
import 'package:intelibill_mobile/src/features/expenses/data/data_sources/expense_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/expenses/data/repositories/expense_repository_impl.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expenses_page.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/repositories/expense_repository.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/use_cases/get_expenses.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'expenses_controller.g.dart';

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

@immutable
class ExpensesState {
  const ExpensesState({this.page, this.isLoading = false, this.failure});

  final ExpensePage? page;
  final bool isLoading;
  final Failure? failure;

  ExpensesState copyWith({
    ExpensePage? page,
    bool? isLoading,
    Failure? failure,
    bool clearFailure = false,
  }) {
    return ExpensesState(
      page: page ?? this.page,
      isLoading: isLoading ?? this.isLoading,
      failure: clearFailure ? null : (failure ?? this.failure),
    );
  }
}

@riverpod
class ExpensesController extends _$ExpensesController {
  @override
  ExpensesState build() {
    unawaited(Future.microtask(_load));
    return const ExpensesState(isLoading: true);
  }

  Future<void> _load() async {
    try {
      final page = await ref.read(getExpensesUseCaseProvider)();
      if (!ref.mounted) return;
      state = state.copyWith(
        page: page,
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

  Future<void> refresh() async {
    state = state.copyWith(isLoading: true, clearFailure: true);
    await _load();
  }
}
