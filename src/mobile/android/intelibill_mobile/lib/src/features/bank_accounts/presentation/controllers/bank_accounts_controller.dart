import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/network/api_client_provider.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/data/data_sources/bank_accounts_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/data/repositories/bank_accounts_repository_impl.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/domain/entities/bank_account.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/domain/entities/save_bank_account_request.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/domain/repositories/bank_accounts_repository.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/domain/use_cases/add_bank_account.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/domain/use_cases/get_bank_accounts.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/domain/use_cases/update_bank_account.dart';
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

@riverpod
AddBankAccount addBankAccountUseCase(Ref ref) {
  return AddBankAccount(ref.watch(bankAccountsRepositoryProvider));
}

@riverpod
UpdateBankAccount updateBankAccountUseCase(Ref ref) {
  return UpdateBankAccount(ref.watch(bankAccountsRepositoryProvider));
}

@immutable
class BankAccountsState {
  const BankAccountsState({
    this.accounts = const [],
    this.isLoading = false,
    this.isSubmitting = false,
    this.failure,
    this.submitFailure,
  });

  final List<BankAccount> accounts;
  final bool isLoading;
  final bool isSubmitting;
  final Failure? failure;
  final Failure? submitFailure;

  BankAccountsState copyWith({
    List<BankAccount>? accounts,
    bool? isLoading,
    bool? isSubmitting,
    Failure? failure,
    Failure? submitFailure,
    bool clearFailure = false,
    bool clearSubmitFailure = false,
  }) {
    return BankAccountsState(
      accounts: accounts ?? this.accounts,
      isLoading: isLoading ?? this.isLoading,
      isSubmitting: isSubmitting ?? this.isSubmitting,
      failure: clearFailure ? null : (failure ?? this.failure),
      submitFailure: clearSubmitFailure
          ? null
          : (submitFailure ?? this.submitFailure),
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
      state = state.copyWith(
        accounts: accounts,
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

  Future<void> retry() async {
    state = state.copyWith(
      isLoading: true,
      clearFailure: true,
      clearSubmitFailure: true,
    );
    await _loadBankAccounts();
  }

  Future<bool> addBankAccount(SaveBankAccountRequest request) async {
    if (state.isSubmitting) return false;

    state = state.copyWith(isSubmitting: true, clearSubmitFailure: true);
    try {
      await ref.read(addBankAccountUseCaseProvider)(request);
      if (!ref.mounted) return false;

      await _loadBankAccounts();
      if (!ref.mounted) return false;
      if (state.failure != null) {
        state = state.copyWith(
          isSubmitting: false,
          submitFailure: state.failure,
        );
        return false;
      }

      state = state.copyWith(isSubmitting: false, clearSubmitFailure: true);
      return true;
    } on AppException catch (error) {
      if (!ref.mounted) return false;
      state = state.copyWith(isSubmitting: false, submitFailure: error.failure);
      return false;
    } on Object {
      if (!ref.mounted) return false;
      state = state.copyWith(
        isSubmitting: false,
        submitFailure: const Failure.unknown(),
      );
      return false;
    }
  }

  Future<bool> updateBankAccount(
    String id,
    SaveBankAccountRequest request,
  ) async {
    if (state.isSubmitting) return false;

    state = state.copyWith(isSubmitting: true, clearSubmitFailure: true);
    try {
      await ref.read(updateBankAccountUseCaseProvider)(id, request);
      if (!ref.mounted) return false;

      await _loadBankAccounts();
      if (!ref.mounted) return false;
      if (state.failure != null) {
        state = state.copyWith(
          isSubmitting: false,
          submitFailure: state.failure,
        );
        return false;
      }

      state = state.copyWith(isSubmitting: false, clearSubmitFailure: true);
      return true;
    } on AppException catch (error) {
      if (!ref.mounted) return false;
      state = state.copyWith(isSubmitting: false, submitFailure: error.failure);
      return false;
    } on Object {
      if (!ref.mounted) return false;
      state = state.copyWith(
        isSubmitting: false,
        submitFailure: const Failure.unknown(),
      );
      return false;
    }
  }
}
