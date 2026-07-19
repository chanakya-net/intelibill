import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/entities/item.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_draft.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_status.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_providers.dart';
import 'package:intelibill_mobile/src/features/suppliers/domain/entities/supplier.dart';
import 'package:intelibill_mobile/src/features/suppliers/presentation/controllers/suppliers_controller.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'purchase_order_builder_controller.g.dart';

@immutable
class PurchaseOrderBuilderState {
  const PurchaseOrderBuilderState({
    this.suppliers = const [],
    this.selectedSupplier,
    this.orderDate,
    this.expectedDeliveryDate,
    this.supplierReferenceNumber = '',
    this.notes = '',
    this.lines = const [],
    this.selectedCatalogItem,
    this.isLoadingSuppliers = false,
    this.isSaving = false,
    this.failure,
    this.savedDraft,
    this.isSupplierLoadFailure = false,
    this.isLoadingEdit = false,
    this.redirectToDetailId,
  });

  final List<Supplier> suppliers;
  final Supplier? selectedSupplier;
  final DateTime? orderDate;
  final DateTime? expectedDeliveryDate;
  final String supplierReferenceNumber;
  final String notes;
  final List<PurchaseOrderDraftLine> lines;
  final Item? selectedCatalogItem;
  final bool isLoadingSuppliers;
  final bool isSaving;
  final Failure? failure;
  final PurchaseOrder? savedDraft;
  final bool isSupplierLoadFailure;
  final bool isLoadingEdit;
  final String? redirectToDetailId;

  double get expectedTotal =>
      lines.fold(0.0, (sum, line) => sum + line.lineTotal);

  PurchaseOrderBuilderState copyWith({
    List<Supplier>? suppliers,
    Supplier? selectedSupplier,
    DateTime? orderDate,
    DateTime? expectedDeliveryDate,
    String? supplierReferenceNumber,
    String? notes,
    List<PurchaseOrderDraftLine>? lines,
    Item? selectedCatalogItem,
    bool? isLoadingSuppliers,
    bool? isSaving,
    Failure? failure,
    PurchaseOrder? savedDraft,
    bool? isSupplierLoadFailure,
    bool? isLoadingEdit,
    String? redirectToDetailId,
    bool clearSupplier = false,
    bool clearOrderDate = false,
    bool clearExpectedDeliveryDate = false,
    bool clearFailure = false,
    bool clearRedirectToDetail = false,
    bool clearSelectedCatalogItem = false,
  }) {
    return PurchaseOrderBuilderState(
      suppliers: suppliers ?? this.suppliers,
      selectedSupplier: clearSupplier
          ? null
          : (selectedSupplier ?? this.selectedSupplier),
      orderDate: clearOrderDate ? null : (orderDate ?? this.orderDate),
      expectedDeliveryDate: clearExpectedDeliveryDate
          ? null
          : (expectedDeliveryDate ?? this.expectedDeliveryDate),
      supplierReferenceNumber:
          supplierReferenceNumber ?? this.supplierReferenceNumber,
      notes: notes ?? this.notes,
      lines: lines ?? this.lines,
      selectedCatalogItem: clearSelectedCatalogItem
          ? null
          : (selectedCatalogItem ?? this.selectedCatalogItem),
      isLoadingSuppliers: isLoadingSuppliers ?? this.isLoadingSuppliers,
      isSaving: isSaving ?? this.isSaving,
      failure: clearFailure ? null : (failure ?? this.failure),
      savedDraft: savedDraft ?? this.savedDraft,
      isSupplierLoadFailure:
          isSupplierLoadFailure ?? this.isSupplierLoadFailure,
      isLoadingEdit: isLoadingEdit ?? this.isLoadingEdit,
      redirectToDetailId: clearRedirectToDetail
          ? null
          : (redirectToDetailId ?? this.redirectToDetailId),
    );
  }
}

@riverpod
class PurchaseOrderBuilderController extends _$PurchaseOrderBuilderController {
  static const supplierReferenceMaxLength = 100;
  static const notesMaxLength = 1000;
  Future<void>? _supplierLoad;
  PurchaseOrder? _loadedDraft;

  @override
  PurchaseOrderBuilderState build(String target) {
    unawaited(Future.microtask(loadSuppliers));
    if (target != 'new') unawaited(Future.microtask(_loadEditTarget));
    return PurchaseOrderBuilderState(
      isLoadingSuppliers: true,
      isLoadingEdit: target != 'new',
    );
  }

  Future<void> loadSuppliers() async {
    final currentLoad = _supplierLoad;
    if (currentLoad != null) return currentLoad;
    final load = _loadSuppliers();
    _supplierLoad = load;
    await load;
    if (identical(_supplierLoad, load)) _supplierLoad = null;
  }

  Future<void> _loadSuppliers() async {
    state = state.copyWith(isLoadingSuppliers: true, clearFailure: true);
    try {
      final suppliers = await ref.read(getSuppliersUseCaseProvider)();
      if (!ref.mounted) return;
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
      _setLoadFailure(error.failure);
    } on Object {
      _setLoadFailure(const Failure.unknown());
    }
  }

  Future<void> _loadEditTarget() async {
    try {
      final detail = await ref.read(getPurchaseOrderProvider)(target);
      if (!ref.mounted) return;
      if (detail.status != PurchaseOrderStatus.draft) {
        state = state.copyWith(
          isLoadingEdit: false,
          redirectToDetailId: target,
        );
        return;
      }
      _loadedDraft = detail;
      _applyLoadedDraft(detail);
    } on AppException catch (error) {
      _setEditLoadFailure(error.failure);
    } on Object {
      _setEditLoadFailure(const Failure.unknown());
    }
  }

  void _applyLoadedDraft(PurchaseOrder detail) {
    state = state.copyWith(
      selectedSupplier: _selectedSupplier(state.suppliers),
      orderDate: detail.orderDate,
      expectedDeliveryDate: detail.expectedDeliveryDate,
      supplierReferenceNumber: detail.supplierReferenceNumber ?? '',
      notes: detail.notes ?? '',
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
      isLoadingEdit: false,
      clearFailure: true,
    );
  }

  Supplier? _selectedSupplier(List<Supplier> suppliers) {
    final supplierId = _loadedDraft?.supplierId;
    if (supplierId == null) return null;
    for (final supplier in suppliers) {
      if (supplier.supplierId == supplierId) return supplier;
    }
    return null;
  }

  void selectSupplier(Supplier? supplier) {
    state = state.copyWith(
      selectedSupplier: supplier,
      clearSupplier: supplier == null,
      clearFailure: true,
    );
  }

  void setOrderDate(DateTime? date) {
    if (date != null && _isAfter(date, state.expectedDeliveryDate)) {
      _setValidation('Order date cannot be after expected delivery date.');
      return;
    }
    state = state.copyWith(
      orderDate: date,
      clearOrderDate: date == null,
      clearFailure: true,
    );
  }

  void setExpectedDeliveryDate(DateTime? date) {
    if (date != null && _isBefore(date, state.orderDate)) {
      _setValidation(
        'Expected delivery date cannot be before order date.',
      );
      return;
    }
    state = state.copyWith(
      expectedDeliveryDate: date,
      clearExpectedDeliveryDate: date == null,
      clearFailure: true,
    );
  }

  void setSupplierReferenceNumber(String value) {
    state = state.copyWith(
      supplierReferenceNumber: value,
      clearFailure: true,
    );
  }

  void setNotes(String value) {
    state = state.copyWith(notes: value, clearFailure: true);
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
    try {
      PurchaseOrderDraftLine.validate(
        itemId: itemId,
        description: description,
        expectedQuantity: expectedQuantity,
        unitCost: unitCost,
      );
      state = state.copyWith(clearFailure: true);
    } catch (e) {
      _setValidation('Invalid line values: ${e.toString()}');
      return;
    }

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
  }

  void updateLine({
    required int index,
    required int expectedQuantity,
    required double unitCost,
  }) {
    if (index < 0 || index >= state.lines.length) return;
    final line = state.lines[index];
    try {
      PurchaseOrderDraftLine.validate(
        itemId: line.itemId,
        description: line.description,
        expectedQuantity: expectedQuantity,
        unitCost: unitCost,
      );
      state = state.copyWith(clearFailure: true);
    } catch (e) {
      _setValidation('Invalid line values: ${e.toString()}');
      return;
    }

    final updated = [...state.lines];
    updated[index] = PurchaseOrderDraftLine(
      itemId: line.itemId,
      description: line.description,
      expectedQuantity: expectedQuantity,
      unitCost: unitCost,
    );
    state = state.copyWith(lines: updated);
  }

  void removeLine(int index) {
    if (index < 0 || index >= state.lines.length) return;
    final updated = [...state.lines];
    updated.removeAt(index);
    state = state.copyWith(lines: updated, clearFailure: true);
  }

  Future<PurchaseOrder?> save() async {
    if (state.isSaving) return null;
    final validation = _validate();
    if (validation != null) {
      _setValidation(validation);
      return null;
    }

    state = state.copyWith(isSaving: true, clearFailure: true);
    try {
      final draft = _draftFromState();
      final result = target == 'new'
          ? await ref.read(createPurchaseOrderDraftProvider)(draft)
          : await ref.read(updatePurchaseOrderDraftProvider)(target, draft);
      if (!ref.mounted) return null;
      state = state.copyWith(
        isSaving: false,
        savedDraft: result,
        clearFailure: true,
      );
      return result;
    } on AppException catch (error) {
      _setSaveFailure(error.failure);
    } on Object {
      _setSaveFailure(const Failure.unknown());
    }
    return null;
  }

  PurchaseOrderDraft _draftFromState() {
    return PurchaseOrderDraft(
      supplierId: state.selectedSupplier?.supplierId,
      orderDate: state.orderDate,
      expectedDeliveryDate: state.expectedDeliveryDate,
      supplierReferenceNumber: state.supplierReferenceNumber,
      notes: state.notes,
      lines: state.lines,
    );
  }

  String? _validate() {
    if (state.supplierReferenceNumber.trim().length >
        supplierReferenceMaxLength) {
      return 'Supplier reference must be 100 characters or fewer.';
    }
    if (state.notes.trim().length > notesMaxLength) {
      return 'Notes must be 1000 characters or fewer.';
    }
    if (_isAfter(state.orderDate, state.expectedDeliveryDate)) {
      return 'Order date cannot be after expected delivery date.';
    }
    return null;
  }

  bool _isAfter(DateTime? first, DateTime? second) {
    return first != null && second != null && first.isAfter(second);
  }

  bool _isBefore(DateTime? first, DateTime? second) {
    return first != null && second != null && first.isBefore(second);
  }

  void _setValidation(String message) {
    state = state.copyWith(failure: Failure.validation(message: message));
  }

  void _setLoadFailure(Failure failure) {
    if (!ref.mounted) return;
    state = state.copyWith(
      isLoadingSuppliers: false,
      failure: failure,
      isSupplierLoadFailure: true,
    );
  }

  void _setEditLoadFailure(Failure failure) {
    if (!ref.mounted) return;
    state = state.copyWith(
      isLoadingEdit: false,
      failure: failure,
      redirectToDetailId: target,
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
