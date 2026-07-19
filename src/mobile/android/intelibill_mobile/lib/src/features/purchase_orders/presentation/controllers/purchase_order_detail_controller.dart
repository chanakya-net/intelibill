import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/data_sources/purchase_order_draft_local_data_source.dart';
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
    this.cancelState = const PurchaseOrderCancelState(),
    this.closeState = const PurchaseOrderCloseState(),
    this.placeState = const PurchaseOrderPlaceState(),
    this.deleteState = const PurchaseOrderDeleteState(),
  });

  final PurchaseOrder? detail;
  final bool isLoading;
  final Failure? failure;
  final PurchaseOrderCancelState cancelState;
  final PurchaseOrderCloseState closeState;
  final PurchaseOrderPlaceState placeState;
  final PurchaseOrderDeleteState deleteState;

  PurchaseOrderDetailState copyWith({
    PurchaseOrder? detail,
    bool? isLoading,
    Failure? failure,
    PurchaseOrderCancelState? cancelState,
    PurchaseOrderCloseState? closeState,
    PurchaseOrderPlaceState? placeState,
    PurchaseOrderDeleteState? deleteState,
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
      deleteState: deleteState ?? this.deleteState,
    );
  }
}

@immutable
class PurchaseOrderCancelState {
  const PurchaseOrderCancelState({
    this.isLoading = false,
    this.failure,
  });

  final bool isLoading;
  final Failure? failure;

  PurchaseOrderCancelState copyWith({
    bool? isLoading,
    Failure? failure,
    bool clearFailure = false,
  }) {
    return PurchaseOrderCancelState(
      isLoading: isLoading ?? this.isLoading,
      failure: clearFailure ? null : (failure ?? this.failure),
    );
  }
}

@immutable
class PurchaseOrderCloseState {
  const PurchaseOrderCloseState({this.isLoading = false, this.failure});

  final bool isLoading;
  final Failure? failure;

  PurchaseOrderCloseState copyWith({
    bool? isLoading,
    Failure? failure,
    bool clearFailure = false,
  }) {
    return PurchaseOrderCloseState(
      isLoading: isLoading ?? this.isLoading,
      failure: clearFailure ? null : (failure ?? this.failure),
    );
  }
}

@immutable
class PurchaseOrderDeleteState {
  const PurchaseOrderDeleteState({this.isLoading = false, this.failure});

  final bool isLoading;
  final Failure? failure;

  PurchaseOrderDeleteState copyWith({
    bool? isLoading,
    Failure? failure,
    bool clearFailure = false,
  }) {
    return PurchaseOrderDeleteState(
      isLoading: isLoading ?? this.isLoading,
      failure: clearFailure ? null : (failure ?? this.failure),
    );
  }
}

@immutable
class PurchaseOrderPlaceState {
  const PurchaseOrderPlaceState({this.isLoading = false, this.failure});

  final bool isLoading;
  final Failure? failure;

  PurchaseOrderPlaceState copyWith({
    bool? isLoading,
    Failure? failure,
    bool clearFailure = false,
  }) {
    return PurchaseOrderPlaceState(
      isLoading: isLoading ?? this.isLoading,
      failure: clearFailure ? null : (failure ?? this.failure),
    );
  }
}

@riverpod
class PurchaseOrderDetailController extends _$PurchaseOrderDetailController {
  late String _purchaseOrderId;
  int _loadGeneration = 0;
  bool _hasAuthoritativeReplacement = false;

  @override
  PurchaseOrderDetailState build(String purchaseOrderId) {
    _purchaseOrderId = purchaseOrderId;
    unawaited(Future.microtask(_loadInitial));
    return const PurchaseOrderDetailState(isLoading: true);
  }

  Future<void> _loadInitial() async {
    if (_hasAuthoritativeReplacement) return;
    await _load();
  }

  Future<void> _load() async {
    final generation = ++_loadGeneration;
    final useCase = ref.read(getPurchaseOrderProvider);
    try {
      final detail = await useCase(_purchaseOrderId);
      if (!ref.mounted || generation != _loadGeneration) return;

      state = state.copyWith(
        detail: detail,
        isLoading: false,
        clearFailure: true,
      );
    } on AppException catch (error) {
      if (!ref.mounted || generation != _loadGeneration) return;
      state = state.copyWith(
        isLoading: false,
        failure: error.failure,
        clearDetail: error.failure is NotFoundFailure || state.detail == null,
      );
    } on Object {
      if (!ref.mounted || generation != _loadGeneration) return;
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

  void replaceAuthoritative(PurchaseOrder detail) {
    _hasAuthoritativeReplacement = true;
    _loadGeneration++;
    state = state.copyWith(
      detail: detail,
      isLoading: false,
      clearFailure: true,
    );
  }

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

  Future<bool> delete() async {
    if (state.deleteState.isLoading) return false;

    final localDraftKey = ref.read(
      purchaseOrderDraftLocalKeyProvider(_purchaseOrderId),
    );
    state = state.copyWith(
      deleteState: state.deleteState.copyWith(
        isLoading: true,
        clearFailure: true,
      ),
    );

    try {
      final useCase = ref.read(deletePurchaseOrderDraftProvider);
      await useCase(_purchaseOrderId);
      if (!ref.mounted) return false;

      await _removeLocalDraftAfterPlace(localDraftKey);
      if (!ref.mounted) return false;

      state = state.copyWith(
        clearDetail: true,
        deleteState: state.deleteState.copyWith(isLoading: false),
      );
      ref.invalidate(purchaseOrdersControllerProvider);
      return true;
    } on AppException catch (error) {
      if (!ref.mounted) return false;
      state = state.copyWith(
        deleteState: state.deleteState.copyWith(
          isLoading: false,
          failure: error.failure,
        ),
      );
      rethrow;
    } on Object {
      if (!ref.mounted) return false;
      state = state.copyWith(
        deleteState: state.deleteState.copyWith(
          isLoading: false,
          failure: const Failure.unknown(),
        ),
      );
      rethrow;
    }
  }

  Future<void> place() async {
    if (state.placeState.isLoading) return;

    final localDraftKey = ref.read(
      purchaseOrderDraftLocalKeyProvider(_purchaseOrderId),
    );
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

      await _removeLocalDraftAfterPlace(localDraftKey);
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

  Future<void> _removeLocalDraftAfterPlace(
    PurchaseOrderDraftLocalKey? key,
  ) async {
    if (key == null) return;
    try {
      final source = await ref.read(
        purchaseOrderDraftLocalDataSourceProvider.future,
      );
      await source.remove(key);
    } on Object {
      return;
    }
  }
}
