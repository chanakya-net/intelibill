import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/network/api_client_provider.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/data/data_sources/bank_accounts_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/data/repositories/bank_accounts_repository_impl.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/domain/entities/bank_account.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/domain/repositories/bank_accounts_repository.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/domain/use_cases/get_bank_accounts.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'bank_accounts_controller.g.dart';

@riverpod
BankAccountsRemoteDataSource bankAccountsRemoteDataSource(Ref ref) {
  return BankAccountsRemoteDataSourceImpl(ref.watch(apiClientProvider));
}

@riverpod
BankAccountsRepository bankAccountsRepository(Ref ref) {
  return BankAccountsRepositoryImpl(
    ref.watch(bankAccountsRemoteDataSourceProvider),
  );
}

@riverpod
GetBankAccounts getBankAccountsUseCase(Ref ref) {
  return GetBankAccounts(ref.watch(bankAccountsRepositoryProvider));
}

@immutable
class BankAccountsState {
  const BankAccountsState({
    this.accounts = const [],
    this.isLoading = false,
    this.failure,
  });

  final List<BankAccount> accounts;
  final bool isLoading;
  final Failure? failure;

  BankAccountsState copyWith({
    List<BankAccount>? accounts,
    bool? isLoading,
    Failure? failure,
    bool clearFailure = false,
  }) {
    return BankAccountsState(
      accounts: accounts ?? this.accounts,
      isLoading: isLoading ?? this.isLoading,
      failure: clearFailure ? null : (failure ?? this.failure),
    );
  }
}

@riverpod
class BankAccountsController extends _$BankAccountsController {
  @override
  BankAccountsState build() {
    unawaited(Future.microtask(_loadBankAccounts));
    return const BankAccountsState(isLoading: true);
  }

  Future<void> _loadBankAccounts() async {
    try {
      final accounts = await ref.read(getBankAccountsUseCaseProvider)();
      if (!ref.mounted) return;
      state = state.copyWith(accounts: accounts, isLoading: false);
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

  Future<void> retry() async {
    state = state.copyWith(isLoading: true, clearFailure: true);
    await _loadBankAccounts();
  }
}
