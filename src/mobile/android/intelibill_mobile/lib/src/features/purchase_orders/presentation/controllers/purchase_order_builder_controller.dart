import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_draft.dart';
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
    this.isLoadingSuppliers = false,
    this.isSaving = false,
    this.failure,
    this.savedDraft,
    this.isSupplierLoadFailure = false,
  });

  final List<Supplier> suppliers;
  final Supplier? selectedSupplier;
  final DateTime? orderDate;
  final DateTime? expectedDeliveryDate;
  final String supplierReferenceNumber;
  final String notes;
  final bool isLoadingSuppliers;
  final bool isSaving;
  final Failure? failure;
  final PurchaseOrder? savedDraft;
  final bool isSupplierLoadFailure;

  PurchaseOrderBuilderState copyWith({
    List<Supplier>? suppliers,
    Supplier? selectedSupplier,
    DateTime? orderDate,
    DateTime? expectedDeliveryDate,
    String? supplierReferenceNumber,
    String? notes,
    bool? isLoadingSuppliers,
    bool? isSaving,
    Failure? failure,
    PurchaseOrder? savedDraft,
    bool? isSupplierLoadFailure,
    bool clearSupplier = false,
    bool clearOrderDate = false,
    bool clearExpectedDeliveryDate = false,
    bool clearFailure = false,
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
      isLoadingSuppliers: isLoadingSuppliers ?? this.isLoadingSuppliers,
      isSaving: isSaving ?? this.isSaving,
      failure: clearFailure ? null : (failure ?? this.failure),
      savedDraft: savedDraft ?? this.savedDraft,
      isSupplierLoadFailure:
          isSupplierLoadFailure ?? this.isSupplierLoadFailure,
    );
  }
}

@riverpod
class PurchaseOrderBuilderController extends _$PurchaseOrderBuilderController {
  static const supplierReferenceMaxLength = 100;
  static const notesMaxLength = 1000;
  Future<void>? _supplierLoad;

  @override
  PurchaseOrderBuilderState build(String target) {
    unawaited(Future.microtask(loadSuppliers));
    return const PurchaseOrderBuilderState(isLoadingSuppliers: true);
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
      state = state.copyWith(
        suppliers: suppliers
            .where((supplier) => supplier.isActive && !supplier.isSystem)
            .toList(growable: false),
        isLoadingSuppliers: false,
        clearFailure: true,
      );
    } on AppException catch (error) {
      _setLoadFailure(error.failure);
    } on Object {
      _setLoadFailure(const Failure.unknown());
    }
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

  Future<PurchaseOrder?> save() async {
    if (state.isSaving) return null;
    final validation = _validate();
    if (validation != null) {
      _setValidation(validation);
      return null;
    }

    state = state.copyWith(isSaving: true, clearFailure: true);
    try {
      final draft = PurchaseOrderDraft(
        supplierId: state.selectedSupplier?.supplierId,
        orderDate: state.orderDate,
        expectedDeliveryDate: state.expectedDeliveryDate,
        supplierReferenceNumber: state.supplierReferenceNumber,
        notes: state.notes,
      );
      final result = await ref.read(createPurchaseOrderDraftProvider)(draft);
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

  void _setSaveFailure(Failure failure) {
    if (!ref.mounted) return;
    state = state.copyWith(
      isSaving: false,
      failure: failure,
      isSupplierLoadFailure: false,
    );
  }
}
