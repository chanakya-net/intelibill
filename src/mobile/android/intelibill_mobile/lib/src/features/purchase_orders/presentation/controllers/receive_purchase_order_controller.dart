import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/inventory/presentation/controllers/items_controller.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_line.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/receive_purchase_order_input.dart';
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
            barcode: line.itemId,
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
    _updateLine(purchaseOrderLineId, (line) => line.copyWith(barcode: value));
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
      (line) => line.copyWith(barcode: barcode),
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
    );
  }

  void setLineSelected(
    String purchaseOrderLineId, {
    required bool isSelected,
  }) {
    _updateLine(
      purchaseOrderLineId,
      (line) => line.copyWith(isSelected: isSelected),
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
    );
  }

  void updateUnitPurchaseCost(String purchaseOrderLineId, String value) {
    final cost = double.tryParse(value);
    if (cost == null) return;
    _updateLine(
      purchaseOrderLineId,
      (line) => line.copyWith(unitPurchaseCost: cost),
    );
  }

  void updateTotalPurchaseCost(String purchaseOrderLineId, String value) {
    final cost = double.tryParse(value);
    if (cost == null) return;
    _updateLine(
      purchaseOrderLineId,
      (line) => line.copyWith(totalPurchaseCost: cost),
    );
  }

  void updateMrp(String purchaseOrderLineId, String value) {
    final mrp = double.tryParse(value);
    if (mrp == null) return;
    _updateLine(purchaseOrderLineId, (line) => line.copyWith(mrp: mrp));
  }

  void updateSalesPrice(String purchaseOrderLineId, String value) {
    final price = double.tryParse(value);
    if (price == null) return;
    _updateLine(
      purchaseOrderLineId,
      (line) => line.copyWith(salesPrice: price),
    );
  }

  void updateTaxRatePercent(String purchaseOrderLineId, String value) {
    final rate = double.tryParse(value);
    if (rate == null) return;
    _updateLine(
      purchaseOrderLineId,
      (line) => line.copyWith(taxRatePercent: rate),
    );
  }

  void updateTaxIncluded(String purchaseOrderLineId, bool value) {
    _updateLine(
      purchaseOrderLineId,
      (line) => line.copyWith(taxIncluded: value),
    );
  }

  void updatePurchaseTaxIncluded(String purchaseOrderLineId, bool value) {
    _updateLine(
      purchaseOrderLineId,
      (line) => line.copyWith(purchaseTaxIncluded: value),
    );
  }

  void updateExpiryDate(String purchaseOrderLineId, DateTime? value) {
    _updateLine(
      purchaseOrderLineId,
      (line) => line.copyWith(
        expiryDate: value,
        clearExpiryDate: value == null,
      ),
    );
  }

  void updateManufacturingDate(String purchaseOrderLineId, DateTime? value) {
    _updateLine(
      purchaseOrderLineId,
      (line) => line.copyWith(
        manufacturingDate: value,
        clearManufacturingDate: value == null,
      ),
    );
  }

  void _updateLine(
    String purchaseOrderLineId,
    ReceivePurchaseOrderLineDraft Function(ReceivePurchaseOrderLineDraft)
    update,
  ) {
    state = state.copyWith(
      lines: state.lines
          .map(
            (line) => line.purchaseOrderLineId == purchaseOrderLineId
                ? update(line)
                : line,
          )
          .toList(growable: false),
      clearFailure: true,
      clearLineErrors: true,
      clearExpandedLine: true,
      clearFocusedLine: true,
    );
  }

  void _setQuantityFailure() {
    state = state.copyWith(
      failure: const Failure.validation(
        message: 'Quantity must be a positive whole number within remaining.',
      ),
    );
  }

  Future<bool> submit() async {
    if (state.isSubmitting || state.detail == null) return false;
    if (!state.hasEligibleLines) {
      state = state.copyWith(
        failure: const Failure.validation(
          message: 'No remaining lines are available to receive.',
        ),
      );
      return false;
    }
    if (!state.hasSelectedLines) {
      state = state.copyWith(
        failure: const Failure.validation(
          message: 'Select at least one line to receive.',
        ),
      );
      return false;
    }

    final request = _buildRequest();
    if (request == null) return false;
    state = state.copyWith(isSubmitting: true, clearFailure: true);
    try {
      final updated = await ref.read(receivePurchaseOrderProvider)(
        state.detail!.purchaseOrderId,
        request,
      );
      if (!ref.mounted) return false;
      ref.invalidate(purchaseOrdersControllerProvider);
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
      return true;
    } on AppException catch (error) {
      await _finishFailure(error.failure);
    } on Object {
      await _finishFailure(const Failure.unknown());
    }
    return false;
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
    _require(errors, 'barcode', line.barcode, 120);
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
    throw AppException(failure: failure);
  }

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
    final date = DateTime.now();
    final dateLabel =
        '${date.year.toString().padLeft(4, '0')}'
        '${date.month.toString().padLeft(2, '0')}'
        '${date.day.toString().padLeft(2, '0')}';
    const chars = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
    final seed = line.lineId.hashCode + date.millisecond;
    final suffix = StringBuffer();
    for (var index = 0; index < 5; index++) {
      suffix.write(chars[(seed + index * 17).abs() % chars.length]);
    }
    return 'BN-$dateLabel-${suffix.toString()}';
  }

  ReceivePurchaseOrderLineDraft? _lineDraftById(String purchaseOrderLineId) {
    for (final line in state.lines) {
      if (line.purchaseOrderLineId == purchaseOrderLineId) return line;
    }
    return null;
  }
}
