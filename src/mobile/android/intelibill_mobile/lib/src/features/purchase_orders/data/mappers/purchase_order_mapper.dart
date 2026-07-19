import 'package:intelibill_mobile/src/features/purchase_orders/data/dto/create_purchase_order_draft_request_dto.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/dto/purchase_order_detail_dto.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/dto/purchase_order_list_item_dto.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/data/dto/purchase_order_page_dto.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_draft.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_line.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_list_item.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_page.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_receipt.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_receipt_line.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_status.dart';

class PurchaseOrderMapper {
  const PurchaseOrderMapper._();

  static PurchaseOrderListItem toDomain(PurchaseOrderListItemDto dto) {
    return PurchaseOrderListItem(
      purchaseOrderId: dto.purchaseOrderId,
      purchaseOrderNumber: dto.purchaseOrderNumber,
      status: PurchaseOrderStatus.fromWire(dto.status),
      supplierName: dto.supplierName,
      supplierReference: dto.supplierReference,
      lineCount: dto.lineCount,
      expectedQuantity: dto.expectedQuantity,
      receivedQuantity: dto.receivedQuantity,
      expectedTotal: dto.expectedTotal,
      createdAt: dto.createdAt.toLocal(),
    );
  }

  static PurchaseOrderPage pageToDomain(PurchaseOrderPageDto dto) {
    return PurchaseOrderPage(
      items: dto.items.map(toDomain).toList(),
      totalCount: dto.totalCount,
      pageNumber: dto.pageNumber,
      pageSize: dto.pageSize,
    );
  }

  static PurchaseOrderLine toLineDomain(PurchaseOrderLineDto dto) {
    return PurchaseOrderLine(
      lineId: dto.lineId,
      itemId: dto.itemId,
      description: dto.description,
      expectedQuantity: dto.expectedQuantity,
      receivedQuantity: dto.receivedQuantity,
      remainingQuantity: dto.remainingQuantity,
      unitCost: dto.unitCost,
      lineTotal: dto.lineTotal,
    );
  }

  static PurchaseOrderReceiptLine toReceiptLineDomain(
    PurchaseOrderReceiptLineDto dto,
  ) {
    return PurchaseOrderReceiptLine(
      receiptLineId: dto.receiptLineId,
      purchaseOrderLineId: dto.purchaseOrderLineId,
      itemId: dto.itemId,
      inventoryBatchId: dto.inventoryBatchId,
      batchNumber: dto.batchNumber,
      batchVoided: dto.batchVoided,
      stockTransactionId: dto.stockTransactionId,
      quantity: dto.quantity,
      totalPurchaseCost: dto.totalPurchaseCost,
      unitCost: dto.unitCost,
      mrp: dto.mrp,
      salesPrice: dto.salesPrice,
      taxRatePercent: dto.taxRatePercent,
      taxIncluded: dto.taxIncluded,
      purchaseTaxIncluded: dto.purchaseTaxIncluded,
    );
  }

  static PurchaseOrderReceipt toReceiptDomain(PurchaseOrderReceiptDto dto) {
    return PurchaseOrderReceipt(
      receiptId: dto.receiptId,
      receiptNumber: dto.receiptNumber,
      receivedAt: dto.receivedAt.toLocal(),
      referenceNumber: dto.referenceNumber,
      notes: dto.notes,
      receivedByUserId: dto.receivedByUserId,
      receivedByDisplayName: dto.receivedByDisplayName,
      lines: dto.lines.map(toReceiptLineDomain).toList(),
    );
  }

  static PurchaseOrder detailToDomain(PurchaseOrderDetailDto dto) {
    return PurchaseOrder(
      purchaseOrderId: dto.purchaseOrderId,
      purchaseOrderNumber: dto.purchaseOrderNumber,
      status: PurchaseOrderStatus.fromWire(dto.status),
      supplierId: dto.supplierId,
      orderDate: _parseDateOnly(dto.orderDate),
      expectedDeliveryDate: _parseDateOnly(dto.expectedDeliveryDate),
      supplierReferenceNumber: dto.supplierReferenceNumber,
      notes: dto.notes,
      lines: dto.lines.map(toLineDomain).toList(),
      expectedTotal: dto.expectedTotal,
      createdAt: dto.createdAt.toLocal(),
      supplierName: dto.supplierName,
      supplierReference: dto.supplierReference,
      receivedQuantity: dto.receivedQuantity,
      cancellationReason: dto.cancellationReason,
      closedAt: _parseDateTime(dto.closedAt),
      closedBy: dto.closedBy,
      closeReason: dto.closeReason,
      receipts: (dto.receipts ?? const [])
          .map(toReceiptDomain)
          .toList(growable: false),
    );
  }

  static DateTime? _parseDateTime(String? value) =>
      value == null ? null : DateTime.parse(value).toLocal();

  static final RegExp _dateOnlyPattern = RegExp(r'^(\d{4})-(\d{2})-(\d{2})$');

  /// Parses a backend `DateOnly` (`yyyy-MM-dd`) into a local calendar date.
  ///
  /// Rejects any non-calendar value (bad shape, year zero, or month/day out of
  /// range) with a [FormatException] instead of silently normalizing overflow
  /// components the way `DateTime.parse` would. Valid dates are preserved
  /// verbatim with no timezone conversion.
  static DateTime? _parseDateOnly(String? value) {
    if (value == null) return null;
    final match = _dateOnlyPattern.firstMatch(value);
    if (match == null) {
      throw FormatException('Invalid DateOnly value', value);
    }
    final year = int.parse(match.group(1)!);
    final month = int.parse(match.group(2)!);
    final day = int.parse(match.group(3)!);
    if (year < 1 ||
        month < 1 ||
        month > 12 ||
        day < 1 ||
        day > _daysInMonth(year, month)) {
      throw FormatException('Invalid DateOnly value', value);
    }
    return DateTime(year, month, day);
  }

  static int _daysInMonth(int year, int month) {
    const monthLengths = <int>[31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
    if (month == 2 && _isLeapYear(year)) return 29;
    return monthLengths[month - 1];
  }

  static bool _isLeapYear(int year) =>
      year % 4 == 0 && (year % 100 != 0 || year % 400 == 0);

  static CreatePurchaseOrderDraftLineRequestDto lineToRequestDto(
    PurchaseOrderDraftLine line,
  ) {
    return CreatePurchaseOrderDraftLineRequestDto(
      itemId: line.itemId,
      description: line.description,
      expectedQuantity: line.expectedQuantity,
      unitCost: line.unitCost,
    );
  }

  static CreatePurchaseOrderDraftRequestDto draftToRequestDto(
    PurchaseOrderDraft draft,
  ) {
    return CreatePurchaseOrderDraftRequestDto(
      supplierId: draft.supplierId,
      orderDate: draft.orderDate != null
          ? _formatDateOnly(draft.orderDate!)
          : null,
      expectedDeliveryDate: draft.expectedDeliveryDate != null
          ? _formatDateOnly(draft.expectedDeliveryDate!)
          : null,
      supplierReferenceNumber: draft.supplierReferenceNumber,
      notes: draft.notes,
      supplierName: draft.supplierName,
      supplierReference: draft.supplierReference,
      lines: draft.lines.map(lineToRequestDto).toList(),
    );
  }

  static String _formatDateOnly(DateTime date) =>
      '${date.year.toString().padLeft(4, '0')}-${date.month.toString().padLeft(2, '0')}-${date.day.toString().padLeft(2, '0')}';
}
