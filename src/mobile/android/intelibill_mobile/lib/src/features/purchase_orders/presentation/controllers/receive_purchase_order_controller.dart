import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
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
    double? totalPurchaseCost,
  }) {
    return ReceivePurchaseOrderLineDraft(
      purchaseOrderLineId: purchaseOrderLineId,
      description: description,
      remainingQuantity: remainingQuantity,
      quantity: quantity ?? this.quantity,
      isSelected: isSelected ?? this.isSelected,
      unitPurchaseCost: unitPurchaseCost,
      barcode: barcode ?? this.barcode,
      batchNumber: batchNumber ?? this.batchNumber,
      totalPurchaseCost: totalPurchaseCost ?? this.totalPurchaseCost,
      mrp: mrp,
      salesPrice: salesPrice,
      taxRatePercent: taxRatePercent,
      taxIncluded: taxIncluded,
      purchaseTaxIncluded: purchaseTaxIncluded,
      expiryDate: expiryDate,
      manufacturingDate: manufacturingDate,
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
    this.isLoading = false,
    this.isSubmitting = false,
    this.failure,
  });

  final PurchaseOrder? detail;
  final String referenceNumber;
  final String notes;
  final DateTime? receivedAt;
  final List<ReceivePurchaseOrderLineDraft> lines;
  final bool isLoading;
  final bool isSubmitting;
  final Failure? failure;

  bool get hasEligibleLines => lines.isNotEmpty;
  Iterable<ReceivePurchaseOrderLineDraft> get selectedLines =>
      lines.where((line) => line.isSelected);
  bool get hasSelectedLines => selectedLines.isNotEmpty;
  int get selectedLineCount => selectedLines.length;
  int get selectedQuantity =>
      selectedLines.fold(0, (sum, line) => sum + line.quantity);
  double get selectedPurchaseCost =>
      selectedLines.fold(0, (sum, line) => sum + line.totalPurchaseCost);
  bool get canSubmit =>
      hasSelectedLines && !isSubmitting && detail != null && failure == null;

  ReceivePurchaseOrderState copyWith({
    PurchaseOrder? detail,
    String? referenceNumber,
    String? notes,
    DateTime? receivedAt,
    List<ReceivePurchaseOrderLineDraft>? lines,
    bool? isLoading,
    bool? isSubmitting,
    Failure? failure,
    bool clearFailure = false,
    bool clearDetail = false,
    bool clearReference = false,
    bool clearNotes = false,
    bool clearReceivedAt = false,
    bool clearLines = false,
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
    state = state.copyWith(
      lines: state.lines
          .map(
            (line) => line.purchaseOrderLineId == purchaseOrderLineId
                ? line.copyWith(barcode: value)
                : line,
          )
          .toList(growable: false),
      clearFailure: true,
    );
  }

  void updateBatchNumber(String purchaseOrderLineId, String value) {
    state = state.copyWith(
      lines: state.lines
          .map(
            (line) => line.purchaseOrderLineId == purchaseOrderLineId
                ? line.copyWith(batchNumber: value)
                : line,
          )
          .toList(growable: false),
      clearFailure: true,
    );
  }

  void setLineSelected(
    String purchaseOrderLineId, {
    required bool isSelected,
  }) {
    state = state.copyWith(
      lines: state.lines
          .map(
            (line) => line.purchaseOrderLineId == purchaseOrderLineId
                ? line.copyWith(isSelected: isSelected)
                : line,
          )
          .toList(growable: false),
      clearFailure: true,
    );
  }

  void updateQuantity(String purchaseOrderLineId, String value) {
    final quantity = int.tryParse(value);
    final line = state.lines
        .where((item) => item.purchaseOrderLineId == purchaseOrderLineId)
        .firstOrNull;
    if (line == null || quantity == null || quantity <= 0) {
      _setQuantityFailure();
      return;
    }
    if (quantity > line.remainingQuantity) {
      _setQuantityFailure();
      return;
    }
    state = state.copyWith(
      lines: state.lines
          .map(
            (item) => item.purchaseOrderLineId == purchaseOrderLineId
                ? item.copyWith(
                    quantity: quantity,
                    totalPurchaseCost: item.unitPurchaseCost * quantity,
                  )
                : item,
          )
          .toList(growable: false),
      clearFailure: true,
    );
  }

  void _setQuantityFailure() {
    state = state.copyWith(
      failure: const Failure.validation(
        message: 'Quantity must be a positive whole number within remaining.',
      ),
    );
  }

  Future<void> submit() async {
    if (state.isSubmitting) return;
    if (state.detail == null) return;
    if (!state.hasEligibleLines) {
      state = state.copyWith(
        failure: const Failure.validation(
          message: 'No remaining lines are available to receive.',
        ),
      );
      return;
    }
    if (!state.hasSelectedLines) {
      state = state.copyWith(
        failure: const Failure.validation(
          message: 'Select at least one line to receive.',
        ),
      );
      return;
    }

    ReceivePurchaseOrderInput request;
    try {
      request = _buildRequest();
    } on AppException catch (error) {
      state = state.copyWith(failure: error.failure);
      return;
    }
    state = state.copyWith(isSubmitting: true, clearFailure: true);
    try {
      final updated = await ref.read(receivePurchaseOrderProvider)(
        state.detail!.purchaseOrderId,
        request,
      );
      if (!ref.mounted) return;
      ref.invalidate(purchaseOrdersControllerProvider);
      state = state.copyWith(
        detail: updated,
        lines: _buildDraftLines(updated.lines),
        isSubmitting: false,
        receivedAt: DateTime.now().toUtc(),
        clearFailure: true,
      );
    } on AppException catch (error) {
      await _finishFailure(error.failure);
    } on Object {
      await _finishFailure(const Failure.unknown());
    }
  }

  ReceivePurchaseOrderInput _buildRequest() {
    final receivedAt = state.receivedAt ?? DateTime.now().toUtc();
    final selectedLines = state.selectedLines.toList(growable: false);
    if (selectedLines.isEmpty) {
      throw AppException(
        failure: const Failure.validation(
          message: 'Select at least one line to receive.',
        ),
      );
    }
    final ids = <String>{};
    return ReceivePurchaseOrderInput(
      referenceNumber: state.referenceNumber.isNotEmpty
          ? state.referenceNumber
          : null,
      notes: state.notes.isNotEmpty ? state.notes : null,
      receivedAt: receivedAt,
      lines: selectedLines
          .map((line) {
            if (!ids.add(line.purchaseOrderLineId)) {
              throw AppException(
                failure: const Failure.validation(
                  message: 'Duplicate purchase-order line IDs are not allowed.',
                ),
              );
            }
            if (line.barcode.trim().isEmpty ||
                line.batchNumber.trim().isEmpty) {
              throw AppException(
                failure: const Failure.validation(
                  message: 'Barcode and batch number are required.',
                ),
              );
            }
            if (line.quantity <= 0 || line.quantity > line.remainingQuantity) {
              throw AppException(
                failure: const Failure.validation(
                  message: 'Quantity must be within the remaining amount.',
                ),
              );
            }
            return ReceivePurchaseOrderLineInput(
              purchaseOrderLineId: line.purchaseOrderLineId,
              barcode: line.barcode,
              batchNumber: line.batchNumber,
              quantity: line.quantity.toDouble(),
              totalPurchaseCost: line.totalPurchaseCost,
              mrp: line.mrp,
              salesPrice: line.salesPrice,
              taxRatePercent: line.taxRatePercent,
              taxIncluded: line.taxIncluded,
              purchaseTaxIncluded: line.purchaseTaxIncluded,
              expiryDate: line.expiryDate,
              manufacturingDate: line.manufacturingDate,
            );
          })
          .toList(growable: false),
    );
  }

  Future<void> _finishFailure(Failure failure) async {
    if (!ref.mounted) return;
    state = state.copyWith(isSubmitting: false, failure: failure);
    await _load(_purchaseOrderId, currentState: state, keepFailure: true);
    throw AppException(failure: failure);
  }

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
}
