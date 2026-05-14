import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/app_status/presentation/controllers/app_status_controller.dart';
import 'package:intelibill_mobile/src/features/suppliers/data/data_sources/supplier_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/suppliers/data/repositories/supplier_repository_impl.dart';
import 'package:intelibill_mobile/src/features/suppliers/domain/entities/supplier.dart';
import 'package:intelibill_mobile/src/features/suppliers/domain/repositories/supplier_repository.dart';
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

@immutable
class SuppliersState {
  const SuppliersState({
    this.suppliers = const [],
    this.searchQuery = '',
    this.isLoading = false,
    this.failure,
  });

  final List<Supplier> suppliers;
  final String searchQuery;
  final bool isLoading;
  final Failure? failure;

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
    Failure? failure,
    bool clearError = false,
  }) {
    return SuppliersState(
      suppliers: suppliers ?? this.suppliers,
      searchQuery: searchQuery ?? this.searchQuery,
      isLoading: isLoading ?? this.isLoading,
      failure: clearError ? null : (failure ?? this.failure),
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

  void updateSearch(String query) {
    state = state.copyWith(searchQuery: query);
  }
}
