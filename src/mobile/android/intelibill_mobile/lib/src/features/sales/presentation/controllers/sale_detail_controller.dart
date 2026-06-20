import 'package:flutter/foundation.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_detail.dart';
import 'package:intelibill_mobile/src/features/sales/domain/use_cases/get_sale_detail.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/controllers/sales_providers.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'sale_detail_controller.g.dart';

@riverpod
GetSaleDetail getSaleDetail(Ref ref) {
  final repository = ref.watch(salesRepositoryProvider);
  return GetSaleDetail(repository);
}

@immutable
class SaleDetailState {
  const SaleDetailState({
    this.detail,
    this.isLoading = false,
    this.failure,
  });

  final SaleDetail? detail;
  final bool isLoading;
  final Failure? failure;

  SaleDetailState copyWith({
    SaleDetail? detail,
    bool? isLoading,
    Failure? failure,
    bool clearError = false,
  }) {
    return SaleDetailState(
      detail: detail ?? this.detail,
      isLoading: isLoading ?? this.isLoading,
      failure: clearError ? null : (failure ?? this.failure),
    );
  }
}

@riverpod
class SaleDetailController extends _$SaleDetailController {
  late final String _saleId;

  @override
  SaleDetailState build(String saleId) {
    _saleId = saleId;
    Future.microtask(_load);
    return const SaleDetailState(isLoading: true);
  }

  Future<void> _load() async {
    final useCase = ref.read(getSaleDetailProvider);

    try {
      final detail = await useCase(_saleId);
      if (!ref.mounted) return;

      state = state.copyWith(
        detail: detail,
        isLoading: false,
        clearError: true,
      );
    } on AppException catch (error) {
      if (!ref.mounted) return;
      state = state.copyWith(
        isLoading: false,
        failure: error.failure,
      );
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
    await _load();
  }
}
