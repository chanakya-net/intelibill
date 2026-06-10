import 'package:intelibill_mobile/src/features/sales/data/dto/sale_list_item_dto.dart';
import 'package:intelibill_mobile/src/features/sales/data/dto/sales_history_response_dto.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_list_item.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sales_history_query.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sales_history_summary.dart';

class SaleListItemMapper {
  const SaleListItemMapper._();

  static SaleListItem toDomain(SaleListItemDto dto) {
    return SaleListItem(
      saleId: dto.saleId,
      invoiceNumber: dto.invoiceNumber,
      customerId: dto.customerId,
      paymentMethod: dto.paymentMethod,
      soldAt: dto.soldAt.toLocal(),
      paidAmount: dto.paidAmount,
      dueAmount: dto.dueAmount,
      totalBeforeDiscount: dto.totalBeforeDiscount,
      totalDiscountAmount: dto.totalDiscountAmount,
      totalAmount: dto.totalAmount,
      totalTaxAmount: dto.totalTaxAmount,
      customerName: dto.customerName,
      customerPhone: dto.customerPhone,
      itemCount: dto.itemCount,
      returnNumbers: dto.returnNumbers,
      status: dto.status,
      refundAmount: dto.refundAmount,
      dueReductionAmount: dto.dueReductionAmount,
    );
  }

  static SalesHistorySummary summaryToDomain(SalesHistorySummaryDto dto) {
    return SalesHistorySummary(
      periodSales: dto.periodSales,
      invoiceCount: dto.invoiceCount,
      refundAmount: dto.refundAmount,
    );
  }

  static SalesHistoryResult resultToDomain(SalesHistoryResponseDto dto) {
    return SalesHistoryResult(
      items: dto.items.map(toDomain).toList(),
      totalCount: dto.totalCount,
      pageNumber: dto.pageNumber,
      pageSize: dto.pageSize,
      summary: summaryToDomain(dto.summary),
    );
  }
}
