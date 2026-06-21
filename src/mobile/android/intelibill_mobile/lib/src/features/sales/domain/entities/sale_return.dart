import 'package:equatable/equatable.dart';

class SaleReturnLineDraft extends Equatable {
  const SaleReturnLineDraft({
    required this.saleItemId,
    required this.selected,
    required this.quantity,
    this.lineType,
    this.condition,
    this.approvedRefundAmount,
    this.notes,
  });

  final String saleItemId;
  final bool selected;
  final double quantity;
  final String? lineType;
  final int? condition;
  final double? approvedRefundAmount;
  final String? notes;

  SaleReturnLineDraft copyWith({
    bool? selected,
    double? quantity,
    String? lineType,
    int? condition,
    double? approvedRefundAmount,
    String? notes,
    bool clearLineType = false,
    bool clearCondition = false,
    bool clearApprovedRefundAmount = false,
    bool clearNotes = false,
  }) {
    return SaleReturnLineDraft(
      saleItemId: saleItemId,
      selected: selected ?? this.selected,
      quantity: quantity ?? this.quantity,
      lineType: clearLineType ? null : (lineType ?? this.lineType),
      condition: clearCondition ? null : (condition ?? this.condition),
      approvedRefundAmount: clearApprovedRefundAmount
          ? null
          : (approvedRefundAmount ?? this.approvedRefundAmount),
      notes: clearNotes ? null : (notes ?? this.notes),
    );
  }

  Map<String, dynamic> toRequestMap() {
    return {
      'saleItemId': saleItemId,
      if (lineType != null) 'lineType': lineType,
      'quantity': quantity,
      if (condition != null) 'condition': condition,
      if (approvedRefundAmount != null)
        'approvedRefundAmount': approvedRefundAmount,
      if (notes != null && notes!.trim().isNotEmpty) 'notes': notes,
    };
  }

  @override
  List<Object?> get props => [
    saleItemId,
    selected,
    quantity,
    lineType,
    condition,
    approvedRefundAmount,
    notes,
  ];
}

class SaleReturnPreviewLineFinancial extends Equatable {
  const SaleReturnPreviewLineFinancial({
    required this.originalCostPrice,
    required this.originalSalesPrice,
    required this.originalTaxRatePercent,
    required this.originalIsPriceIncludingTax,
    required this.maxRefundAmount,
    required this.approvedRefundAmount,
    required this.taxableAmount,
    required this.taxAmount,
  });

  final double originalCostPrice;
  final double originalSalesPrice;
  final double originalTaxRatePercent;
  final bool originalIsPriceIncludingTax;
  final double maxRefundAmount;
  final double approvedRefundAmount;
  final double taxableAmount;
  final double taxAmount;

  @override
  List<Object?> get props => [
    originalCostPrice,
    originalSalesPrice,
    originalTaxRatePercent,
    originalIsPriceIncludingTax,
    maxRefundAmount,
    approvedRefundAmount,
    taxableAmount,
    taxAmount,
  ];
}

class SaleReturnPreviewLine extends Equatable {
  const SaleReturnPreviewLine({
    required this.saleItemId,
    this.itemId,
    this.inventoryBatchId,
    required this.requestedQuantity,
    required this.returnedQuantity,
    required this.returnableQuantity,
    this.condition,
    required this.willRestock,
    this.financial,
  });

  final String saleItemId;
  final String? itemId;
  final String? inventoryBatchId;
  final double requestedQuantity;
  final double returnedQuantity;
  final double returnableQuantity;
  final int? condition;
  final bool willRestock;
  final SaleReturnPreviewLineFinancial? financial;

  @override
  List<Object?> get props => [
    saleItemId,
    itemId,
    inventoryBatchId,
    requestedQuantity,
    returnedQuantity,
    returnableQuantity,
    condition,
    willRestock,
    financial,
  ];
}

class SaleReturnPreviewFinancial extends Equatable {
  const SaleReturnPreviewFinancial({
    required this.totalRefundAmount,
    required this.dueReductionAmount,
    required this.payoutAmount,
    required this.totalTaxableAmount,
    required this.totalTaxAmount,
    this.customerBalanceBefore,
    this.customerBalanceAfter,
  });

  final double totalRefundAmount;
  final double dueReductionAmount;
  final double payoutAmount;
  final double totalTaxableAmount;
  final double totalTaxAmount;
  final double? customerBalanceBefore;
  final double? customerBalanceAfter;

  @override
  List<Object?> get props => [
    totalRefundAmount,
    dueReductionAmount,
    payoutAmount,
    totalTaxableAmount,
    totalTaxAmount,
    customerBalanceBefore,
    customerBalanceAfter,
  ];
}

class SaleReturnPreview extends Equatable {
  const SaleReturnPreview({
    required this.saleId,
    required this.hasFinancialAccess,
    required this.lines,
    this.financial,
    required this.warnings,
  });

  final String saleId;
  final bool hasFinancialAccess;
  final List<SaleReturnPreviewLine> lines;
  final SaleReturnPreviewFinancial? financial;
  final List<String> warnings;

  @override
  List<Object?> get props => [
    saleId,
    hasFinancialAccess,
    lines,
    financial,
    warnings,
  ];
}

class PreviewSaleReturnRequest {
  const PreviewSaleReturnRequest({
    this.dueReductionOverrideAmount,
    this.dueOverrideReason,
    required this.items,
  });

  final double? dueReductionOverrideAmount;
  final String? dueOverrideReason;
  final List<SaleReturnLineDraft> items;

  Map<String, dynamic> toJson() {
    return {
      if (dueReductionOverrideAmount != null)
        'dueReductionOverrideAmount': dueReductionOverrideAmount,
      if (dueOverrideReason != null) 'dueOverrideReason': dueOverrideReason,
      'items': items.map((item) => item.toRequestMap()).toList(),
    };
  }
}

class RecordSaleReturnRequest {
  const RecordSaleReturnRequest({
    this.payoutDestination,
    this.payoutMethod,
    this.dueReductionOverrideAmount,
    this.dueOverrideReason,
    this.notes,
    this.creditNoteExpiresAt,
    this.creditNoteReason,
    required this.items,
  });

  final int? payoutDestination;
  final int? payoutMethod;
  final double? dueReductionOverrideAmount;
  final String? dueOverrideReason;
  final String? notes;
  final String? creditNoteExpiresAt;
  final String? creditNoteReason;
  final List<SaleReturnLineDraft> items;

  Map<String, dynamic> toJson() {
    return {
      if (payoutDestination != null) 'payoutDestination': payoutDestination,
      if (payoutMethod != null) 'payoutMethod': payoutMethod,
      if (dueReductionOverrideAmount != null)
        'dueReductionOverrideAmount': dueReductionOverrideAmount,
      if (dueOverrideReason != null) 'dueOverrideReason': dueOverrideReason,
      if (notes != null) 'notes': notes,
      if (creditNoteExpiresAt != null)
        'creditNoteExpiresAt': creditNoteExpiresAt,
      if (creditNoteReason != null) 'creditNoteReason': creditNoteReason,
      'items': items.map((item) => item.toRequestMap()).toList(),
    };
  }
}
