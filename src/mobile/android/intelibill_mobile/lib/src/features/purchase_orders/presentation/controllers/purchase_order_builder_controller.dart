import 'dart:async';

import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/entities/item.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/data_sources/purchase_order_draft_local_data_source.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_draft.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_status.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_builder_state.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_detail_controller.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_draft_persistence.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_providers.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_orders_controller.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/localization/purchase_order_messages.dart';
import 'package:intelibill_mobile/src/features/suppliers/domain/entities/supplier.dart';
import 'package:intelibill_mobile/src/features/suppliers/presentation/controllers/suppliers_controller.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

export 'purchase_order_builder_state.dart';

part 'purchase_order_builder_controller.g.dart';
part 'purchase_order_builder_draft_actions.dart';
part 'purchase_order_builder_draft_lifecycle.dart';

@riverpod
class PurchaseOrderBuilderController extends _$PurchaseOrderBuilderController
    with
        _PurchaseOrderBuilderDraftLifecycle,
        _PurchaseOrderBuilderDraftActions {
  static const supplierReferenceMaxLength = 100;
  static const notesMaxLength = 1000;

  @override
  PurchaseOrderBuilderState build(String target) => _buildDraftScope(target);

  void selectSupplier(Supplier? supplier) {
    _desiredSupplierId = supplier?.supplierId;
    state = state.copyWith(
      selectedSupplier: supplier,
      clearSupplier: supplier == null,
      clearFailure: true,
    );
    _scheduleHeaderPersistence();
  }

  void setOrderDate(DateTime? date) {
    if (date != null && _isAfter(date, state.expectedDeliveryDate)) {
      _setValidation(PurchaseOrderMessage.builderOrderDates);
      return;
    }
    state = state.copyWith(
      orderDate: date,
      clearOrderDate: date == null,
      clearFailure: true,
    );
    _scheduleHeaderPersistence();
  }

  void setExpectedDeliveryDate(DateTime? date) {
    if (date != null && _isBefore(date, state.orderDate)) {
      _setValidation(PurchaseOrderMessage.builderOrderDates);
      return;
    }
    state = state.copyWith(
      expectedDeliveryDate: date,
      clearExpectedDeliveryDate: date == null,
      clearFailure: true,
    );
    _scheduleHeaderPersistence();
  }

  void setSupplierReferenceNumber(String value) {
    state = state.copyWith(
      supplierReferenceNumber: value,
      clearFailure: true,
    );
    _scheduleHeaderPersistence();
  }

  void setNotes(String value) {
    state = state.copyWith(notes: value, clearFailure: true);
    _scheduleHeaderPersistence();
  }

  void selectCreatedCatalogItem(Item item) {
    state = state.copyWith(selectedCatalogItem: item, clearFailure: true);
  }

  void selectCatalogItem(Item? item) {
    state = state.copyWith(
      selectedCatalogItem: item,
      clearSelectedCatalogItem: item == null,
      clearFailure: true,
    );
  }

  void addItem({
    required String itemId,
    required String description,
    required int expectedQuantity,
    required double unitCost,
  }) {
    final validation = _lineValidationMessage(
      description: description,
      expectedQuantity: expectedQuantity,
      unitCost: unitCost,
    );
    if (validation != null) {
      _setValidation(validation);
      return;
    }
    state = state.copyWith(clearFailure: true);

    final existingIdx = state.lines.indexWhere((line) => line.itemId == itemId);
    if (existingIdx >= 0) {
      final existing = state.lines[existingIdx];
      final merged = PurchaseOrderDraftLine(
        itemId: itemId,
        description: description,
        expectedQuantity: existing.expectedQuantity + expectedQuantity,
        unitCost: unitCost,
      );
      final updated = [...state.lines];
      updated[existingIdx] = merged;
      state = state.copyWith(lines: updated);
    } else {
      final line = PurchaseOrderDraftLine(
        itemId: itemId,
        description: description,
        expectedQuantity: expectedQuantity,
        unitCost: unitCost,
      );
      state = state.copyWith(lines: [...state.lines, line]);
    }
    _persistLineChange();
  }

  void updateLine({
    required int index,
    required int expectedQuantity,
    required double unitCost,
  }) {
    if (index < 0 || index >= state.lines.length) return;
    final line = state.lines[index];
    final validation = _lineValidationMessage(
      description: line.description,
      expectedQuantity: expectedQuantity,
      unitCost: unitCost,
    );
    if (validation != null) {
      _setValidation(validation);
      return;
    }
    state = state.copyWith(clearFailure: true);

    final updated = [...state.lines];
    updated[index] = PurchaseOrderDraftLine(
      itemId: line.itemId,
      description: line.description,
      expectedQuantity: expectedQuantity,
      unitCost: unitCost,
    );
    state = state.copyWith(lines: updated);
    _persistLineChange();
  }

  void removeLine(int index) {
    if (index < 0 || index >= state.lines.length) return;
    final updated = [...state.lines];
    updated.removeAt(index);
    state = state.copyWith(lines: updated, clearFailure: true);
    _persistLineChange();
  }

  PurchaseOrderMessage? _lineValidationMessage({
    required String description,
    required int expectedQuantity,
    required double unitCost,
  }) {
    if (expectedQuantity <= 0) {
      return PurchaseOrderMessage.builderQuantity;
    }
    if (unitCost < 0) {
      return PurchaseOrderMessage.builderUnitCost;
    }
    if (description.length > 255) {
      return PurchaseOrderMessage.builderDescription;
    }
    return null;
  }

  Future<PurchaseOrder?> save() async {
    if (state.isSaving) return null;
    if (_pendingSavedDraft != null) {
      return await _completeSuccessfulSaveCleanup() ? state.savedDraft : null;
    }
    final validation = _validate();
    if (validation != null) {
      _setValidation(validation);
      return null;
    }

    final generation = _scopeGeneration;
    final cleanupPersistence = _persistence;
    final cleanupKey = _scopeKey;
    state = state.copyWith(isSaving: true, clearFailure: true);
    try {
      final draft = _draftFromState();
      final result = target == 'new'
          ? await ref.read(createPurchaseOrderDraftProvider)(draft)
          : await ref.read(updatePurchaseOrderDraftProvider)(target, draft);
      if (!_isCurrentScope(generation)) {
        await _cleanupStaleSuccessfulMutation(cleanupPersistence, cleanupKey);
        return null;
      }
      _pendingSavedDraft = result;
      _pendingCleanupPersistence = cleanupPersistence;
      return await _completeSuccessfulSaveCleanup() ? result : null;
    } on AppException catch (error) {
      if (_isCurrentScope(generation)) _setSaveFailure(error.failure);
    } on Object {
      if (_isCurrentScope(generation)) {
        _setSaveFailure(const Failure.unknown());
      }
    }
    return null;
  }

  @override
  PurchaseOrderDraft _draftFromState() {
    return PurchaseOrderDraft(
      supplierId: _desiredSupplierId,
      orderDate: state.orderDate,
      expectedDeliveryDate: state.expectedDeliveryDate,
      supplierReferenceNumber: state.supplierReferenceNumber,
      notes: state.notes,
      lines: state.lines,
    );
  }

  PurchaseOrderMessage? _validate() {
    if (state.supplierReferenceNumber.trim().length >
        supplierReferenceMaxLength) {
      return PurchaseOrderMessage.builderReference;
    }
    if (state.notes.trim().length > notesMaxLength) {
      return PurchaseOrderMessage.builderNotes;
    }
    if (_isAfter(state.orderDate, state.expectedDeliveryDate)) {
      return PurchaseOrderMessage.builderOrderDates;
    }
    return null;
  }

  bool _isAfter(DateTime? first, DateTime? second) {
    return first != null && second != null && first.isAfter(second);
  }

  bool _isBefore(DateTime? first, DateTime? second) {
    return first != null && second != null && first.isBefore(second);
  }

  void _setValidation(PurchaseOrderMessage message) {
    state = state.copyWith(
      failure: const Failure.validation(),
      localMessage: message,
    );
  }

  @override
  void _setLoadFailure(Failure failure, int generation) {
    if (!_isCurrentScope(generation)) return;
    state = state.copyWith(
      isLoadingSuppliers: false,
      failure: failure,
      isSupplierLoadFailure: true,
    );
  }

  @override
  void _setEditLoadFailure(Failure failure, int generation) {
    if (!_isCurrentScope(generation)) return;
    state = state.copyWith(
      isLoadingEdit: false,
      failure: failure,
      redirectToDetailId: state.hasRecoveredDraft ? null : target,
      clearRedirectToDetail: state.hasRecoveredDraft,
    );
  }

  void _setSaveFailure(Failure failure) {
    if (!ref.mounted) return;
    state = state.copyWith(
      isSaving: false,
      failure: failure,
      isSupplierLoadFailure: false,
    );
  }
}
