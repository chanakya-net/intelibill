part of 'purchase_order_builder_controller.dart';

mixin _PurchaseOrderBuilderDraftActions on _PurchaseOrderBuilderDraftLifecycle {
  Future<bool> retryStorage() async {
    final persistence = _persistence ?? await _ensurePersistence();
    if (_pendingSavedDraft != null) {
      _pendingCleanupPersistence ??= persistence;
      return _completeSuccessfulSaveCleanup();
    }
    if (persistence == null) return false;
    await persistence.persistNow(_draftFromState());
    return state.storageWarning == null;
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
      _setRemoveWarning('Local draft could not be removed. Retry.');
      return false;
    }
    try {
      if (persistence != null) await persistence.stopAndRemove();
      persistence?.resume();
      return true;
    } on Object {
      persistence?.resume();
      _setRemoveWarning('Local draft could not be removed. Retry.');
      return false;
    }
  }

  Future<bool> discardLocalDraft() async {
    if (state.isDraftActionInProgress) return false;
    state = state.copyWith(isDraftActionInProgress: true, clearFailure: true);
    final persistence = _persistence ?? await _ensurePersistence();
    if (_scopeKey != null && persistence == null) {
      _setRemoveWarning('Local draft could not be removed. Retry discard.');
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
      _setRemoveWarning('Local draft could not be removed. Retry discard.');
      return false;
    }
  }

  void _setRemoveWarning(String message) {
    if (!ref.mounted) return;
    state = state.copyWith(
      isDraftActionInProgress: false,
      storageWarning: message,
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
      storageWarning:
          'The purchase order was saved, but local cleanup failed. Retry.',
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
