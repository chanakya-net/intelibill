import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_detail.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_return.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sales_history_query.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sellable.dart';

interface class SalesRepository {
  Future<SalesHistoryResult> getSalesHistory(SalesHistoryQuery query) {
    throw UnimplementedError();
  }

  Future<SaleDetail> getSaleDetail(String saleId) {
    throw UnimplementedError();
  }

  Future<SaleReturnPreview> previewSaleReturn({
    required String saleId,
    required PreviewSaleReturnRequest request,
  }) {
    throw UnimplementedError();
  }

  Future<SaleDetail> recordSaleReturn({
    required String saleId,
    required RecordSaleReturnRequest request,
  }) {
    throw UnimplementedError();
  }

  Future<List<Sellable>> searchSellables({
    String? searchTerm,
    String? barcode,
  }) {
    throw UnimplementedError();
  }

  Future<void> voidSaleReturn({
    required String saleReturnId,
    required String reason,
  }) {
    throw UnimplementedError();
  }
}
