import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/app_status/presentation/controllers/app_status_controller.dart';
import 'package:intelibill_mobile/src/features/suppliers/data/data_sources/supplier_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/suppliers/data/repositories/supplier_repository_impl.dart';
import 'package:intelibill_mobile/src/features/suppliers/domain/entities/supplier.dart';
import 'package:intelibill_mobile/src/features/suppliers/domain/repositories/supplier_repository.dart';
import 'package:intelibill_mobile/src/features/suppliers/domain/use_cases/create_supplier.dart';
import 'package:intelibill_mobile/src/features/suppliers/domain/use_cases/get_suppliers.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'suppliers_controller.g.dart';

@riverpod
SupplierRemoteDataSource supplierRemoteDataSource(Ref ref) {
  final apiClient = ref.watch(apiClientProvider);
  return SupplierRemoteDataSourceImpl(apiClient);
}

@riverpod
SupplierRepository supplierRepository(Ref ref) {
  final remoteDataSource = ref.watch(supplierRemoteDataSourceProvider);
  return SupplierRepositoryImpl(remoteDataSource);
}

@riverpod
GetSuppliers getSuppliersUseCase(Ref ref) {
  final repository = ref.watch(supplierRepositoryProvider);
  return GetSuppliers(repository);
}

@riverpod
CreateSupplier createSupplierUseCase(Ref ref) {
  final repository = ref.watch(supplierRepositoryProvider);
  return CreateSupplier(repository);
}

@immutable
class SuppliersState {
  const SuppliersState({
    this.suppliers = const [],
    this.searchQuery = '',
    this.isLoading = false,
    this.isSubmitting = false,
    this.failure,
    this.submitFailure,
  });

  final List<Supplier> suppliers;
  final String searchQuery;
  final bool isLoading;
  final bool isSubmitting;
  final Failure? failure;
  final Failure? submitFailure;

  List<Supplier> get filteredSuppliers {
    final activeSuppliers = suppliers.where((s) => !s.isSystem).toList();
    if (searchQuery.isEmpty) return activeSuppliers;
    final query = searchQuery.toLowerCase();
    return activeSuppliers.where((supplier) {
      return supplier.name.toLowerCase().contains(query) ||
          (supplier.contactPersonName?.toLowerCase().contains(query) ??
              false) ||
          (supplier.city?.toLowerCase().contains(query) ?? false) ||
          (supplier.state?.toLowerCase().contains(query) ?? false);
    }).toList();
  }

  SuppliersState copyWith({
    List<Supplier>? suppliers,
    String? searchQuery,
    bool? isLoading,
    bool? isSubmitting,
    Failure? failure,
    Failure? submitFailure,
    bool clearError = false,
    bool clearSubmitError = false,
  }) {
    return SuppliersState(
      suppliers: suppliers ?? this.suppliers,
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
class SuppliersController extends _$SuppliersController {
  @override
  SuppliersState build() {
    unawaited(Future.microtask(_loadSuppliers));
    return const SuppliersState(isLoading: true);
  }

  Future<void> _loadSuppliers() async {
    final useCase = ref.read(getSuppliersUseCaseProvider);
    try {
      final suppliers = await useCase();
      if (!ref.mounted) return;
      state = state.copyWith(suppliers: suppliers, isLoading: false);
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
    await _loadSuppliers();
  }

  Future<bool> createSupplier({
    required String name,
    String? contactPersonName,
    String? contactPersonPhone,
    required String address,
    required String city,
    required String state,
    required String pin,
    required bool isActive,
    required bool isPreferred,
  }) async {
    if (this.state.isSubmitting) {
      return false;
    }

    this.state = this.state.copyWith(
      isSubmitting: true,
      clearSubmitError: true,
    );
    final useCase = ref.read(createSupplierUseCaseProvider);
    try {
      await useCase(
        name: name,
        contactPersonName: contactPersonName,
        contactPersonPhone: contactPersonPhone,
        address: address,
        city: city,
        state: state,
        pin: pin,
        isActive: isActive,
        isPreferred: isPreferred,
      );
      if (!ref.mounted) return false;
      await refresh();
      if (!ref.mounted) return false;
      final refreshFailure = this.state.failure;
      if (refreshFailure != null) {
        this.state = this.state.copyWith(
          isSubmitting: false,
          submitFailure: refreshFailure,
        );
        return false;
      }
      this.state = this.state.copyWith(
        isSubmitting: false,
        clearSubmitError: true,
      );
      return true;
    } on AppException catch (error) {
      if (!ref.mounted) return false;
      this.state = this.state.copyWith(
        isSubmitting: false,
        submitFailure: error.failure,
      );
      return false;
    } on Object {
      if (!ref.mounted) return false;
      this.state = this.state.copyWith(
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
