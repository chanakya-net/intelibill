part of 'purchase_order_builder_controller.dart';

mixin _PurchaseOrderBuilderDraftLifecycle on _$PurchaseOrderBuilderController {
  Future<void>? _supplierLoad;
  PurchaseOrder? _pendingSavedDraft;
  PurchaseOrder? _pendingPlacedOrder;
  PurchaseOrderDraftPersistence? _pendingCleanupPersistence;
  PurchaseOrderDraftLocalKey? _scopeKey;
  PurchaseOrderDraftPersistence? _persistence;
  String? _desiredSupplierId;
  int _scopeGeneration = 0;
  bool _isDraftInitialized = false;

  PurchaseOrderDraft _draftFromState();

  void _setLoadFailure(Failure failure, int generation);

  void _setEditLoadFailure(Failure failure, int generation);

  PurchaseOrderBuilderState _buildDraftScope(String target) {
    final key = ref.watch(purchaseOrderDraftLocalKeyProvider(target));
    final generation = _beginScope(key);
    unawaited(Future.microtask(loadSuppliers));
    unawaited(Future.microtask(() => _initializeDraft(generation, key)));
    ref.onDispose(() {
      _scopeGeneration++;
      _persistence?.dispose();
    });
    return PurchaseOrderBuilderState(
      isLoadingSuppliers: true,
      isLoadingEdit: target != 'new',
      isLoadingLocalDraft: key != null,
    );
  }

  int _beginScope(PurchaseOrderDraftLocalKey? key) {
    _persistence?.dispose();
    _persistence = null;
    _supplierLoad = null;
    _pendingSavedDraft = null;
    _pendingPlacedOrder = null;
    _pendingCleanupPersistence = null;
    _scopeKey = key;
    _desiredSupplierId = null;
    _isDraftInitialized = false;
    return ++_scopeGeneration;
  }

  Future<void> _initializeDraft(
    int generation,
    PurchaseOrderDraftLocalKey? key,
  ) async {
    PurchaseOrderDraftLocalRecord? localRecord;
    if (key != null) localRecord = await _loadLocalRecord(generation, key);
    if (!_isCurrentScope(generation)) return;
    if (localRecord != null) _applyRecoveredDraft(localRecord);
    _isDraftInitialized = true;
    state = state.copyWith(isLoadingLocalDraft: false);
    if (target != 'new') await _loadEditTarget(generation);
  }

  Future<PurchaseOrderDraftLocalRecord?> _loadLocalRecord(
    int generation,
    PurchaseOrderDraftLocalKey key,
  ) async {
    try {
      final source = await ref.read(
        purchaseOrderDraftLocalDataSourceProvider.future,
      );
      final record = await source.load(key);
      if (!_isCurrentScope(generation)) return null;
      _attachPersistence(source, key, generation);
      return record;
    } on Object {
      _setStorageWarning(generation);
      return null;
    }
  }

  void _attachPersistence(
    PurchaseOrderDraftLocalDataSource source,
    PurchaseOrderDraftLocalKey key,
    int generation,
  ) {
    _persistence = PurchaseOrderDraftPersistence(
      source: source,
      key: key,
      onSaveSuccess: () => _clearStorageWarning(generation),
      onSaveError: (_) => _setStorageWarning(generation),
    );
  }

  Future<PurchaseOrderDraftPersistence?> _ensurePersistence() async {
    final existing = _persistence;
    if (existing != null) return existing;
    final key = _scopeKey;
    if (key == null) return null;
    final generation = _scopeGeneration;
    try {
      final source = await ref.read(
        purchaseOrderDraftLocalDataSourceProvider.future,
      );
      if (!_isCurrentScope(generation)) return null;
      _attachPersistence(source, key, generation);
      return _persistence;
    } on Object {
      _setStorageWarning(generation);
      return null;
    }
  }

  void _applyRecoveredDraft(PurchaseOrderDraftLocalRecord record) {
    _desiredSupplierId = record.draft.supplierId;
    _applyDraft(record.draft, clearFailure: false);
    state = state.copyWith(
      hasRecoveredDraft: true,
      recoveredDraftUpdatedAt: record.updatedAt,
      isLoadingEdit: false,
    );
  }

  bool _isCurrentScope(int generation) {
    return ref.mounted && generation == _scopeGeneration;
  }

  Future<void> loadSuppliers() async {
    final currentLoad = _supplierLoad;
    if (currentLoad != null) return currentLoad;
    final load = _loadSuppliers(_scopeGeneration);
    _supplierLoad = load;
    await load;
    if (identical(_supplierLoad, load)) _supplierLoad = null;
  }

  Future<void> _loadSuppliers(int generation) async {
    state = state.copyWith(isLoadingSuppliers: true, clearFailure: true);
    try {
      final suppliers = await ref.read(getSuppliersUseCaseProvider)();
      if (!_isCurrentScope(generation)) return;
      final selectableSuppliers = suppliers
          .where((supplier) => supplier.isActive && !supplier.isSystem)
          .toList(growable: false);
      state = state.copyWith(
        suppliers: selectableSuppliers,
        isLoadingSuppliers: false,
        selectedSupplier: _selectedSupplier(selectableSuppliers),
        clearFailure: true,
      );
    } on AppException catch (error) {
      _setLoadFailure(error.failure, generation);
    } on Object {
      _setLoadFailure(const Failure.unknown(), generation);
    }
  }

  Future<void> _loadEditTarget(int generation) async {
    try {
      final detail = await ref.read(getPurchaseOrderProvider)(target);
      if (!_isCurrentScope(generation)) return;
      if (detail.status != PurchaseOrderStatus.draft) {
        state = state.copyWith(
          isLoadingEdit: false,
          redirectToDetailId: target,
        );
        return;
      }
      if (!state.hasRecoveredDraft) {
        _desiredSupplierId = detail.supplierId;
        _applyDraft(_draftFromPurchaseOrder(detail));
      }
      state = state.copyWith(isLoadingEdit: false, isEditableDraft: true);
    } on AppException catch (error) {
      _setEditLoadFailure(error.failure, generation);
    } on Object {
      _setEditLoadFailure(const Failure.unknown(), generation);
    }
  }

  void _applyDraft(PurchaseOrderDraft draft, {bool clearFailure = true}) {
    final supplier = _selectedSupplier(state.suppliers);
    state = state.copyWith(
      selectedSupplier: supplier,
      clearSupplier: supplier == null,
      orderDate: draft.orderDate,
      clearOrderDate: draft.orderDate == null,
      expectedDeliveryDate: draft.expectedDeliveryDate,
      clearExpectedDeliveryDate: draft.expectedDeliveryDate == null,
      supplierReferenceNumber: draft.supplierReferenceNumber ?? '',
      notes: draft.notes ?? '',
      lines: draft.lines,
      clearFailure: clearFailure,
    );
  }

  PurchaseOrderDraft _draftFromPurchaseOrder(PurchaseOrder detail) {
    return PurchaseOrderDraft(
      supplierId: detail.supplierId,
      orderDate: detail.orderDate,
      expectedDeliveryDate: detail.expectedDeliveryDate,
      supplierReferenceNumber: detail.supplierReferenceNumber,
      notes: detail.notes,
      lines: detail.lines
          .map(
            (line) => PurchaseOrderDraftLine(
              itemId: line.itemId,
              description: line.description,
              expectedQuantity: line.expectedQuantity,
              unitCost: line.unitCost,
            ),
          )
          .toList(growable: false),
    );
  }

  Supplier? _selectedSupplier(List<Supplier> suppliers) {
    final supplierId = _desiredSupplierId;
    if (supplierId == null) return null;
    for (final supplier in suppliers) {
      if (supplier.supplierId == supplierId) return supplier;
    }
    return null;
  }

  void _scheduleHeaderPersistence() {
    if (!_isDraftInitialized) return;
    _persistence?.scheduleHeader(_draftFromState);
  }

  void _persistLineChange() {
    if (!_isDraftInitialized) return;
    final persistence = _persistence;
    if (persistence != null) {
      unawaited(persistence.persistNow(_draftFromState()));
    }
  }

  void _setStorageWarning(int generation) {
    if (!_isCurrentScope(generation)) return;
    state = state.copyWith(
      storageWarning: PurchaseOrderMessage.draftSaveStorage,
    );
  }

  void _clearStorageWarning(int generation) {
    if (!_isCurrentScope(generation)) return;
    state = state.copyWith(clearStorageWarning: true);
  }
}
