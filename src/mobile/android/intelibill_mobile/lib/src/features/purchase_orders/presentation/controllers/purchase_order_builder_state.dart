import 'package:flutter/foundation.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/entities/item.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_draft.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/localization/purchase_order_messages.dart';
import 'package:intelibill_mobile/src/features/suppliers/domain/entities/supplier.dart';

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
    this.isPlacing = false,
    this.isEditableDraft = false,
    this.failure,
    this.localMessage,
    this.savedDraft,
    this.isSupplierLoadFailure = false,
    this.isLoadingEdit = false,
    this.redirectToDetailId,
    this.isLoadingLocalDraft = false,
    this.hasRecoveredDraft = false,
    this.recoveredDraftUpdatedAt,
    this.storageWarning,
    this.isDraftActionInProgress = false,
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
  final bool isPlacing;
  final bool isEditableDraft;
  final Failure? failure;
  final PurchaseOrderMessage? localMessage;
  final PurchaseOrder? savedDraft;
  final bool isSupplierLoadFailure;
  final bool isLoadingEdit;
  final String? redirectToDetailId;
  final bool isLoadingLocalDraft;
  final bool hasRecoveredDraft;
  final DateTime? recoveredDraftUpdatedAt;
  final PurchaseOrderMessage? storageWarning;
  final bool isDraftActionInProgress;

  double get expectedTotal =>
      lines.fold(0, (sum, line) => sum + line.lineTotal);

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
    bool? isPlacing,
    bool? isEditableDraft,
    Failure? failure,
    PurchaseOrderMessage? localMessage,
    PurchaseOrder? savedDraft,
    bool? isSupplierLoadFailure,
    bool? isLoadingEdit,
    String? redirectToDetailId,
    bool? isLoadingLocalDraft,
    bool? hasRecoveredDraft,
    DateTime? recoveredDraftUpdatedAt,
    PurchaseOrderMessage? storageWarning,
    bool? isDraftActionInProgress,
    bool clearSupplier = false,
    bool clearOrderDate = false,
    bool clearExpectedDeliveryDate = false,
    bool clearFailure = false,
    bool clearRedirectToDetail = false,
    bool clearSelectedCatalogItem = false,
    bool clearRecoveredDraft = false,
    bool clearStorageWarning = false,
    bool clearSavedDraft = false,
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
      isPlacing: isPlacing ?? this.isPlacing,
      isEditableDraft: isEditableDraft ?? this.isEditableDraft,
      failure: clearFailure ? null : (failure ?? this.failure),
      localMessage: clearFailure ? null : (localMessage ?? this.localMessage),
      savedDraft: clearSavedDraft ? null : (savedDraft ?? this.savedDraft),
      isSupplierLoadFailure:
          isSupplierLoadFailure ?? this.isSupplierLoadFailure,
      isLoadingEdit: isLoadingEdit ?? this.isLoadingEdit,
      redirectToDetailId: clearRedirectToDetail
          ? null
          : (redirectToDetailId ?? this.redirectToDetailId),
      isLoadingLocalDraft: isLoadingLocalDraft ?? this.isLoadingLocalDraft,
      hasRecoveredDraft:
          !clearRecoveredDraft && (hasRecoveredDraft ?? this.hasRecoveredDraft),
      recoveredDraftUpdatedAt: clearRecoveredDraft
          ? null
          : (recoveredDraftUpdatedAt ?? this.recoveredDraftUpdatedAt),
      storageWarning: clearStorageWarning
          ? null
          : (storageWarning ?? this.storageWarning),
      isDraftActionInProgress:
          isDraftActionInProgress ?? this.isDraftActionInProgress,
    );
  }

  bool get canPlace {
    final supplier = selectedSupplier;
    return isEditableDraft &&
        supplier != null &&
        supplier.isActive &&
        !supplier.isSystem &&
        lines.any(
          (line) => line.expectedQuantity > 0 && line.unitCost >= 0,
        );
  }
}
