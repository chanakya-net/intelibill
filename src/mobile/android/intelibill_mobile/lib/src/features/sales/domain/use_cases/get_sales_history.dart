import 'package:intelibill_mobile/src/features/sales/domain/entities/sales_history_query.dart';
import 'package:intelibill_mobile/src/features/sales/domain/repositories/sales_repository.dart';

class GetSalesHistory {
  const GetSalesHistory(this._repository);

  final SalesRepository _repository;

  Future<SalesHistoryResult> call(SalesHistoryQuery query) {
    return _repository.getSalesHistory(query);
  }
}
