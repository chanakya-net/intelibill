import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/network/api_client_provider.dart';
import 'package:intelibill_mobile/src/features/users/data/data_sources/shop_user_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/users/data/repositories/shop_user_repository_impl.dart';
import 'package:intelibill_mobile/src/features/users/domain/entities/shop_user.dart';
import 'package:intelibill_mobile/src/features/users/domain/repositories/shop_user_repository.dart';
import 'package:intelibill_mobile/src/features/users/domain/use_cases/add_shop_user.dart';
import 'package:intelibill_mobile/src/features/users/domain/use_cases/edit_shop_user.dart';
import 'package:intelibill_mobile/src/features/users/domain/use_cases/get_shop_users.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'users_controller.g.dart';

@riverpod
ShopUserRemoteDataSource shopUserRemoteDataSource(Ref ref) {
  final apiClient = ref.watch(apiClientProvider);
  return ShopUserRemoteDataSourceImpl(apiClient);
}

@riverpod
ShopUserRepository shopUserRepository(Ref ref) {
  final remoteDataSource = ref.watch(shopUserRemoteDataSourceProvider);
  return ShopUserRepositoryImpl(remoteDataSource);
}

@riverpod
GetShopUsers getShopUsersUseCase(Ref ref) {
  final repository = ref.watch(shopUserRepositoryProvider);
  return GetShopUsers(repository);
}

@riverpod
AddShopUser addShopUserUseCase(Ref ref) {
  final repository = ref.watch(shopUserRepositoryProvider);
  return AddShopUser(repository);
}

@riverpod
EditShopUser editShopUserUseCase(Ref ref) {
  final repository = ref.watch(shopUserRepositoryProvider);
  return EditShopUser(repository);
}

@immutable
class UsersState {
  const UsersState({
    this.users = const [],
    this.searchQuery = '',
    this.isLoading = false,
    this.isSubmitting = false,
    this.failure,
    this.submitFailure,
  });

  final List<ShopUser> users;
  final String searchQuery;
  final bool isLoading;
  final bool isSubmitting;
  final Failure? failure;
  final Failure? submitFailure;

  List<ShopUser> get filteredUsers {
    if (searchQuery.isEmpty) return users;
    final query = searchQuery.toLowerCase();
    return users.where((user) {
      return user.fullName.toLowerCase().contains(query) ||
          (user.phoneNumber ?? '').toLowerCase().contains(query) ||
          (user.email ?? '').toLowerCase().contains(query);
    }).toList();
  }

  UsersState copyWith({
    List<ShopUser>? users,
    String? searchQuery,
    bool? isLoading,
    Failure? failure,
    bool? isSubmitting,
    Failure? submitFailure,
    bool clearError = false,
    bool clearSubmitError = false,
  }) {
    return UsersState(
      users: users ?? this.users,
      searchQuery: searchQuery ?? this.searchQuery,
      isLoading: isLoading ?? this.isLoading,
      isSubmitting: isSubmitting ?? this.isSubmitting,
      failure: clearError ? null : (failure ?? this.failure),
      submitFailure: clearSubmitError
          ? null
          : (submitFailure ?? this.submitFailure),
    );
  }
}

@riverpod
class UsersController extends _$UsersController {
  @override
  UsersState build() {
    unawaited(Future.microtask(_loadUsers));
    return const UsersState(isLoading: true);
  }

  Future<void> _loadUsers() async {
    final useCase = ref.read(getShopUsersUseCaseProvider);
    try {
      final users = await useCase();
      if (!ref.mounted) return;
      state = state.copyWith(users: users, isLoading: false);
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
    state = state.copyWith(isLoading: true, clearError: true);
    await _loadUsers();
  }

  void updateSearch(String query) {
    state = state.copyWith(searchQuery: query);
  }

  Future<bool> addShopUser({
    required List<String> shopIds,
    required String email,
    required String firstName,
    required String lastName,
    required String phoneNumber,
    required String password,
    required String confirmPassword,
    required String role,
  }) async {
    if (state.isSubmitting) {
      return false;
    }

    state = state.copyWith(isSubmitting: true, clearSubmitError: true);
    final useCase = ref.read(addShopUserUseCaseProvider);
    try {
      await useCase(
        shopIds: shopIds,
        email: email,
        firstName: firstName,
        lastName: lastName,
        phoneNumber: phoneNumber,
        password: password,
        confirmPassword: confirmPassword,
        role: role,
      );
      if (!ref.mounted) return false;
      state = state.copyWith(isSubmitting: false, clearSubmitError: true);
      await refresh();
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

  Future<bool> editShopUser({
    required String userId,
    required String email,
    required String firstName,
    required String lastName,
    required String phoneNumber,
    required String role,
    required bool isLoginEnabled,
    required List<String> shopIds,
  }) async {
    if (state.isSubmitting) {
      return false;
    }

    state = state.copyWith(isSubmitting: true, clearSubmitError: true);
    final useCase = ref.read(editShopUserUseCaseProvider);
    try {
      await useCase(
        userId: userId,
        email: email,
        firstName: firstName,
        lastName: lastName,
        phoneNumber: phoneNumber,
        role: role,
        isLoginEnabled: isLoginEnabled,
        shopIds: shopIds,
      );
      if (!ref.mounted) return false;
      state = state.copyWith(isSubmitting: false, clearSubmitError: true);
      await refresh();
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
