import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_providers.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_orders_controller.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'purchase_order_detail_controller.g.dart';

@immutable
class PurchaseOrderDetailState {
  const PurchaseOrderDetailState({
    this.detail,
    this.isLoading = false,
    this.failure,
    this.cancelState = const _CancelState(),
    this.closeState = const _CloseState(),
    this.placeState = const _PlaceState(),
  });

  final PurchaseOrder? detail;
  final bool isLoading;
  final Failure? failure;
  final _CancelState cancelState;
  final _CloseState closeState;
  final _PlaceState placeState;

  PurchaseOrderDetailState copyWith({
    PurchaseOrder? detail,
    bool? isLoading,
    Failure? failure,
    _CancelState? cancelState,
    _CloseState? closeState,
    _PlaceState? placeState,
    bool clearDetail = false,
    bool clearFailure = false,
  }) {
    return PurchaseOrderDetailState(
      detail: clearDetail ? null : (detail ?? this.detail),
      isLoading: isLoading ?? this.isLoading,
      failure: clearFailure ? null : (failure ?? this.failure),
      cancelState: cancelState ?? this.cancelState,
      closeState: closeState ?? this.closeState,
      placeState: placeState ?? this.placeState,
    );
  }
}

@immutable
class _CancelState {
  const _CancelState({
    this.isLoading = false,
    this.failure,
  });

  final bool isLoading;
  final Failure? failure;

  _CancelState copyWith({
    bool? isLoading,
    Failure? failure,
    bool clearFailure = false,
  }) {
    return _CancelState(
      isLoading: isLoading ?? this.isLoading,
      failure: clearFailure ? null : (failure ?? this.failure),
    );
  }
}

@immutable
class _CloseState {
  const _CloseState({this.isLoading = false, this.failure});

  final bool isLoading;
  final Failure? failure;

  _CloseState copyWith({
    bool? isLoading,
    Failure? failure,
    bool clearFailure = false,
  }) {
    return _CloseState(
      isLoading: isLoading ?? this.isLoading,
      failure: clearFailure ? null : (failure ?? this.failure),
    );
  }
}

@immutable
class _PlaceState {
  const _PlaceState({this.isLoading = false, this.failure});

  final bool isLoading;
  final Failure? failure;

  _PlaceState copyWith({
    bool? isLoading,
    Failure? failure,
    bool clearFailure = false,
  }) {
    return _PlaceState(
      isLoading: isLoading ?? this.isLoading,
      failure: clearFailure ? null : (failure ?? this.failure),
    );
  }
}

@riverpod
class PurchaseOrderDetailController extends _$PurchaseOrderDetailController {
  late String _purchaseOrderId;

  @override
  PurchaseOrderDetailState build(String purchaseOrderId) {
    _purchaseOrderId = purchaseOrderId;
    unawaited(Future.microtask(_load));
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
      );
    } on AppException catch (error) {
      if (!ref.mounted) return;
      state = state.copyWith(
        isLoading: false,
        failure: error.failure,
        clearDetail: error.failure is NotFoundFailure || state.detail == null,
      );
    } on Object {
      if (!ref.mounted) return;
      state = state.copyWith(
        isLoading: false,
        failure: const Failure.unknown(),
        clearDetail: state.detail == null,
      );
    }
  }

  Future<void> refresh() async {
    state = state.copyWith(
      isLoading: true,
      clearFailure: true,
    );
    await _load();
  }

  Future<void> retry() => refresh();

  Future<void> cancel(String reason) async {
    state = state.copyWith(
      cancelState: state.cancelState.copyWith(
        isLoading: true,
        clearFailure: true,
      ),
    );

    try {
      final useCase = ref.read(cancelPurchaseOrderProvider);
      final updated = await useCase(_purchaseOrderId, reason);
      if (!ref.mounted) return;

      state = state.copyWith(
        detail: updated,
        cancelState: state.cancelState.copyWith(isLoading: false),
      );
      ref.invalidate(purchaseOrdersControllerProvider);
    } on AppException catch (error) {
      if (!ref.mounted) return;
      state = state.copyWith(
        cancelState: state.cancelState.copyWith(
          isLoading: false,
          failure: error.failure,
        ),
      );
      await _load();
      rethrow;
    } on Object {
      if (!ref.mounted) return;
      state = state.copyWith(
        cancelState: state.cancelState.copyWith(
          isLoading: false,
          failure: const Failure.unknown(),
        ),
      );
      await _load();
      rethrow;
    }
  }

  Future<void> close(String reason) async {
    state = state.copyWith(
      closeState: state.closeState.copyWith(
        isLoading: true,
        clearFailure: true,
      ),
    );
    try {
      final useCase = ref.read(closePurchaseOrderProvider);
      final updated = await useCase(_purchaseOrderId, reason);
      if (!ref.mounted) return;

      state = state.copyWith(
        detail: updated,
        closeState: state.closeState.copyWith(isLoading: false),
      );
      ref.invalidate(purchaseOrdersControllerProvider);
    } on AppException catch (error) {
      await _finishCloseFailure(error.failure);
    } on Object {
      await _finishCloseFailure(const Failure.unknown());
    }
  }

  Future<void> _finishCloseFailure(Failure failure) async {
    if (!ref.mounted) return;
    state = state.copyWith(
      closeState: state.closeState.copyWith(
        isLoading: false,
        failure: failure,
      ),
    );
    await _load();
    throw AppException(failure: failure);
  }

  Future<void> place() async {
    if (state.placeState.isLoading) return;

    state = state.copyWith(
      placeState: state.placeState.copyWith(
        isLoading: true,
        clearFailure: true,
      ),
    );
    try {
      final useCase = ref.read(placePurchaseOrderProvider);
      final updated = await useCase(_purchaseOrderId);
      if (!ref.mounted) return;

      state = state.copyWith(
        detail: updated,
        placeState: state.placeState.copyWith(isLoading: false),
      );
      ref.invalidate(purchaseOrdersControllerProvider);
    } on AppException catch (error) {
      if (!ref.mounted) return;
      state = state.copyWith(
        placeState: state.placeState.copyWith(
          isLoading: false,
          failure: error.failure,
        ),
      );
      await _load();
      rethrow;
    } on Object {
      if (!ref.mounted) return;
      state = state.copyWith(
        placeState: state.placeState.copyWith(
          isLoading: false,
          failure: const Failure.unknown(),
        ),
      );
      await _load();
      rethrow;
    }
  }
}
