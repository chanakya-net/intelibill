import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_providers.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'purchase_order_detail_controller.g.dart';

@immutable
class PurchaseOrderDetailState {
  const PurchaseOrderDetailState({
    this.detail,
    this.isLoading = false,
    this.failure,
  });

  final PurchaseOrder? detail;
  final bool isLoading;
  final Failure? failure;

  PurchaseOrderDetailState copyWith({
    PurchaseOrder? detail,
    bool? isLoading,
    Failure? failure,
    bool clearDetail = false,
    bool clearFailure = false,
  }) {
    return PurchaseOrderDetailState(
      detail: clearDetail ? null : (detail ?? this.detail),
      isLoading: isLoading ?? this.isLoading,
      failure: clearFailure ? null : (failure ?? this.failure),
    );
  }
}

@riverpod
class PurchaseOrderDetailController extends _$PurchaseOrderDetailController {
  late final String _purchaseOrderId;

  @override
  PurchaseOrderDetailState build(String purchaseOrderId) {
    _purchaseOrderId = purchaseOrderId;
    unawaited(Future.microtask(() => _load()));
    return const PurchaseOrderDetailState(isLoading: true);
  }

  Future<void> _load() async {
    final useCase = ref.read(getPurchaseOrderProvider);
    try {
      final detail = await useCase(_purchaseOrderId);
      if (!ref.mounted) return;

      state = state.copyWith(
        detail: detail,
        isLoading: false,
        clearFailure: true,
        clearDetail: false,
      );
    } on AppException catch (error) {
      if (!ref.mounted) return;
      state = state.copyWith(
        isLoading: false,
        failure: error.failure,
        clearDetail: true,
      );
    } on Object {
      if (!ref.mounted) return;
      state = state.copyWith(
        isLoading: false,
        failure: const Failure.unknown(),
        clearDetail: true,
      );
    }
  }

  Future<void> refresh() async {
    state = state.copyWith(
      isLoading: true,
      clearFailure: true,
      clearDetail: false,
    );
    await _load();
  }

  Future<void> retry() => refresh();
}
