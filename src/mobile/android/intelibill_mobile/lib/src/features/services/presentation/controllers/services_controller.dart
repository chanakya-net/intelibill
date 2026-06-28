import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/network/api_client_provider.dart';
import 'package:intelibill_mobile/src/features/services/data/data_sources/services_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/services/data/repositories/services_repository_impl.dart';
import 'package:intelibill_mobile/src/features/services/domain/entities/service.dart';
import 'package:intelibill_mobile/src/features/services/domain/repositories/services_repository.dart';
import 'package:intelibill_mobile/src/features/services/domain/use_cases/activate_service.dart';
import 'package:intelibill_mobile/src/features/services/domain/use_cases/create_service.dart';
import 'package:intelibill_mobile/src/features/services/domain/use_cases/deactivate_service.dart';
import 'package:intelibill_mobile/src/features/services/domain/use_cases/get_services.dart';
import 'package:intelibill_mobile/src/features/services/domain/use_cases/update_service.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'services_controller.g.dart';

enum ServiceFilterOption { all, active, inactive }

@riverpod
ServicesRemoteDataSource servicesRemoteDataSource(Ref ref) {
  final apiClient = ref.watch(apiClientProvider);
  return ServicesRemoteDataSourceImpl(apiClient);
}

@riverpod
ServicesRepository servicesRepository(Ref ref) {
  final remoteDataSource = ref.watch(servicesRemoteDataSourceProvider);
  return ServicesRepositoryImpl(remoteDataSource);
}

@riverpod
GetServices getServicesUseCase(Ref ref) {
  final repository = ref.watch(servicesRepositoryProvider);
  return GetServices(repository);
}

@riverpod
CreateService createServiceUseCase(Ref ref) {
  final repository = ref.watch(servicesRepositoryProvider);
  return CreateService(repository);
}

@riverpod
UpdateService updateServiceUseCase(Ref ref) {
  final repository = ref.watch(servicesRepositoryProvider);
  return UpdateService(repository);
}

@riverpod
ActivateService activateServiceUseCase(Ref ref) {
  final repository = ref.watch(servicesRepositoryProvider);
  return ActivateService(repository);
}

@riverpod
DeactivateService deactivateServiceUseCase(Ref ref) {
  final repository = ref.watch(servicesRepositoryProvider);
  return DeactivateService(repository);
}

@immutable
class ServicesState {
  const ServicesState({
    this.services = const [],
    this.searchQuery = '',
    this.filter = ServiceFilterOption.all,
    this.isLoading = false,
    this.isSubmitting = false,
    this.failure,
    this.submitFailure,
  });

  final List<Service> services;
  final String searchQuery;
  final ServiceFilterOption filter;
  final bool isLoading;
  final bool isSubmitting;
  final Failure? failure;
  final Failure? submitFailure;

  List<Service> get filteredServices {
    final query = searchQuery.trim().toLowerCase();
    final items = query.isEmpty
        ? services
        : services.where(
            (service) =>
                service.name.toLowerCase().contains(query) ||
                (service.code.toLowerCase().contains(query)) ||
                (service.description?.toLowerCase().contains(query) ?? false),
          );

    return switch (filter) {
      ServiceFilterOption.active =>
        items.where((service) => service.isActive).toList(),
      ServiceFilterOption.inactive =>
        items.where((service) => !service.isActive).toList(),
      ServiceFilterOption.all => items.toList(),
    };
  }

  ServicesState copyWith({
    List<Service>? services,
    String? searchQuery,
    ServiceFilterOption? filter,
    bool? isLoading,
    bool? isSubmitting,
    Failure? failure,
    Failure? submitFailure,
    bool clearError = false,
    bool clearSubmitError = false,
  }) {
    return ServicesState(
      services: services ?? this.services,
      searchQuery: searchQuery ?? this.searchQuery,
      filter: filter ?? this.filter,
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
class ServicesController extends _$ServicesController {
  @override
  ServicesState build() {
    unawaited(Future.microtask(_loadServices));
    return const ServicesState(isLoading: true);
  }

  Future<void> _loadServices() async {
    final useCase = ref.read(getServicesUseCaseProvider);

    final includeInactive = state.filter != ServiceFilterOption.active;
    try {
      final services = await useCase(
        includeInactive: includeInactive,
        search: state.searchQuery,
      );
      if (!ref.mounted) return;
      state = state.copyWith(services: services, isLoading: false);
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
    state = state.copyWith(
      isLoading: true,
      clearError: true,
      clearSubmitError: true,
    );
    await _loadServices();
  }

  void updateSearch(String query) {
    state = state.copyWith(searchQuery: query);
    unawaited(refresh());
  }

  void setFilter(ServiceFilterOption filter) {
    if (state.filter == filter) return;
    state = state.copyWith(
      filter: filter,
      clearError: true,
      clearSubmitError: true,
    );
    unawaited(refresh());
  }

  Future<bool> createService({
    required String name,
    String? description,
    required double price,
    String? hsnCode,
    required double taxRatePercent,
    required bool taxIncluded,
    required bool isActive,
  }) {
    return _runMutation(() async {
      final useCase = ref.read(createServiceUseCaseProvider);
      await useCase(
        name: name,
        description: description,
        price: price,
        hsnCode: hsnCode,
        taxRatePercent: taxRatePercent,
        taxIncluded: taxIncluded,
        isActive: isActive,
      );
    });
  }

  Future<bool> updateService({
    required String serviceId,
    required String name,
    String? description,
    required double price,
    String? hsnCode,
    required double taxRatePercent,
    required bool taxIncluded,
  }) {
    return _runMutation(() async {
      final useCase = ref.read(updateServiceUseCaseProvider);
      await useCase(
        serviceId: serviceId,
        name: name,
        description: description,
        price: price,
        hsnCode: hsnCode,
        taxRatePercent: taxRatePercent,
        taxIncluded: taxIncluded,
      );
    });
  }

  Future<bool> activateService(String serviceId) {
    return _runMutation(() async {
      final useCase = ref.read(activateServiceUseCaseProvider);
      await useCase(serviceId);
    });
  }

  Future<bool> deactivateService(String serviceId) {
    return _runMutation(() async {
      final useCase = ref.read(deactivateServiceUseCaseProvider);
      await useCase(serviceId);
    });
  }

  Future<bool> _runMutation(Future<void> Function() action) async {
    if (state.isSubmitting) return false;

    state = state.copyWith(
      isSubmitting: true,
      clearSubmitError: true,
    );

    try {
      await action();
      if (!ref.mounted) return false;

      await refresh();
      if (!ref.mounted) return false;

      if (state.failure != null) {
        state = state.copyWith(
          isSubmitting: false,
          submitFailure: state.failure,
        );
        return false;
      }

      state = state.copyWith(
        isSubmitting: false,
        clearSubmitError: true,
      );
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
}
