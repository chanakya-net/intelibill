import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_detail.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sales_history_query.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sellable.dart';

interface class SalesRepository {
  Future<SalesHistoryResult> getSalesHistory(SalesHistoryQuery query) {
    throw UnimplementedError();
  }

  Future<SaleDetail> getSaleDetail(String saleId) {
    throw UnimplementedError();
  }

  Future<List<Sellable>> searchSellables({
    String? searchTerm,
    String? barcode,
  }) {
    throw UnimplementedError();
  }
}
