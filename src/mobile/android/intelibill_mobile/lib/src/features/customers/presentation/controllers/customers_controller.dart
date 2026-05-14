import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/app_status/presentation/controllers/app_status_controller.dart';
import 'package:intelibill_mobile/src/features/customers/data/data_sources/customer_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/customers/data/repositories/customer_repository_impl.dart';
import 'package:intelibill_mobile/src/features/customers/domain/entities/customer.dart';
import 'package:intelibill_mobile/src/features/customers/domain/repositories/customer_repository.dart';
import 'package:intelibill_mobile/src/features/customers/domain/use_cases/create_customer.dart';
import 'package:intelibill_mobile/src/features/customers/domain/use_cases/get_customers.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'customers_controller.g.dart';

@riverpod
CustomerRemoteDataSource customerRemoteDataSource(Ref ref) {
  final apiClient = ref.watch(apiClientProvider);
  return CustomerRemoteDataSourceImpl(apiClient);
}

@riverpod
CustomerRepository customerRepository(Ref ref) {
  final remoteDataSource = ref.watch(customerRemoteDataSourceProvider);
  return CustomerRepositoryImpl(remoteDataSource);
}

@riverpod
GetCustomers getCustomersUseCase(Ref ref) {
  final repository = ref.watch(customerRepositoryProvider);
  return GetCustomers(repository);
}

@riverpod
CreateCustomer createCustomerUseCase(Ref ref) {
  final repository = ref.watch(customerRepositoryProvider);
  return CreateCustomer(repository);
}

@immutable
class CustomersState {
  const CustomersState({
    this.customers = const [],
    this.searchQuery = '',
    this.isLoading = false,
    this.isSubmitting = false,
    this.failure,
    this.submitFailure,
  });

  final List<Customer> customers;
  final String searchQuery;
  final bool isLoading;
  final bool isSubmitting;
  final Failure? failure;
  final Failure? submitFailure;

  List<Customer> get filteredCustomers {
    if (searchQuery.isEmpty) return customers;
    final query = searchQuery.toLowerCase();
    return customers.where((customer) {
      return customer.name.toLowerCase().contains(query) ||
          customer.phoneNumber.toLowerCase().contains(query) ||
          (customer.address?.toLowerCase().contains(query) ?? false);
    }).toList();
  }

  CustomersState copyWith({
    List<Customer>? customers,
    String? searchQuery,
    bool? isLoading,
    Failure? failure,
    bool? isSubmitting,
    Failure? submitFailure,
    bool clearError = false,
    bool clearSubmitError = false,
  }) {
    return CustomersState(
      customers: customers ?? this.customers,
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
class CustomersController extends _$CustomersController {
  @override
  CustomersState build() {
    unawaited(Future.microtask(_loadCustomers));
    return const CustomersState(isLoading: true);
  }

  Future<void> _loadCustomers() async {
    final useCase = ref.read(getCustomersUseCaseProvider);
    try {
      final customers = await useCase();
      if (!ref.mounted) return;
      state = state.copyWith(customers: customers, isLoading: false);
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
    await _loadCustomers();
  }

  Future<bool> createCustomer({
    required String name,
    required String phoneNumber,
    String? address,
    required bool isActive,
  }) async {
    if (state.isSubmitting) {
      return false;
    }

    state = state.copyWith(isSubmitting: true, clearSubmitError: true);
    final useCase = ref.read(createCustomerUseCaseProvider);
    try {
      await useCase(
        name: name,
        phoneNumber: phoneNumber,
        address: address,
        isActive: isActive,
      );
      if (!ref.mounted) return false;
      state = state.copyWith(isSubmitting: false, clearSubmitError: true);
      await refresh();
      return true;
    } on AppException catch (error) {
      if (!ref.mounted) return false;
      state = state.copyWith(
        isSubmitting: false,
        submitFailure: error.failure,
      );
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

  void updateSearch(String query) {
    state = state.copyWith(searchQuery: query);
  }
}
