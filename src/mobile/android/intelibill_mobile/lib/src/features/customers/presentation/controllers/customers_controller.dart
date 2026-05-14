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

@immutable
class CustomersState {
  const CustomersState({
    this.customers = const [],
    this.searchQuery = '',
    this.isLoading = false,
    this.errorMessage,
  });

  final List<Customer> customers;
  final String searchQuery;
  final bool isLoading;
  final String? errorMessage;

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
    String? errorMessage,
    bool clearError = false,
  }) {
    return CustomersState(
      customers: customers ?? this.customers,
      searchQuery: searchQuery ?? this.searchQuery,
      isLoading: isLoading ?? this.isLoading,
      errorMessage: clearError ? null : (errorMessage ?? this.errorMessage),
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
      state = state.copyWith(
        isLoading: false,
        errorMessage: _mapFailureToMessage(error),
      );
    } on Object catch (error) {
      if (!ref.mounted) return;
      state = state.copyWith(
        isLoading: false,
        errorMessage: error.toString(),
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

const _networkErrorMessage = 'Unable to connect. Please check your network.';
const _timeoutErrorMessage = 'Request timed out. Please try again.';
const _genericErrorMessage = 'Unable to load customers. Please try again.';

String _mapFailureToMessage(AppException error) {
  return error.failure.when(
    validation: (String? message, Map<String, List<String>>? _) =>
        message ?? _genericErrorMessage,
    unauthorized: (String? _) => 'Session expired. Please log in again.',
    forbidden: (String? _) => 'You do not have permission to view customers.',
    notFound: (String? _) => _genericErrorMessage,
    server: (String? message, int? _) => message ?? _genericErrorMessage,
    network: (String? _) => _networkErrorMessage,
    timeout: (String? _) => _timeoutErrorMessage,
    serialization: (String? _) => _genericErrorMessage,
    unknown: (String? _) => _genericErrorMessage,
  );
}
