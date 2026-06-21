import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/network/api_client_provider.dart';
import 'package:intelibill_mobile/src/features/services/data/data_sources/services_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/services/data/repositories/services_repository_impl.dart';
import 'package:intelibill_mobile/src/features/services/domain/entities/service.dart';
import 'package:intelibill_mobile/src/features/services/domain/repositories/services_repository.dart';
import 'package:intelibill_mobile/src/features/services/domain/use_cases/get_services.dart';
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

@immutable
class ServicesState {
  const ServicesState({
    this.services = const [],
    this.searchQuery = '',
    this.filter = ServiceFilterOption.all,
    this.isLoading = false,
    this.failure,
  });

  final List<Service> services;
  final String searchQuery;
  final ServiceFilterOption filter;
  final bool isLoading;
  final Failure? failure;

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
    Failure? failure,
    bool clearError = false,
  }) {
    return ServicesState(
      services: services ?? this.services,
      searchQuery: searchQuery ?? this.searchQuery,
      filter: filter ?? this.filter,
      isLoading: isLoading ?? this.isLoading,
      failure: clearError ? null : (failure ?? this.failure),
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
    state = state.copyWith(isLoading: true, clearError: true);
    await _loadServices();
  }

  void updateSearch(String query) {
    state = state.copyWith(searchQuery: query);
    unawaited(refresh());
  }

  void setFilter(ServiceFilterOption filter) {
    if (state.filter == filter) return;
    state = state.copyWith(filter: filter, clearError: true);
    unawaited(refresh());
  }
}
