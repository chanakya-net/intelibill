import 'package:flutter/foundation.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_detail.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/controllers/sales_history_controller.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/controllers/sales_providers.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'sale_detail_controller.g.dart';

@immutable
class SaleDetailState {
  const SaleDetailState({
    this.detail,
    this.isLoading = false,
    this.failure,
    this.isVoidSubmitting = false,
    this.voidFailure,
  });

  final SaleDetail? detail;
  final bool isLoading;
  final Failure? failure;
  final bool isVoidSubmitting;
  final Failure? voidFailure;

  SaleDetailState copyWith({
    SaleDetail? detail,
    bool? isLoading,
    Failure? failure,
    bool? isVoidSubmitting,
    Failure? voidFailure,
    bool clearError = false,
    bool clearVoidError = false,
  }) {
    return SaleDetailState(
      detail: detail ?? this.detail,
      isLoading: isLoading ?? this.isLoading,
      failure: clearError ? null : (failure ?? this.failure),
      isVoidSubmitting: isVoidSubmitting ?? this.isVoidSubmitting,
      voidFailure: clearVoidError ? null : (voidFailure ?? this.voidFailure),
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
    final useCase = ref.read(getSaleDetailUseCaseProvider);

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

  Future<bool> voidSaleReturn({
    required String saleReturnId,
    required String reason,
  }) async {
    final trimmedReason = reason.trim();
    if (trimmedReason.isEmpty) {
      return false;
    }

    state = state.copyWith(isVoidSubmitting: true, clearVoidError: true);

    try {
      await ref.read(voidSaleReturnUseCaseProvider)(
        saleReturnId: saleReturnId,
        reason: trimmedReason,
      );
      if (!ref.mounted) return false;
      state = state.copyWith(isVoidSubmitting: false, clearVoidError: true);
      await refresh();
      if (!ref.mounted) return false;
      ref.invalidate(salesHistoryControllerProvider);
      await ref.read(salesHistoryControllerProvider.notifier).refresh();
      return true;
    } on AppException catch (error) {
      if (!ref.mounted) return false;
      state = state.copyWith(
        isVoidSubmitting: false,
        voidFailure: error.failure,
      );
      return false;
    } on Object {
      if (!ref.mounted) return false;
      state = state.copyWith(
        isVoidSubmitting: false,
        voidFailure: const Failure.unknown(),
      );
      return false;
    }
  }
}
