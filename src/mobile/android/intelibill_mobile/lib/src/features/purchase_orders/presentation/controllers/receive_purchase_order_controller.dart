import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/dashboard/presentation/controllers/dashboard_controller.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/entities/product_details.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/use_cases/get_product_details.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/utils/batch_number_generator.dart';
import 'package:intelibill_mobile/src/features/inventory/presentation/controllers/items_controller.dart';
import 'package:intelibill_mobile/src/features/inventory/presentation/controllers/inventory_batches_controller.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_line.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/receive_purchase_order_input.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_detail_controller.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_providers.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_orders_controller.dart';
import 'package:riverpod_annotation/riverpod_annotation.dart';

part 'receive_purchase_order_controller.g.dart';

@immutable
class ReceivePurchaseOrderLineDraft {
  const ReceivePurchaseOrderLineDraft({
    required this.purchaseOrderLineId,
    required this.description,
    required this.remainingQuantity,
    required this.quantity,
    required this.isSelected,
    required this.unitPurchaseCost,
    required this.barcode,
    required this.batchNumber,
    required this.totalPurchaseCost,
    required this.mrp,
    required this.salesPrice,
    required this.taxRatePercent,
    required this.taxIncluded,
    required this.purchaseTaxIncluded,
    this.expiryDate,
    this.manufacturingDate,
    this.dirtyFields = const {},
    this.invalidFields = const {},
    this.editVersion = 0,
  });

  final String purchaseOrderLineId;
  final String description;
  final int remainingQuantity;
  final int quantity;
  final bool isSelected;
  final double unitPurchaseCost;
  final String barcode;
  final String batchNumber;
  final double totalPurchaseCost;
  final double mrp;
  final double salesPrice;
  final double taxRatePercent;
  final bool taxIncluded;
  final bool purchaseTaxIncluded;
  final DateTime? expiryDate;
  final DateTime? manufacturingDate;
  final Set<String> dirtyFields;
  final Set<String> invalidFields;
  final int editVersion;

  ReceivePurchaseOrderLineDraft copyWith({
    int? quantity,
    bool? isSelected,
    String? barcode,
    String? batchNumber,
    double? unitPurchaseCost,
    double? totalPurchaseCost,
    double? mrp,
    double? salesPrice,
    double? taxRatePercent,
    bool? taxIncluded,
    bool? purchaseTaxIncluded,
    DateTime? expiryDate,
    DateTime? manufacturingDate,
    Set<String>? dirtyFields,
    Set<String>? invalidFields,
    int? editVersion,
    bool clearExpiryDate = false,
    bool clearManufacturingDate = false,
  }) {
    return ReceivePurchaseOrderLineDraft(
      purchaseOrderLineId: purchaseOrderLineId,
      description: description,
      remainingQuantity: remainingQuantity,
      quantity: quantity ?? this.quantity,
      isSelected: isSelected ?? this.isSelected,
      unitPurchaseCost: unitPurchaseCost ?? this.unitPurchaseCost,
      barcode: barcode ?? this.barcode,
      batchNumber: batchNumber ?? this.batchNumber,
      totalPurchaseCost: totalPurchaseCost ?? this.totalPurchaseCost,
      mrp: mrp ?? this.mrp,
      salesPrice: salesPrice ?? this.salesPrice,
      taxRatePercent: taxRatePercent ?? this.taxRatePercent,
      taxIncluded: taxIncluded ?? this.taxIncluded,
      purchaseTaxIncluded: purchaseTaxIncluded ?? this.purchaseTaxIncluded,
      expiryDate: clearExpiryDate ? null : (expiryDate ?? this.expiryDate),
      manufacturingDate: clearManufacturingDate
          ? null
          : (manufacturingDate ?? this.manufacturingDate),
      dirtyFields: dirtyFields ?? this.dirtyFields,
      invalidFields: invalidFields ?? this.invalidFields,
      editVersion: editVersion ?? this.editVersion,
    );
  }
}

@immutable
class ReceivePurchaseOrderState {
  const ReceivePurchaseOrderState({
    this.detail,
    this.referenceNumber = '',
    this.notes = '',
    this.receivedAt,
    this.lines = const [],
    this.barcodeGenerationLineIds = const {},
    this.barcodeGenerationFailures = const {},
    this.prefillLoadingLineIds = const {},
    this.prefillFailures = const {},
    this.isLoading = false,
    this.isSubmitting = false,
    this.failure,
    this.lineErrors = const {},
    this.expandedLineId,
    this.focusedLineId,
  });

  final PurchaseOrder? detail;
  final String referenceNumber;
  final String notes;
  final DateTime? receivedAt;
  final List<ReceivePurchaseOrderLineDraft> lines;
  final Set<String> barcodeGenerationLineIds;
  final Map<String, String> barcodeGenerationFailures;
  final Set<String> prefillLoadingLineIds;
  final Map<String, String> prefillFailures;
  final bool isLoading;
  final bool isSubmitting;
  final Failure? failure;
  final Map<String, Map<String, String>> lineErrors;
  final String? expandedLineId;
  final String? focusedLineId;

  bool get hasEligibleLines => lines.isNotEmpty;
  Iterable<ReceivePurchaseOrderLineDraft> get selectedLines =>
      lines.where((line) => line.isSelected);
  bool get hasSelectedLines => selectedLines.isNotEmpty;
  int get selectedLineCount => selectedLines.length;
  int get selectedQuantity =>
      selectedLines.fold(0, (sum, line) => sum + line.quantity);
  double get selectedPurchaseCost =>
      selectedLines.fold(0, (sum, line) => sum + line.totalPurchaseCost);
  bool get canSubmit => hasSelectedLines && !isSubmitting && detail != null;

  String? lineError(String lineId, String field) => lineErrors[lineId]?[field];

  ReceivePurchaseOrderState copyWith({
    PurchaseOrder? detail,
    String? referenceNumber,
    String? notes,
    DateTime? receivedAt,
    List<ReceivePurchaseOrderLineDraft>? lines,
    bool? isLoading,
    bool? isSubmitting,
    Failure? failure,
    Map<String, Map<String, String>>? lineErrors,
    Set<String>? barcodeGenerationLineIds,
    Map<String, String>? barcodeGenerationFailures,
    Set<String>? prefillLoadingLineIds,
    Map<String, String>? prefillFailures,
    String? expandedLineId,
    String? focusedLineId,
    bool clearFailure = false,
    bool clearDetail = false,
    bool clearReference = false,
    bool clearNotes = false,
    bool clearReceivedAt = false,
    bool clearLines = false,
    bool clearLineErrors = false,
    bool clearExpandedLine = false,
    bool clearFocusedLine = false,
    bool clearBarcodeGenerationLineIds = false,
    bool clearBarcodeGenerationFailures = false,
    bool clearPrefillLoading = false,
    bool clearPrefillFailures = false,
  }) {
    return ReceivePurchaseOrderState(
      detail: clearDetail ? null : (detail ?? this.detail),
      referenceNumber: clearReference
          ? ''
          : (referenceNumber ?? this.referenceNumber),
      notes: clearNotes ? '' : (notes ?? this.notes),
      receivedAt: clearReceivedAt ? null : (receivedAt ?? this.receivedAt),
      lines: clearLines ? const [] : (lines ?? this.lines),
      isLoading: isLoading ?? this.isLoading,
      isSubmitting: isSubmitting ?? this.isSubmitting,
      failure: clearFailure ? null : (failure ?? this.failure),
      lineErrors: clearLineErrors ? const {} : (lineErrors ?? this.lineErrors),
      barcodeGenerationLineIds: clearBarcodeGenerationLineIds
          ? const {}
          : (barcodeGenerationLineIds ?? this.barcodeGenerationLineIds),
      barcodeGenerationFailures: clearBarcodeGenerationFailures
          ? const {}
          : (barcodeGenerationFailures ?? this.barcodeGenerationFailures),
      prefillLoadingLineIds: clearPrefillLoading
          ? const {}
          : (prefillLoadingLineIds ?? this.prefillLoadingLineIds),
      prefillFailures: clearPrefillFailures
          ? const {}
          : (prefillFailures ?? this.prefillFailures),
      expandedLineId: clearExpandedLine
          ? null
          : (expandedLineId ?? this.expandedLineId),
      focusedLineId: clearFocusedLine
          ? null
          : (focusedLineId ?? this.focusedLineId),
    );
  }
}

@riverpod
class ReceivePurchaseOrderController extends _$ReceivePurchaseOrderController {
  static String Function(PurchaseOrderLine line) batchNumberGenerator =
      _generateBatch;

  late final String _purchaseOrderId;
  int _loadGeneration = 0;

  @override
  ReceivePurchaseOrderState build(String purchaseOrderId) {
    _purchaseOrderId = purchaseOrderId;
    const initial = ReceivePurchaseOrderState(isLoading: true);
    unawaited(_load(purchaseOrderId, currentState: initial));
    return initial;
  }

  Future<void> _load(
    String purchaseOrderId, {
    ReceivePurchaseOrderState? currentState,
    bool keepFailure = false,
  }) async {
    final generation = ++_loadGeneration;
    final baseline = currentState ?? state;
    state = baseline.copyWith(
      isLoading: true,
      clearFailure: !keepFailure,
      clearLines: true,
      clearLineErrors: true,
      clearExpandedLine: true,
      clearFocusedLine: true,
    );
    try {
      final detail = await ref.read(getPurchaseOrderProvider)(purchaseOrderId);
      if (!ref.mounted) return;
      state = state.copyWith(
        detail: detail,
        receivedAt: DateTime.now().toUtc(),
        lines: _buildDraftLines(detail.lines),
        isLoading: false,
        clearFailure: !keepFailure,
        clearLineErrors: true,
        clearExpandedLine: true,
        clearFocusedLine: true,
      );
      if (ref.mounted) {
        unawaited(_prefillCatalogBarcodes(detail.lines, generation));
        unawaited(_prefillLinesAsync(generation));
      }
    } on AppException catch (error) {
      if (!ref.mounted) return;
      state = state.copyWith(
        isLoading: false,
        failure: error.failure,
        clearDetail: error.failure is NotFoundFailure || state.detail == null,
        clearLines: true,
      );
    } on Object {
      if (!ref.mounted) return;
      state = state.copyWith(
        isLoading: false,
        failure: const Failure.unknown(),
        clearDetail: state.detail == null,
        clearLines: true,
      );
    }
  }

  Future<void> refresh() => _load(_purchaseOrderId, currentState: state);

  List<ReceivePurchaseOrderLineDraft> _buildDraftLines(
    List<PurchaseOrderLine> lines,
  ) {
    return lines
        .where((line) => line.remainingQuantity > 0)
        .map(
          (line) => ReceivePurchaseOrderLineDraft(
            purchaseOrderLineId: line.lineId,
            description: line.description,
            remainingQuantity: line.remainingQuantity,
            quantity: line.remainingQuantity,
            isSelected: true,
            unitPurchaseCost: line.unitCost,
            barcode: '',
            batchNumber: _buildBatchNumber(line),
            totalPurchaseCost: line.unitCost * line.remainingQuantity,
            mrp: 0,
            salesPrice: 0,
            taxRatePercent: 0,
            taxIncluded: false,
            purchaseTaxIncluded: false,
          ),
        )
        .toList(growable: false);
  }

  String _buildBatchNumber(PurchaseOrderLine line) =>
      batchNumberGenerator(line);

  Future<void> _prefillCatalogBarcodes(
    List<PurchaseOrderLine> purchaseOrderLines,
    int generation,
  ) async {
    final editVersions = {
      for (final line in state.lines)
        line.purchaseOrderLineId: line.editVersion,
    };
    try {
      final items = await ref.read(getItemsProvider)();
      if (!ref.mounted || generation != _loadGeneration) return;
      final barcodesByItemId = {
        for (final item in items)
          if (item.barcode.trim().isNotEmpty) item.itemId: item.barcode.trim(),
      };
      for (final purchaseOrderLine in purchaseOrderLines) {
        final barcode = barcodesByItemId[purchaseOrderLine.itemId];
        final draft = _lineDraftById(purchaseOrderLine.lineId);
        if (barcode == null ||
            draft == null ||
            draft.editVersion != editVersions[purchaseOrderLine.lineId] ||
            draft.dirtyFields.contains('barcode')) {
          continue;
        }
        _updateLine(
          purchaseOrderLine.lineId,
          (line) => line.copyWith(barcode: barcode),
        );
      }
    } on Object {
      // Catalog defaults are optional; receiving remains editable without them.
    }
  }

  Future<void> _prefillLinesAsync(int generation) async {
    final getProductDetails = ref.read(getProductDetailsProvider);
    final lines = List.of(state.lines);
    for (final line in lines) {
      _setPrefillLoading(line.purchaseOrderLineId, true, generation);
    }

    final futures = lines.map(
      (line) => _prefillLineAsync(line, getProductDetails, generation),
    );
    await Future.wait(futures, eagerError: false);
  }

  Future<void> _prefillLineAsync(
    ReceivePurchaseOrderLineDraft line,
    GetProductDetails getProductDetails,
    int generation,
  ) async {
    try {
      final details = await getProductDetails(name: line.description);
      if (!ref.mounted) return;
      _applyPrefill(
        line.purchaseOrderLineId,
        details,
        generation,
        line.editVersion,
      );
      _setPrefillLoading(line.purchaseOrderLineId, false, generation);
    } on Object catch (e) {
      if (!ref.mounted || generation != _loadGeneration) return;
      _setPrefillFailure(
        line.purchaseOrderLineId,
        e.toString(),
      );
      _setPrefillLoading(line.purchaseOrderLineId, false, generation);
    }
  }

  void _setPrefillLoading(String lineId, bool loading, int generation) {
    if (generation != _loadGeneration) return;
    if (!loading) {
      final loading = Set<String>.from(state.prefillLoadingLineIds);
      loading.remove(lineId);
      state = state.copyWith(prefillLoadingLineIds: loading);
    } else {
      state = state.copyWith(
        prefillLoadingLineIds: {...state.prefillLoadingLineIds, lineId},
      );
    }
  }

  void _setPrefillFailure(String lineId, String message) {
    final failures = Map<String, String>.from(state.prefillFailures);
    failures[lineId] = message;
    state = state.copyWith(prefillFailures: failures);
  }

  void _applyPrefill(
    String lineId,
    ProductDetails details,
    int generation,
    int editVersion,
  ) {
    if (generation != _loadGeneration) return;
    final line = _lineDraftById(lineId);
    if (line == null || line.editVersion != editVersion) return;

    _updateLine(lineId, (l) {
      var updated = l;
      if (!l.dirtyFields.contains('mrp') && l.mrp == 0) {
        updated = updated.copyWith(mrp: details.mrp);
      }
      if (!l.dirtyFields.contains('salesPrice') && l.salesPrice == 0) {
        updated = updated.copyWith(salesPrice: details.salesPrice);
      }
      if (!l.dirtyFields.contains('taxRatePercent') &&
          l.taxRatePercent == 0 &&
          details.taxRatePercent != null) {
        updated = updated.copyWith(taxRatePercent: details.taxRatePercent!);
      }
      if (!l.dirtyFields.contains('taxIncluded') &&
          !l.taxIncluded &&
          details.taxIncluded == true) {
        updated = updated.copyWith(taxIncluded: details.taxIncluded!);
      }
      return updated;
    });
  }

  void updateReferenceNumber(String value) {
    state = state.copyWith(
      referenceNumber: value,
      clearFailure: true,
    );
  }

  void updateNotes(String value) {
    state = state.copyWith(notes: value, clearFailure: true);
  }

  void updateReceivedAt(DateTime value) {
    state = state.copyWith(receivedAt: value.toUtc(), clearFailure: true);
  }

  void updateBarcode(String purchaseOrderLineId, String value) {
    _clearBarcodeGenerationFailure(purchaseOrderLineId);
    _updateLine(
      purchaseOrderLineId,
      (line) => line.copyWith(
        barcode: value,
        dirtyFields: {...line.dirtyFields, 'barcode'},
      ),
      isUserEdit: true,
    );
  }

  Future<String?> generateItemBarcodeForLine(String purchaseOrderLineId) async {
    if (state.barcodeGenerationLineIds.contains(purchaseOrderLineId)) {
      return null;
    }
    final previousLine = _lineDraftById(purchaseOrderLineId);
    if (previousLine == null) return null;

    final existingFailure = Map<String, String>.from(
      state.barcodeGenerationFailures,
    )..remove(purchaseOrderLineId);
    state = state.copyWith(
      barcodeGenerationLineIds: {
        ...state.barcodeGenerationLineIds,
        purchaseOrderLineId,
      },
      barcodeGenerationFailures: existingFailure,
      clearFailure: true,
    );

    try {
      final useCase = ref.read(generateItemBarcodeProvider);
      final generated = await useCase();
      if (!ref.mounted) return null;
      _clearBarcodeGenerationLine(purchaseOrderLineId);
      return generated.barcode.trim();
    } on AppException catch (error) {
      if (!ref.mounted) return null;
      state = state.copyWith(
        barcodeGenerationLineIds: state.barcodeGenerationLineIds
            .where((lineId) => lineId != purchaseOrderLineId)
            .toSet(),
        barcodeGenerationFailures: {
          ...state.barcodeGenerationFailures,
          purchaseOrderLineId:
              error.failure.message ??
              _barcodeGenerationFailureMessage(previousLine.barcode),
        },
      );
    } catch (_) {
      if (!ref.mounted) return null;
      state = state.copyWith(
        barcodeGenerationLineIds: state.barcodeGenerationLineIds
            .where((lineId) => lineId != purchaseOrderLineId)
            .toSet(),
        barcodeGenerationFailures: {
          ...state.barcodeGenerationFailures,
          purchaseOrderLineId: _barcodeGenerationFailureMessage(
            previousLine.barcode,
          ),
        },
      );
    }
    return null;
  }

  void applyGeneratedBarcode(String purchaseOrderLineId, String barcode) {
    _clearBarcodeGenerationFailure(purchaseOrderLineId);
    _updateLine(
      purchaseOrderLineId,
      (line) => line.copyWith(
        barcode: barcode,
        dirtyFields: {...line.dirtyFields, 'barcode'},
      ),
      isUserEdit: true,
    );
  }

  void _clearBarcodeGenerationLine(String purchaseOrderLineId) {
    state = state.copyWith(
      barcodeGenerationLineIds: state.barcodeGenerationLineIds
          .where((lineId) => lineId != purchaseOrderLineId)
          .toSet(),
    );
  }

  void _clearBarcodeGenerationFailure(String purchaseOrderLineId) {
    if (!state.barcodeGenerationFailures.containsKey(purchaseOrderLineId)) {
      return;
    }
    final cleared = Map<String, String>.from(state.barcodeGenerationFailures)
      ..remove(purchaseOrderLineId);
    state = state.copyWith(barcodeGenerationFailures: cleared);
  }

  String _barcodeGenerationFailureMessage(String existingBarcode) {
    return existingBarcode.isEmpty
        ? 'Could not generate barcode. Please try again.'
        : 'Could not generate barcode for this line. Your current value was not changed.';
  }

  void updateBatchNumber(String purchaseOrderLineId, String value) {
    _updateLine(
      purchaseOrderLineId,
      (line) => line.copyWith(batchNumber: value),
      isUserEdit: true,
    );
  }

  void setLineSelected(
    String purchaseOrderLineId, {
    required bool isSelected,
  }) {
    _updateLine(
      purchaseOrderLineId,
      (line) => line.copyWith(isSelected: isSelected),
      isUserEdit: true,
    );
  }

  void updateQuantity(String purchaseOrderLineId, String value) {
    final quantity = int.tryParse(value);
    final line = _lineDraftById(purchaseOrderLineId);
    if (line == null || quantity == null || quantity <= 0) {
      _setQuantityFailure();
      return;
    }
    if (quantity > line.remainingQuantity) {
      _setQuantityFailure();
      return;
    }
    _updateLine(
      purchaseOrderLineId,
      (line) => line.copyWith(
        quantity: quantity,
        totalPurchaseCost: line.unitPurchaseCost * quantity,
      ),
      isUserEdit: true,
    );
  }

  void updateUnitPurchaseCost(String purchaseOrderLineId, String value) {
    final cost = double.tryParse(value);
    _updateLine(
      purchaseOrderLineId,
      (line) => cost == null ? line : line.copyWith(unitPurchaseCost: cost),
      isUserEdit: true,
    );
  }

  void updateTotalPurchaseCost(String purchaseOrderLineId, String value) {
    final cost = double.tryParse(value);
    _updateLine(
      purchaseOrderLineId,
      (line) => cost == null ? line : line.copyWith(totalPurchaseCost: cost),
      isUserEdit: true,
    );
  }

  void updateMrp(String purchaseOrderLineId, String value) {
    _updateNumericField(
      purchaseOrderLineId,
      'mrp',
      value,
      (line, parsed) => line.copyWith(mrp: parsed),
    );
  }

  void updateSalesPrice(String purchaseOrderLineId, String value) {
    _updateNumericField(
      purchaseOrderLineId,
      'salesPrice',
      value,
      (line, parsed) => line.copyWith(salesPrice: parsed),
    );
  }

  void updateTaxRatePercent(String purchaseOrderLineId, String value) {
    _updateNumericField(
      purchaseOrderLineId,
      'taxRatePercent',
      value,
      (line, parsed) => line.copyWith(taxRatePercent: parsed),
    );
  }

  void updateTaxIncluded(String purchaseOrderLineId, bool value) {
    _updateLine(
      purchaseOrderLineId,
      (line) => line.copyWith(
        taxIncluded: value,
        dirtyFields: {...line.dirtyFields, 'taxIncluded'},
      ),
      isUserEdit: true,
    );
  }

  void updatePurchaseTaxIncluded(String purchaseOrderLineId, bool value) {
    _updateLine(
      purchaseOrderLineId,
      (line) => line.copyWith(purchaseTaxIncluded: value),
      isUserEdit: true,
    );
  }

  void updateExpiryDate(String purchaseOrderLineId, DateTime? value) {
    _updateLine(
      purchaseOrderLineId,
      (line) => line.copyWith(
        expiryDate: value,
        clearExpiryDate: value == null,
      ),
      isUserEdit: true,
    );
  }

  void updateManufacturingDate(String purchaseOrderLineId, DateTime? value) {
    _updateLine(
      purchaseOrderLineId,
      (line) => line.copyWith(
        manufacturingDate: value,
        clearManufacturingDate: value == null,
      ),
      isUserEdit: true,
    );
  }

  void _updateLine(
    String purchaseOrderLineId,
    ReceivePurchaseOrderLineDraft Function(ReceivePurchaseOrderLineDraft)
    update, {
    bool isUserEdit = false,
  }) {
    state = state.copyWith(
      lines: state.lines
          .map(
            (line) => line.purchaseOrderLineId == purchaseOrderLineId
                ? _updatedLine(line, update, isUserEdit)
                : line,
          )
          .toList(growable: false),
      clearFailure: true,
      clearLineErrors: true,
      clearExpandedLine: true,
      clearFocusedLine: true,
    );
  }

  ReceivePurchaseOrderLineDraft _updatedLine(
    ReceivePurchaseOrderLineDraft line,
    ReceivePurchaseOrderLineDraft Function(ReceivePurchaseOrderLineDraft)
    update,
    bool isUserEdit,
  ) {
    final updated = update(line);
    return isUserEdit
        ? updated.copyWith(editVersion: line.editVersion + 1)
        : updated;
  }

  void _updateNumericField(
    String lineId,
    String field,
    String value,
    ReceivePurchaseOrderLineDraft Function(
      ReceivePurchaseOrderLineDraft line,
      double parsed,
    )
    update,
  ) {
    final parsed = double.tryParse(value);
    _updateLine(lineId, (line) {
      final dirtyFields = {...line.dirtyFields, field};
      final invalidFields = {...line.invalidFields};
      if (parsed == null) {
        invalidFields.add(field);
        return line.copyWith(
          dirtyFields: dirtyFields,
          invalidFields: invalidFields,
        );
      }
      invalidFields.remove(field);
      return update(line, parsed).copyWith(
        dirtyFields: dirtyFields,
        invalidFields: invalidFields,
      );
    }, isUserEdit: true);
  }

  void _setQuantityFailure() {
    state = state.copyWith(
      failure: const Failure.validation(
        message: 'Quantity must be a positive whole number within remaining.',
      ),
    );
  }

  Future<PurchaseOrder?> submit() async {
    if (state.isSubmitting || state.detail == null) return null;
    if (!state.hasEligibleLines) {
      state = state.copyWith(
        failure: const Failure.validation(
          message: 'No remaining lines are available to receive.',
        ),
      );
      return null;
    }
    if (!state.hasSelectedLines) {
      state = state.copyWith(
        failure: const Failure.validation(
          message: 'Select at least one line to receive.',
        ),
      );
      return null;
    }

    final request = _buildRequest();
    if (request == null) return null;
    state = state.copyWith(isSubmitting: true, clearFailure: true);
    try {
      final updated = await ref.read(receivePurchaseOrderProvider)(
        state.detail!.purchaseOrderId,
        request,
      );
      if (!ref.mounted) return null;
      state = state.copyWith(
        detail: updated,
        lines: _buildDraftLines(updated.lines),
        isSubmitting: false,
        receivedAt: DateTime.now().toUtc(),
        clearFailure: true,
        clearLineErrors: true,
        clearExpandedLine: true,
        clearFocusedLine: true,
      );
      _invalidateDependentState();
      return updated;
    } on AppException catch (error) {
      await _finishFailure(error.failure);
    } on Object {
      await _finishFailure(const Failure.unknown());
    }
    return null;
  }

  void _invalidateDependentState() {
    ref.invalidate(purchaseOrdersControllerProvider);
    ref.invalidate(purchaseOrderDetailControllerProvider(_purchaseOrderId));
    ref.invalidate(dashboardControllerProvider);
    ref.invalidate(itemsControllerProvider);
    ref.invalidate(inventoryBatchesControllerProvider);
  }

  ReceivePurchaseOrderInput? _buildRequest() {
    final receivedAt = state.receivedAt ?? DateTime.now().toUtc();
    final selectedLines = state.selectedLines.toList(growable: false);
    if (selectedLines.isEmpty) {
      state = state.copyWith(
        failure: const Failure.validation(
          message: 'Select at least one line to receive.',
        ),
      );
      return null;
    }
    final ids = <String>{};
    final errors = <String, Map<String, String>>{};
    for (final line in selectedLines) {
      final lineErrors = _validateLine(line);
      if (!ids.add(line.purchaseOrderLineId)) {
        lineErrors['purchaseOrderLineId'] =
            'Duplicate purchase-order line IDs are not allowed.';
      }
      if (lineErrors.isNotEmpty) {
        errors[line.purchaseOrderLineId] = lineErrors;
      }
    }
    if (errors.isNotEmpty) {
      _setLineErrors(errors);
      return null;
    }
    return ReceivePurchaseOrderInput(
      referenceNumber: state.referenceNumber.isNotEmpty
          ? state.referenceNumber
          : null,
      notes: state.notes.isNotEmpty ? state.notes : null,
      receivedAt: receivedAt,
      lines: selectedLines
          .map(
            (line) => ReceivePurchaseOrderLineInput(
              purchaseOrderLineId: line.purchaseOrderLineId,
              barcode: line.barcode,
              batchNumber: line.batchNumber,
              quantity: line.quantity.toDouble(),
              totalPurchaseCost: line.totalPurchaseCost,
              unitCost: line.unitPurchaseCost,
              mrp: line.mrp,
              salesPrice: line.salesPrice,
              taxRatePercent: line.taxRatePercent,
              taxIncluded: line.taxIncluded,
              purchaseTaxIncluded: line.purchaseTaxIncluded,
              expiryDate: line.expiryDate,
              manufacturingDate: line.manufacturingDate,
            ),
          )
          .toList(growable: false),
    );
  }

  Map<String, String> _validateLine(ReceivePurchaseOrderLineDraft line) {
    final errors = <String, String>{};
    for (final field in line.invalidFields) {
      errors[field] = 'Enter a valid number.';
    }
    if (line.barcode.isNotEmpty)
      _maxLength(errors, 'barcode', line.barcode, 120);
    _require(errors, 'batchNumber', line.batchNumber, 80);
    if (line.quantity <= 0 || line.quantity > line.remainingQuantity) {
      errors['quantity'] = 'Quantity must be within the remaining amount.';
    }
    _nonNegative(errors, 'unitCost', line.unitPurchaseCost);
    _nonNegative(errors, 'totalPurchaseCost', line.totalPurchaseCost);
    _nonNegative(errors, 'mrp', line.mrp);
    _nonNegative(errors, 'salesPrice', line.salesPrice);
    if (line.salesPrice > line.mrp) {
      errors['salesPrice'] = 'Sales price cannot exceed MRP.';
    }
    if (line.taxRatePercent < 0 || line.taxRatePercent > 100) {
      errors['taxRatePercent'] = 'Tax rate must be between 0 and 100.';
    }
    if (line.manufacturingDate != null &&
        line.expiryDate != null &&
        line.manufacturingDate!.isAfter(line.expiryDate!)) {
      errors['expiryDate'] =
          'Expiry date must be on or after the manufacturing date.';
    }
    return errors;
  }

  void _require(
    Map<String, String> errors,
    String field,
    String value,
    int max,
  ) {
    if (value.trim().isEmpty) {
      errors[field] = 'This field is required.';
    } else if (value.length > max) {
      errors[field] = 'Must not exceed $max characters.';
    }
  }

  void _maxLength(
    Map<String, String> errors,
    String field,
    String value,
    int max,
  ) {
    if (value.length > max) {
      errors[field] = 'Must not exceed $max characters.';
    }
  }

  void _nonNegative(Map<String, String> errors, String field, double value) {
    if (value < 0) errors[field] = 'Must be non-negative.';
  }

  void _setLineErrors(Map<String, Map<String, String>> errors) {
    final first = state.selectedLines.firstWhere(
      (line) => errors.containsKey(line.purchaseOrderLineId),
    );
    final firstField = errors[first.purchaseOrderLineId]!.keys.first;
    state = state.copyWith(
      failure: const Failure.validation(
        message: 'Fix the highlighted receipt fields.',
      ),
      lineErrors: errors,
      expandedLineId: first.purchaseOrderLineId,
      focusedLineId: firstField,
    );
  }

  Future<void> _finishFailure(Failure failure) async {
    if (!ref.mounted) return;
    final errors = _mapValidationErrors(failure);
    if (errors.isEmpty) {
      state = state.copyWith(isSubmitting: false, failure: failure);
    } else {
      _setLineErrors(errors);
      state = state.copyWith(isSubmitting: false);
    }
    if (_isLifecycleConflict(failure)) {
      ref.invalidate(purchaseOrderDetailControllerProvider(_purchaseOrderId));
      await _load(_purchaseOrderId, currentState: state, keepFailure: true);
    }
    throw AppException(failure: failure);
  }

  bool _isLifecycleConflict(Failure failure) =>
      failure is ServerFailure && failure.statusCode == 409;

  Map<String, Map<String, String>> _mapValidationErrors(Failure failure) {
    if (failure is! ValidationFailure || failure.errors == null) return {};
    final selected = state.selectedLines.toList(growable: false);
    final mapped = <String, Map<String, String>>{};
    final path = RegExp(r'^Lines\[(\d+)\]\.(.+)$', caseSensitive: false);
    for (final entry in failure.errors!.entries) {
      final match = path.firstMatch(entry.key);
      if (match == null || entry.value.isEmpty) continue;
      final index = int.tryParse(match.group(1)!);
      final field = _fieldName(match.group(2)!);
      if (index == null || index >= selected.length || field == null) continue;
      mapped.putIfAbsent(selected[index].purchaseOrderLineId, () => {})[field] =
          entry.value.first;
    }
    return mapped;
  }

  String? _fieldName(String value) => switch (value.toLowerCase()) {
    'barcode' => 'barcode',
    'batchnumber' => 'batchNumber',
    'quantity' => 'quantity',
    'totalpurchasecost' => 'totalPurchaseCost',
    'unitcost' => 'unitCost',
    'mrp' => 'mrp',
    'salesprice' => 'salesPrice',
    'taxratepercent' => 'taxRatePercent',
    'taxincluded' => 'taxIncluded',
    'purchasetaxincluded' => 'purchaseTaxIncluded',
    'expirydate' => 'expiryDate',
    'manufacturingdate' => 'manufacturingDate',
    _ => null,
  };

  static String _generateBatch(PurchaseOrderLine line) {
    final entropy = line.lineId.hashCode + DateTime.now().millisecond;
    return generateBatchNumber(entropy: entropy);
  }

  ReceivePurchaseOrderLineDraft? _lineDraftById(String purchaseOrderLineId) {
    for (final line in state.lines) {
      if (line.purchaseOrderLineId == purchaseOrderLineId) return line;
    }
    return null;
  }
}
