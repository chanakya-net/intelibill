part of 'purchase_order_builder_controller.dart';

mixin _PurchaseOrderBuilderDraftActions on _PurchaseOrderBuilderDraftLifecycle {
  Future<bool> retryStorage() async {
    final persistence = _persistence ?? await _ensurePersistence();
    if (_pendingPlacedOrder != null) {
      return _completeSuccessfulPlaceCleanup();
    }
    if (_pendingSavedDraft != null) {
      _pendingCleanupPersistence ??= persistence;
      return _completeSuccessfulSaveCleanup();
    }
    if (persistence == null) return false;
    await persistence.persistNow(_draftFromState());
    return state.storageWarning == null;
  }

  Future<PurchaseOrder?> place() async {
    if (state.isPlacing || !state.canPlace) return null;

    final generation = _scopeGeneration;
    final cleanupPersistence = _persistence;
    final cleanupKey = _scopeKey;
    state = state.copyWith(isPlacing: true, clearFailure: true);
    try {
      final placed = await ref.read(placePurchaseOrderProvider)(target);
      if (!_isCurrentScope(generation)) {
        await _cleanupStaleSuccessfulMutation(cleanupPersistence, cleanupKey);
        return null;
      }
      _pendingPlacedOrder = placed;
      _pendingCleanupPersistence = cleanupPersistence;
      return await _completeSuccessfulPlaceCleanup() ? placed : null;
    } on AppException catch (error) {
      await _finishPlaceFailure(error.failure);
    } on Object {
      await _finishPlaceFailure(const Failure.unknown());
    }
    return null;
  }

  Future<bool> _completeSuccessfulPlaceCleanup() async {
    final placed = _pendingPlacedOrder;
    if (placed == null) return false;
    final persistence =
        _pendingCleanupPersistence ?? await _ensurePersistence();
    _pendingCleanupPersistence = persistence;
    if (_scopeKey != null && persistence == null) {
      _setPlaceCleanupWarning();
      return false;
    }
    try {
      if (persistence != null) await persistence.stopAndRemove();
      if (!ref.mounted) return false;
      ref
          .read(purchaseOrderDetailControllerProvider(target).notifier)
          .replaceAuthoritative(placed);
      ref.invalidate(purchaseOrdersControllerProvider);
      _pendingPlacedOrder = null;
      _pendingCleanupPersistence = null;
      state = state.copyWith(
        isPlacing: false,
        redirectToDetailId: target,
        clearFailure: true,
        clearStorageWarning: true,
      );
      return true;
    } on Object {
      _setPlaceCleanupWarning();
      return false;
    }
  }

  Future<void> _finishPlaceFailure(Failure failure) async {
    if (!ref.mounted) return;
    state = state.copyWith(isPlacing: false, failure: failure);
    await ref
        .read(purchaseOrderDetailControllerProvider(target).notifier)
        .refresh();
    throw AppException(failure: failure);
  }

  void _setPlaceCleanupWarning() {
    if (!ref.mounted) return;
    state = state.copyWith(
      isPlacing: false,
      storageWarning: PurchaseOrderMessage.draftPlaceCleanup,
    );
  }

  void continueRecoveredDraft() {
    state = state.copyWith(clearRecoveredDraft: true);
  }

  Future<bool> discardRecoveredDraftAndReload() async {
    if (state.isDraftActionInProgress) return false;
    state = state.copyWith(
      isDraftActionInProgress: true,
      clearFailure: true,
    );
    final replacement = await _discardReplacement();
    if (replacement == null) return false;
    if (!await _removeLocalForEditableAction()) return false;
    _desiredSupplierId = replacement.supplierId;
    _applyDraft(replacement);
    state = state.copyWith(
      isDraftActionInProgress: false,
      clearRecoveredDraft: true,
      clearStorageWarning: true,
      clearFailure: true,
    );
    return true;
  }

  Future<PurchaseOrderDraft?> _discardReplacement() async {
    if (target == 'new') return const PurchaseOrderDraft();
    try {
      final detail = await ref.read(getPurchaseOrderProvider)(target);
      if (!ref.mounted) return null;
      if (detail.status != PurchaseOrderStatus.draft) {
        state = state.copyWith(
          isDraftActionInProgress: false,
          redirectToDetailId: target,
        );
        return null;
      }
      return _draftFromPurchaseOrder(detail);
    } on AppException catch (error) {
      _setDiscardReloadFailure(error.failure);
    } on Object {
      _setDiscardReloadFailure(const Failure.unknown());
    }
    return null;
  }

  Future<bool> _removeLocalForEditableAction() async {
    final persistence = _persistence ?? await _ensurePersistence();
    if (_scopeKey != null && persistence == null) {
      _setRemoveWarning();
      return false;
    }
    try {
      if (persistence != null) await persistence.stopAndRemove();
      persistence?.resume();
      return true;
    } on Object {
      persistence?.resume();
      _setRemoveWarning();
      return false;
    }
  }

  Future<bool> discardLocalDraft() async {
    if (state.isDraftActionInProgress) return false;
    state = state.copyWith(isDraftActionInProgress: true, clearFailure: true);
    final persistence = _persistence ?? await _ensurePersistence();
    if (_scopeKey != null && persistence == null) {
      _setRemoveWarning();
      return false;
    }
    try {
      if (persistence != null) await persistence.stopAndRemove();
      if (!ref.mounted) return false;
      state = state.copyWith(
        isDraftActionInProgress: false,
        clearRecoveredDraft: true,
        clearStorageWarning: true,
      );
      return true;
    } on Object {
      persistence?.resume();
      _setRemoveWarning();
      return false;
    }
  }

  void _setRemoveWarning() {
    if (!ref.mounted) return;
    state = state.copyWith(
      isDraftActionInProgress: false,
      storageWarning: PurchaseOrderMessage.draftRemoveStorage,
    );
  }

  void _setDiscardReloadFailure(Failure failure) {
    if (!ref.mounted) return;
    state = state.copyWith(
      isDraftActionInProgress: false,
      failure: failure,
    );
  }

  Future<bool> _completeSuccessfulSaveCleanup() async {
    final saved = _pendingSavedDraft;
    if (saved == null) return false;
    final persistence =
        _pendingCleanupPersistence ?? await _ensurePersistence();
    _pendingCleanupPersistence = persistence;
    if (_scopeKey != null && persistence == null) {
      _setCleanupWarning();
      return false;
    }
    try {
      if (persistence != null) await persistence.stopAndRemove();
      if (!ref.mounted) return false;
      _pendingSavedDraft = null;
      _pendingCleanupPersistence = null;
      state = state.copyWith(
        savedDraft: saved,
        isSaving: false,
        clearFailure: true,
        clearStorageWarning: true,
      );
      return true;
    } on Object {
      _setCleanupWarning();
      return false;
    }
  }

  void _setCleanupWarning() {
    if (!ref.mounted) return;
    state = state.copyWith(
      isSaving: false,
      storageWarning: PurchaseOrderMessage.draftSaveCleanup,
    );
  }

  Future<void> _cleanupStaleSuccessfulMutation(
    PurchaseOrderDraftPersistence? persistence,
    PurchaseOrderDraftLocalKey? key,
  ) async {
    try {
      if (persistence != null) {
        await persistence.stopAndRemove();
      } else if (key != null) {
        final source = await ref.read(
          purchaseOrderDraftLocalDataSourceProvider.future,
        );
        await source.remove(key);
      }
    } on Object {
      // Stale scope completions must not alter the current editor state.
    }
  }
}
