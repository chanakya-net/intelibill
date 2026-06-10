import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/network/api_client_provider.dart';
import 'package:intelibill_mobile/src/features/dashboard/data/data_sources/dashboard_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/dashboard/data/repositories/dashboard_repository_impl.dart';
import 'package:intelibill_mobile/src/features/dashboard/domain/entities/dashboard.dart';
import 'package:intelibill_mobile/src/features/dashboard/domain/repositories/dashboard_repository.dart';
import 'package:intelibill_mobile/src/features/dashboard/domain/use_cases/get_dashboard.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'dashboard_controller.g.dart';

@riverpod
DashboardRemoteDataSource dashboardRemoteDataSource(Ref ref) {
  final apiClient = ref.watch(apiClientProvider);
  return DashboardRemoteDataSourceImpl(apiClient);
}

@riverpod
DashboardRepository dashboardRepository(Ref ref) {
  final remoteDataSource = ref.watch(dashboardRemoteDataSourceProvider);
  return DashboardRepositoryImpl(remoteDataSource);
}

@riverpod
GetDashboard getDashboardUseCase(Ref ref) {
  final repository = ref.watch(dashboardRepositoryProvider);
  return GetDashboard(repository);
}

@immutable
class DashboardState {
  const DashboardState({
    this.dashboard,
    this.selectedPeriod = DashboardPeriod.last30,
    this.customFrom,
    this.customTo,
    this.isLoading = false,
    this.failure,
  });

  final Dashboard? dashboard;
  final DashboardPeriod selectedPeriod;
  final DateTime? customFrom;
  final DateTime? customTo;
  final bool isLoading;
  final Failure? failure;

  DashboardState copyWith({
    Dashboard? dashboard,
    DashboardPeriod? selectedPeriod,
    DateTime? customFrom,
    DateTime? customTo,
    bool? isLoading,
    Failure? failure,
    bool clearError = false,
    bool clearCustomRange = false,
  }) {
    return DashboardState(
      dashboard: dashboard ?? this.dashboard,
      selectedPeriod: selectedPeriod ?? this.selectedPeriod,
      customFrom: clearCustomRange ? null : (customFrom ?? this.customFrom),
      customTo: clearCustomRange ? null : (customTo ?? this.customTo),
      isLoading: isLoading ?? this.isLoading,
      failure: clearError ? null : (failure ?? this.failure),
    );
  }
}

@riverpod
class DashboardController extends _$DashboardController {
  int _loadGeneration = 0;

  @override
  DashboardState build() {
    unawaited(Future.microtask(_loadDashboard));
    return const DashboardState(isLoading: true);
  }

  Future<void> refresh() async {
    await _loadDashboard();
  }

  Future<void> setPeriod(DashboardPeriod period) async {
    state = state.copyWith(
      selectedPeriod: period,
      clearCustomRange: period != DashboardPeriod.custom,
    );

    if (period != DashboardPeriod.custom) {
      await refresh();
    }
  }

  Future<void> setCustomRange({
    required DateTime from,
    required DateTime to,
  }) async {
    state = state.copyWith(
      selectedPeriod: DashboardPeriod.custom,
      customFrom: _startOfDay(from),
      customTo: _startOfDay(to),
    );
    await refresh();
  }

  Future<void> _loadDashboard() async {
    final generation = ++_loadGeneration;
    if (!ref.mounted) return;
    state = state.copyWith(isLoading: true, clearError: true);

    final range = _resolveDateRange();
    if (range == null) {
      if (!ref.mounted || generation != _loadGeneration) return;
      state = state.copyWith(isLoading: false);
      return;
    }

    final useCase = ref.read(getDashboardUseCaseProvider);
    try {
      final dashboard = await useCase(from: range.$1, to: range.$2);
      if (!ref.mounted || generation != _loadGeneration) return;
      state = state.copyWith(dashboard: dashboard, isLoading: false);
    } on AppException catch (error) {
      if (!ref.mounted || generation != _loadGeneration) return;
      state = state.copyWith(isLoading: false, failure: error.failure);
    } on Object {
      if (!ref.mounted || generation != _loadGeneration) return;
      state = state.copyWith(
        isLoading: false,
        failure: const Failure.unknown(),
      );
    }
  }

  (DateTime, DateTime)? _resolveDateRange() {
    final today = _startOfDay(DateTime.now());

    switch (state.selectedPeriod) {
      case DashboardPeriod.last7:
        return (_startOfDay(today.subtract(const Duration(days: 6))), today);
      case DashboardPeriod.last30:
        return (_startOfDay(today.subtract(const Duration(days: 29))), today);
      case DashboardPeriod.custom:
        final from = state.customFrom;
        final to = state.customTo;
        if (from == null || to == null || from.isAfter(to)) {
          return null;
        }
        return (from, to);
    }
  }

  DateTime _startOfDay(DateTime date) {
    return DateTime(date.year, date.month, date.day);
  }
}
